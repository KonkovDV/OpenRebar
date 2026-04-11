using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using A101.Domain.Models;
using A101.Domain.Ports;

namespace A101.Infrastructure.Reporting;

/// <summary>
/// Stores reinforcement execution reports as indented UTF-8 JSON files.
/// </summary>
public sealed class JsonFileReportStore : IReportStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<StoredReportReference> SaveAsync(
        ReinforcementExecutionReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = JsonSerializer.SerializeToUtf8Bytes(report, SerializerOptions);
        await File.WriteAllBytesAsync(outputPath, payload, cancellationToken);

        return new StoredReportReference
        {
            OutputPath = Path.GetFullPath(outputPath),
            MediaType = "application/json",
            Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
            ByteCount = payload.LongLength
        };
    }
}