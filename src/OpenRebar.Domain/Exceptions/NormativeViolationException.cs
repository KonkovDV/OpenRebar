namespace OpenRebar.Domain.Exceptions;

public sealed class NormativeViolationException : OpenRebarDomainException
{
  public string Clause { get; }

  public NormativeViolationException(string clause, string message)
      : base("SP63_VIOLATION", $"SP 63 §{clause}: {message}")
  {
    Clause = clause;
  }
}
