using A101.Infrastructure.ImageProcessing;
using A101.Domain.Models;
using FluentAssertions;

namespace A101.Infrastructure.Tests.ImageProcessing;

/// <summary>
/// Tests for HttpImageSegmentationService — verifies contract, error handling,
/// and health-check behavior without requiring a running ML server.
/// </summary>
public sealed class HttpImageSegmentationServiceTests : IDisposable
{
    private readonly HttpImageSegmentationService _sut;

    public HttpImageSegmentationServiceTests()
    {
        // Point to a non-existent server — tests verify error handling
        _sut = new HttpImageSegmentationService("http://localhost:19999");
    }

    [Fact]
    public async Task SegmentAsync_Throws_WhenFileNotFound()
    {
        var act = () => _sut.SegmentAsync("/nonexistent/path.png");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task SegmentAsync_Throws_WhenServiceUnavailable()
    {
        // Create a temporary file to bypass file-not-found check
        var tmpFile = Path.GetTempFileName() + ".png";
        await File.WriteAllBytesAsync(tmpFile, [0x89, 0x50, 0x4E, 0x47]); // PNG magic bytes

        try
        {
            var act = () => _sut.SegmentAsync(tmpFile);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Cannot connect to ML segmentation service*");
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Constructor_AcceptsCustomParameters()
    {
        using var service = new HttpImageSegmentationService(
            baseUrl: "http://ml-server:9000",
            minArea: 5000.0,
            timeoutSeconds: 60);

        // No exception = success. Service is just configured, not connected.
        service.Should().NotBeNull();
    }

    public void Dispose() => _sut.Dispose();
}
