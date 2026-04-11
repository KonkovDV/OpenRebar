namespace A101.Domain.Exceptions;

public sealed class InvalidIsolineFileException : A101DomainException
{
    public InvalidIsolineFileException(string filePath, string reason)
        : base("ISOLINE_INVALID", $"Invalid isoline file '{filePath}': {reason}")
    {
    }
}