using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using A101.Domain.Models;
using A101.Domain.Ports;

namespace A101.Infrastructure.ImageProcessing;

/// <summary>
/// HTTP adapter that calls the Python FastAPI segmentation service.
/// Endpoint: POST /segment — uploads PNG, receives polygon zones.
/// </summary>
public sealed class HttpImageSegmentationService : IImageSegmentationService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly double _minArea;
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
        int timeoutSeconds = 120)
    {
        _minArea = minArea;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }

    public async Task<IReadOnlyList<(Polygon Boundary, IsolineColor DominantColor)>> SegmentAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image file not found: {imagePath}");

        // Check service health first
        await EnsureServiceHealthyAsync(cancellationToken);

        // Upload image as multipart form data
        using var content = new MultipartFormDataContent();
        var fileBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(imagePath));

        var response = await _httpClient.PostAsync(
            $"/segment?min_area={_minArea}",
            content,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SegmentationResponseDto>(
            JsonOptions, cancellationToken);

        if (result?.Zones is null)
            return [];

        return ConvertToPolygons(result.Zones);
    }

    private async Task EnsureServiceHealthyAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", ct);
            response.EnsureSuccessStatusCode();

            var health = await response.Content.ReadFromJsonAsync<HealthDto>(JsonOptions, ct);
            if (health?.Status != "ok")
                throw new InvalidOperationException(
                    $"ML segmentation service is not ready. Status: {health?.Status ?? "unknown"}. " +
                    "Ensure the model checkpoint is placed at ml/models/isoline_unet.pt");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Cannot connect to ML segmentation service at {_httpClient.BaseAddress}. " +
                "Start it with: uvicorn ml.src.api.server:app --host 0.0.0.0 --port 8101",
                ex);
        }
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

    public void Dispose() => _httpClient.Dispose();

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
