namespace OpenRebar.Domain.Exceptions;

public sealed class AsFieldReadException : OpenRebarDomainException
{
  public AsFieldReadException(string code, string message)
      : base(code, message)
  {
  }
}
