namespace OpenRebar.Domain.Exceptions;

/// <summary>
/// Base exception for all domain-specific OpenRebar failures that need stable error codes.
/// </summary>
public abstract class OpenRebarDomainException : Exception
{
  public string ErrorCode { get; }

  protected OpenRebarDomainException(string errorCode, string message)
      : this(errorCode, message, null)
  {
  }

  protected OpenRebarDomainException(string errorCode, string message, Exception? innerException)
      : base(message, innerException)
  {
    ErrorCode = errorCode;
  }
}
