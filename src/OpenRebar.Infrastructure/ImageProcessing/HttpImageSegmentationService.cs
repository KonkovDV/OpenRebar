using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRebar.Domain.Exceptions;
using OpenRebar.Domain.Models;
using OpenRebar.Domain.Ports;

namespace OpenRebar.Infrastructure.ImageProcessing;

/// <summary>
/// HTTP adapter that calls the Python FastAPI segmentation service.
/// Endpoint: POST /segment — uploads PNG, receives polygon zones.
/// </summary>
public sealed class HttpImageSegmentationService : IImageSegmentationService, IDisposable
{
  private const string CircuitOpenMessagePrefix = "ML segmentation circuit is open";
  private const long MaxUploadBytes = 20L * 1024 * 1024;
  private const long MaxResponseBytes = 10L * 1024 * 1024;
  private const long MaxHealthResponseBytes = 64L * 1024;
  private const int MaxZones = 10_000;
  private const int MaxVerticesPerZone = 200_000;
  private const int MaxTotalVertices = 1_000_000;
  private const int MaxCoordinateMagnitude = 50_000_000;

  private readonly HttpClient _httpClient;
  private readonly double _minArea;
  private readonly int _maxRetryAttempts;
  private readonly int _failureThreshold;
  private readonly TimeSpan _circuitBreakDuration;
  private readonly TimeProvider _timeProvider;
  private readonly object _stateLock = new();
  private readonly bool _ownsHttpClient;
  private int _consecutiveFailures;
  private DateTimeOffset? _circuitOpenUntilUtc;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString
  };

  /// <param name="baseUrl">ML service base URL (default: http://localhost:8101).</param>
  /// <param name="minArea">Minimum polygon area in pixels (passed to the ML service).</param>
  /// <param name="timeoutSeconds">HTTP request timeout.</param>
  public HttpImageSegmentationService(
      string baseUrl = "http://localhost:8101",
      double minArea = 1000.0,
      int timeoutSeconds = 120,
      int maxRetryAttempts = 2,
      int failureThreshold = 3,
      int circuitBreakSeconds = 30,
      TimeProvider? timeProvider = null,
      HttpMessageHandler? messageHandler = null)
  {
    ValidateConfiguration(baseUrl, minArea, timeoutSeconds);
    _minArea = minArea;
    _maxRetryAttempts = Math.Max(1, maxRetryAttempts);
    _failureThreshold = Math.Max(1, failureThreshold);
    _circuitBreakDuration = TimeSpan.FromSeconds(Math.Max(1, circuitBreakSeconds));
    _timeProvider = timeProvider ?? TimeProvider.System;
    _httpClient = messageHandler is null ? new HttpClient() : new HttpClient(messageHandler);
    _httpClient.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    _ownsHttpClient = true;
  }

  internal HttpImageSegmentationService(
      HttpClient httpClient,
      double minArea = 1000.0,
      int maxRetryAttempts = 2,
      int failureThreshold = 3,
      int circuitBreakSeconds = 30,
      TimeProvider? timeProvider = null)
  {
    if (!double.IsFinite(minArea) || minArea < 0)
      throw new ArgumentOutOfRangeException(nameof(minArea));
    _httpClient = httpClient;
    _minArea = minArea;
    _maxRetryAttempts = Math.Max(1, maxRetryAttempts);
    _failureThreshold = Math.Max(1, failureThreshold);
    _circuitBreakDuration = TimeSpan.FromSeconds(Math.Max(1, circuitBreakSeconds));
    _timeProvider = timeProvider ?? TimeProvider.System;
    _ownsHttpClient = false;
  }

  public async Task<IReadOnlyList<(Polygon Boundary, IsolineColor DominantColor)>> SegmentAsync(
      string imagePath,
      CancellationToken cancellationToken = default)
  {
    if (!File.Exists(imagePath))
      throw new FileNotFoundException($"Image file not found: {imagePath}");
    var fileInfo = new FileInfo(imagePath);
    if (fileInfo.Length > MaxUploadBytes)
      throw new ImageSegmentationServiceException(
          $"Image file exceeds the {MaxUploadBytes} byte ML upload limit: {fileInfo.Length} bytes.");

    ThrowIfCircuitOpen();

    try
    {
      var result = await ExecuteWithRetriesAsync(async ct =>
      {
        await EnsureServiceHealthyAsync(ct);

        using var content = new MultipartFormDataContent();
        await using var fileStream = new FileStream(
            imagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(imagePath));

        string requestPath = string.Create(
                  CultureInfo.InvariantCulture,
                  $"/segment?min_area={_minArea}");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestPath)
        {
          Content = content
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        return await ReadJsonLimitedAsync<SegmentationResponseDto>(
            response.Content,
            MaxResponseBytes,
            ct);
      }, cancellationToken);

      RecordSuccess();

      if (result?.Zones is null)
        return [];

      return ConvertToPolygons(result.Zones);
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
    {
      if (!IsCircuitOpenException(ex))
        RecordFailure();

      throw WrapServiceException(ex);
    }
  }

  private async Task EnsureServiceHealthyAsync(CancellationToken ct)
  {
    using var response = await _httpClient.GetAsync(
        "/health",
        HttpCompletionOption.ResponseHeadersRead,
        ct);
    response.EnsureSuccessStatusCode();

    var health = await ReadJsonLimitedAsync<HealthDto>(
        response.Content,
        MaxHealthResponseBytes,
        ct);
    if (health?.Status != "ok")
      throw new ImageSegmentationServiceException(
          $"ML segmentation service is not ready. Status: {health?.Status ?? "unknown"}. " +
          "Ensure the model checkpoint is placed at ml/models/isoline_unet.pt");
  }

  private static async Task<T?> ReadJsonLimitedAsync<T>(
      HttpContent content,
      long maxBytes,
      CancellationToken ct)
  {
    if (content.Headers.ContentLength is long declared && declared > maxBytes)
      throw new ImageSegmentationServiceException(
          $"ML service response exceeds the {maxBytes} byte limit.");

    await using var source = await content.ReadAsStreamAsync(ct);
    await using var buffer = new MemoryStream(capacity: (int)Math.Min(maxBytes, 64 * 1024));
    var chunk = new byte[64 * 1024];
    long totalBytes = 0;

    while (true)
    {
      int bytesRead = await source.ReadAsync(chunk.AsMemory(), ct);
      if (bytesRead == 0)
        break;

      totalBytes += bytesRead;
      if (totalBytes > maxBytes)
        throw new ImageSegmentationServiceException(
            $"ML service response exceeds the {maxBytes} byte limit.");

      await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), ct);
    }

    buffer.Position = 0;
    return await JsonSerializer.DeserializeAsync<T>(buffer, JsonOptions, ct);
  }

  private static void ValidateConfiguration(string baseUrl, double minArea, int timeoutSeconds)
  {
    if (!double.IsFinite(minArea) || minArea < 0)
      throw new ArgumentOutOfRangeException(nameof(minArea));
    if (timeoutSeconds <= 0)
      throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
    if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        || !string.IsNullOrEmpty(uri.UserInfo))
    {
      throw new ArgumentException(
          "ML service URL must be an absolute HTTP(S) URL without user information.",
          nameof(baseUrl));
    }
  }

  private async Task<T> ExecuteWithRetriesAsync<T>(
      Func<CancellationToken, Task<T>> operation,
      CancellationToken cancellationToken)
  {
    Exception? lastError = null;

    for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
    {
      try
      {
        return await operation(cancellationToken);
      }
      catch (Exception ex) when (IsRetryable(ex, cancellationToken) && attempt < _maxRetryAttempts)
      {
        lastError = ex;
      }
    }

    throw lastError ?? new ImageSegmentationServiceException("ML segmentation request failed.");
  }

  private static bool IsRetryable(Exception ex, CancellationToken cancellationToken)
  {
    return ex switch
    {
      HttpRequestException => true,
      TaskCanceledException when !cancellationToken.IsCancellationRequested => true,
      _ => false
    };
  }

  private void ThrowIfCircuitOpen()
  {
    lock (_stateLock)
    {
      var now = _timeProvider.GetUtcNow();
      if (_circuitOpenUntilUtc.HasValue && _circuitOpenUntilUtc.Value > now)
      {
        throw new ImageSegmentationServiceException(
            $"{CircuitOpenMessagePrefix} until {_circuitOpenUntilUtc.Value:O}. " +
            "The Python segmentation service is in cooldown after repeated failures.");
      }

      if (_circuitOpenUntilUtc.HasValue && _circuitOpenUntilUtc.Value <= now)
      {
        _circuitOpenUntilUtc = null;
        _consecutiveFailures = 0;
      }
    }
  }

  private void RecordSuccess()
  {
    lock (_stateLock)
    {
      _consecutiveFailures = 0;
      _circuitOpenUntilUtc = null;
    }
  }

  private void RecordFailure()
  {
    lock (_stateLock)
    {
      _consecutiveFailures++;
      if (_consecutiveFailures >= _failureThreshold)
        _circuitOpenUntilUtc = _timeProvider.GetUtcNow().Add(_circuitBreakDuration);
    }
  }

  private ImageSegmentationServiceException WrapServiceException(Exception ex)
  {
    if (ex is ImageSegmentationServiceException imageSegmentationServiceException)
      return imageSegmentationServiceException;

    if (ex is HttpRequestException or TaskCanceledException)
    {
      return new ImageSegmentationServiceException(
          $"Cannot connect to ML segmentation service at {_httpClient.BaseAddress}. " +
          "Start it with: uvicorn ml.src.api.server:app --host 0.0.0.0 --port 8101",
          ex);
    }

    return new ImageSegmentationServiceException(ex.Message, ex);
  }

  private static bool IsCircuitOpenException(Exception ex)
  {
    return ex is ImageSegmentationServiceException imageSegmentationServiceException
        && imageSegmentationServiceException.Message.StartsWith(CircuitOpenMessagePrefix, StringComparison.Ordinal);
  }

  private static IReadOnlyList<(Polygon Boundary, IsolineColor DominantColor)> ConvertToPolygons(
      IReadOnlyList<PolygonZoneDto> zones)
  {
    if (zones.Count > MaxZones)
      throw new ImageSegmentationServiceException(
          $"ML response contains {zones.Count} zones; limit is {MaxZones}.");

    var result = new List<(Polygon, IsolineColor)>();
    int totalVertices = 0;

    foreach (var zone in zones)
    {
      if (zone.ClassId is < 1 or > 7)
        throw new ImageSegmentationServiceException($"ML response has unknown class_id {zone.ClassId}.");
      if (!double.IsFinite(zone.Area) || zone.Area < 0 || zone.Bbox.Length != 4)
        throw new ImageSegmentationServiceException("ML response has invalid area or bbox metadata.");
      if (zone.Polygon.Count < 3 || zone.Polygon.Count > MaxVerticesPerZone)
        throw new ImageSegmentationServiceException(
            $"ML polygon vertex count {zone.Polygon.Count} is outside the allowed range.");

      totalVertices = checked(totalVertices + zone.Polygon.Count);
      if (totalVertices > MaxTotalVertices)
        throw new ImageSegmentationServiceException(
            $"ML response exceeds the {MaxTotalVertices} total vertex limit.");

      var vertices = new List<Point2D>(zone.Polygon.Count);
      foreach (var point in zone.Polygon)
      {
        if (point.Length != 2
            || Math.Abs((long)point[0]) > MaxCoordinateMagnitude
            || Math.Abs((long)point[1]) > MaxCoordinateMagnitude)
        {
          throw new ImageSegmentationServiceException(
              "ML response contains an invalid or out-of-range polygon point.");
        }
        vertices.Add(new Point2D(point[0], point[1]));
      }

      var polygon = new Polygon(vertices);

      // Map class_id to a representative color (placeholder — in production,
      // the color would come from the legend or the ML service would return it).
      var color = ClassIdToColor(zone.ClassId);

      result.Add((polygon, color));
    }

    return result;
  }

  /// <summary>
  /// Map ML class ID to a representative IsolineColor.
  /// Class IDs correspond to reinforcement spec tiers in the training legend.
  /// In production, this mapping should come from the training config.
  /// </summary>
  private static IsolineColor ClassIdToColor(int classId) => classId switch
  {
    1 => new IsolineColor(255, 0, 0),       // Red — high reinforcement
    2 => new IsolineColor(255, 165, 0),     // Orange
    3 => new IsolineColor(255, 255, 0),     // Yellow
    4 => new IsolineColor(0, 255, 0),       // Green
    5 => new IsolineColor(0, 255, 255),     // Cyan
    6 => new IsolineColor(0, 0, 255),       // Blue — low reinforcement
    7 => new IsolineColor(255, 0, 255),     // Magenta — special zones
    _ => new IsolineColor(128, 128, 128),   // Gray — unknown
  };

  public void Dispose()
  {
    if (_ownsHttpClient)
      _httpClient.Dispose();
  }

  // DTOs for JSON deserialization (matches Python FastAPI response models)
  private sealed record HealthDto
  {
    [JsonPropertyName("status")]
    public string? Status { get; init; }
  }

  private sealed record SegmentationResponseDto
  {
    [JsonPropertyName("zones")]
    public IReadOnlyList<PolygonZoneDto> Zones { get; init; } = [];

    [JsonPropertyName("total_zones")]
    public int TotalZones { get; init; }
  }

  private sealed record PolygonZoneDto
  {
    [JsonPropertyName("class_id")]
    public int ClassId { get; init; }

    [JsonPropertyName("polygon")]
    public IReadOnlyList<int[]> Polygon { get; init; } = [];

    [JsonPropertyName("area")]
    public double Area { get; init; }

    [JsonPropertyName("bbox")]
    public int[] Bbox { get; init; } = [];
  }
}
