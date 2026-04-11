namespace A101.Domain.Exceptions;

public sealed class OptimizationException : A101DomainException
{
    public OptimizationException(string message)
        : base("OPTIMIZATION_FAILED", message)
    {
    }
}