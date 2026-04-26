namespace OpenRebar.Domain.Exceptions;

public sealed class InvalidIsolineFileException : OpenRebarDomainException
{
  public InvalidIsolineFileException(string filePath, string reason)
      : base("ISOLINE_INVALID", $"Invalid isoline file '{filePath}': {reason}")
  {
  }
}
