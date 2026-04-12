using A101.Domain.Exceptions;
using A101.Infrastructure.ImageProcessing;
using A101.Domain.Models;
using FluentAssertions;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;

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
            await act.Should().ThrowAsync<ImageSegmentationServiceException>()
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

    [Fact]
    public async Task SegmentAsync_AfterRepeatedFailures_ShouldOpenCircuit()
    {
        var handler = new CountingHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        using var service = new HttpImageSegmentationService(
            baseUrl: "http://ml-server:9000",
            maxRetryAttempts: 1,
            failureThreshold: 2,
            circuitBreakSeconds: 60,
            messageHandler: handler);

        var tmpFile = Path.GetTempFileName() + ".png";
        await File.WriteAllBytesAsync(tmpFile, [0x89, 0x50, 0x4E, 0x47]);

        try
        {
            await service.Invoking(sut => sut.SegmentAsync(tmpFile)).Should().ThrowAsync<ImageSegmentationServiceException>()
                .WithMessage("*Cannot connect to ML segmentation service*");

            await service.Invoking(sut => sut.SegmentAsync(tmpFile)).Should().ThrowAsync<ImageSegmentationServiceException>()
                .WithMessage("*Cannot connect to ML segmentation service*");

            await service.Invoking(sut => sut.SegmentAsync(tmpFile)).Should().ThrowAsync<ImageSegmentationServiceException>()
                .WithMessage("*circuit is open*");

            handler.RequestCount.Should().Be(2, "the third call should be short-circuited without another HTTP attempt");
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task SegmentAsync_ShouldUseInvariantCultureForMinAreaQueryParameter()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;

        var handler = new CountingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/health")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json")
                };
            }

            request.RequestUri!.AbsolutePath.Should().Be("/segment");
            request.RequestUri.Query.Should().Contain("min_area=1000.5");
            request.RequestUri.Query.Should().NotContain(",");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"zones\":[],\"total_zones\":0}", Encoding.UTF8, "application/json")
            };
        });

        using var service = new HttpImageSegmentationService(
            baseUrl: "http://ml-server:9000",
            minArea: 1000.5,
            maxRetryAttempts: 1,
            messageHandler: handler);

        var tmpFile = Path.GetTempFileName() + ".png";
        await File.WriteAllBytesAsync(tmpFile, [0x89, 0x50, 0x4E, 0x47]);

        try
        {
            var result = await service.SegmentAsync(tmpFile);

            result.Should().BeEmpty();
            handler.RequestCount.Should().Be(2, "health and segmentation requests should both complete successfully");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            File.Delete(tmpFile);
        }
    }

    private sealed class CountingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responder(request));
        }
    }

    public void Dispose() => _sut.Dispose();
}
