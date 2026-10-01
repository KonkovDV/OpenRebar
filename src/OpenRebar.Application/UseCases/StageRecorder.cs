using System.Diagnostics;
using OpenRebar.Domain.Models;

namespace OpenRebar.Application.UseCases;

/// <summary>
/// Records pipeline stages in a fixed order. Stages that never ran are Skipped.
/// </summary>
public sealed class StageRecorder
{
  public static readonly string[] Canonical =
  [
      "Parse",
      "Layer",
      "ZoneDetection",
      "RebarCalculation",
      "Verification",
      "Optimization",
      "Placement"
  ];

  private readonly List<StageExecutionReport> _done = [];
  private readonly Stopwatch _clock = Stopwatch.StartNew();
  private long _markTicks;

  public void Complete(
      string name,
      string? algorithmId = null,
      int processed = 0,
      int? max = null,
      IReadOnlyList<StageReasonReport>? reasons = null,
      bool partial = false,
      bool budgetExhausted = false)
  {
    Add(name, partial ? "Incomplete" : "Complete", complete: !partial, algorithmId, processed, max, partial, budgetExhausted, reasons);
  }

  public void Fail(string name, StageReasonReport reason, string? algorithmId = null, bool budgetExhausted = false)
  {
    Add(name, "Failed", complete: false, algorithmId, processed: 0, max: null, partial: true, budgetExhausted, [reason]);
  }

  public void Skip(string name)
  {
    Add(name, "Skipped", complete: false, algorithmId: null, processed: 0, max: null, partial: false, budgetExhausted: false, reasons: null);
  }

  public IReadOnlyList<StageExecutionReport> Seal()
  {
    var map = _done
        .GroupBy(stage => stage.Name, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

    return Canonical
        .Select(name => map.TryGetValue(name, out var stage) ? stage : Skipped(name))
        .ToList();
  }

  public static StageReasonReport Reason(ReasonCode code, string message, bool retryable = false, string? context = null)
      => new()
      {
        Code = code.ToWire(),
        Retryable = retryable,
        Message = message,
        Context = context
      };

  private void Add(
      string name,
      string status,
      bool complete,
      string? algorithmId,
      int processed,
      int? max,
      bool partial,
      bool budgetExhausted,
      IReadOnlyList<StageReasonReport>? reasons)
  {
    long now = _clock.ElapsedTicks;
    double durationMs = (now - _markTicks) * 1000.0 / Stopwatch.Frequency;
    _markTicks = now;
    _done.Add(new StageExecutionReport
    {
      Name = name,
      Status = status,
      Complete = complete,
      BudgetExhausted = budgetExhausted,
      PartialResults = partial,
      DurationMs = durationMs,
      AlgorithmId = algorithmId,
      Counters = new StageCounters { Processed = processed, Max = max },
      Reasons = reasons ?? []
    });
  }

  private static StageExecutionReport Skipped(string name) => new()
  {
    Name = name,
    Status = "Skipped",
    Complete = false,
    DurationMs = 0,
    Counters = new StageCounters()
  };
}
