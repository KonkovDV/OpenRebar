using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using A101.Domain.Exceptions;
using A101.Domain.Models;
using A101.Domain.Ports;

namespace A101.Infrastructure.ImageProcessing;

/// <summary>
/// HTTP adapter that calls the Python FastAPI segmentation service.
/// Endpoint: POST /segment — uploads PNG, receives polygon zones.
/// </summary>
public sealed class HttpImageSegmentationService : IImageSegmentationService, IDisposable
{
    private const string CircuitOpenMessagePrefix = "ML segmentation circuit is open";

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
        _minArea = minArea;
        _maxRetryAttempts = Math.Max(1, maxRetryAttempts);
        _failureThreshold = Math.Max(1, failureThreshold);
        _circuitBreakDuration = TimeSpan.FromSeconds(Math.Max(1, circuitBreakSeconds));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _httpClient = messageHandler is null ? new HttpClient() : new HttpClient(messageHandler);
        _httpClient.BaseAddress = new Uri(baseUrl);
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

        ThrowIfCircuitOpen();

        try
        {
            var result = await ExecuteWithRetriesAsync(async ct =>
            {
                await EnsureServiceHealthyAsync(ct);

                using var content = new MultipartFormDataContent();
                var fileBytes = await File.ReadAllBytesAsync(imagePath, ct);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                content.Add(fileContent, "file", Path.GetFileName(imagePath));

                string requestPath = string.Create(
                    CultureInfo.InvariantCulture,
                    $"/segment?min_area={_minArea}");

                var response = await _httpClient.PostAsync(
                    requestPath,
                    content,
                    ct);

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<SegmentationResponseDto>(
                    JsonOptions,
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
        var response = await _httpClient.GetAsync("/health", ct);
        response.EnsureSuccessStatusCode();

        var health = await response.Content.ReadFromJsonAsync<HealthDto>(JsonOptions, ct);
        if (health?.Status != "ok")
            throw new ImageSegmentationServiceException(
                $"ML segmentation service is not ready. Status: {health?.Status ?? "unknown"}. " +
                "Ensure the model checkpoint is placed at ml/models/isoline_unet.pt");
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
        var result = new List<(Polygon, IsolineColor)>();

        foreach (var zone in zones)
        {
            if (zone.Polygon.Count < 3) continue;

            var vertices = zone.Polygon
                .Select(p => new Point2D(p[0], p[1]))
                .ToList();

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
