using A101.Domain.Models;

namespace A101.Application.UseCases;

/// <summary>
/// Executes the reinforcement pipeline across multiple slabs and aggregates success metrics.
/// </summary>
public sealed class BatchReinforcementPipeline
{
    private readonly GenerateReinforcementPipeline _singlePipeline;

    public BatchReinforcementPipeline(GenerateReinforcementPipeline singlePipeline)
    {
        _singlePipeline = singlePipeline;
    }

    public async Task<BatchResult> ExecuteAsync(
        IReadOnlyList<PipelineInput> inputs,
        CancellationToken ct = default)
    {
        var slabResults = new List<BatchSlabResult>();
        var failures = new List<BatchFailure>();

        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();

            string slabId = string.IsNullOrWhiteSpace(input.Metadata.SlabId)
                ? Path.GetFileNameWithoutExtension(input.IsolineFilePath)
                : input.Metadata.SlabId;

            try
            {
                var result = await _singlePipeline.ExecuteAsync(input, ct);
                slabResults.Add(new BatchSlabResult
                {
                    SlabId = slabId,
                    Result = result
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new BatchFailure
                {
                    SlabId = slabId,
                    ErrorMessage = ex.Message
                });
            }
        }

        return new BatchResult
        {
            SlabResults = slabResults,
            Failures = failures
        };
    }
}

public sealed record BatchSlabResult
{
    public required string SlabId { get; init; }
    public required PipelineResult Result { get; init; }
}

public sealed record BatchFailure
{
    public required string SlabId { get; init; }
    public required string ErrorMessage { get; init; }
}

public sealed class BatchResult
{
    public IReadOnlyList<BatchSlabResult> SlabResults { get; init; } = [];
    public IReadOnlyList<BatchFailure> Failures { get; init; } = [];

    public double TotalMassKg => SlabResults.Sum(r => r.Result.TotalMassKg);

    public double AverageWastePercent =>
        SlabResults.Count == 0 ? 0 : SlabResults.Average(r => r.Result.TotalWastePercent);

    public int TotalStockBars => SlabResults.Sum(r =>
        r.Result.OptimizationResults.Values.Sum(o => o.TotalStockBarsNeeded));
}