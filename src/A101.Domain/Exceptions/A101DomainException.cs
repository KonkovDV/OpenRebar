namespace A101.Domain.Exceptions;

/// <summary>
/// Base exception for all domain-specific A101 failures that need stable error codes.
/// </summary>
public abstract class A101DomainException : Exception
{
    public string ErrorCode { get; }

    protected A101DomainException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}