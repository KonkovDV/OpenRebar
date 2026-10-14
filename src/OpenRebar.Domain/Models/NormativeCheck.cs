using System.Text.Json.Serialization;

namespace OpenRebar.Domain.Models;

/// <summary>Outcome of one named check. A missing input stays NotEvaluated.</summary>
public enum CheckStatus
{
  Passed,
  Failed,
  NotEvaluated
}

/// <summary>One check shown in the execution report.</summary>
public sealed record NormativeCheck
{
  public required string Id { get; init; }
  public required CheckStatus Status { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Reason { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Clause { get; init; }

  public static IReadOnlyList<NormativeCheck> Build(
      ReinforcementVerificationResult? verification,
      bool hasLaps)
  {
    var checks = new List<NormativeCheck>
    {
      ProvidedArea(verification),
      new()
      {
        Id = "crackWidth",
        Status = CheckStatus.NotEvaluated,
        Reason = "inputIsRequiredAreaOnly"
      },
      new()
      {
        Id = "deflection",
        Status = CheckStatus.NotEvaluated,
        Reason = "inputIsRequiredAreaOnly"
      }
    };

    if (hasLaps)
    {
      checks.Add(new NormativeCheck
      {
        Id = "transverseInLap",
        Status = CheckStatus.NotEvaluated,
        Reason = "Q-2b"
      });
    }

    return checks;
  }

  private static NormativeCheck ProvidedArea(ReinforcementVerificationResult? verification)
  {
    if (verification is null)
    {
      return new NormativeCheck
      {
        Id = "providedArea",
        Status = CheckStatus.NotEvaluated,
        Reason = "noVerification"
      };
    }

    bool passed = verification.Status.StartsWith(VerificationStatuses.Passed, StringComparison.Ordinal);
    return new NormativeCheck
    {
      Id = "providedArea",
      Status = passed ? CheckStatus.Passed : CheckStatus.Failed
    };
  }
}
