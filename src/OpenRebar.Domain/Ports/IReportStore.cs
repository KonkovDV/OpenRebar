using OpenRebar.Domain.Models;

namespace OpenRebar.Domain.Ports;

/// <summary>
/// Persists machine-readable reinforcement execution reports.
/// Allows CLI, plugin, or future integration hosts to store a canonical contract artifact.
/// </summary>
public interface IReportStore
{
    Task<StoredReportReference> SaveAsync(
        ReinforcementExecutionReport report,
        string outputPath,
        CancellationToken cancellationToken = default);
}

public sealed record StoredReportReference
{
    public required string OutputPath { get; init; }
    public required string MediaType { get; init; }
    public required string Sha256 { get; init; }
    public required long ByteCount { get; init; }
}