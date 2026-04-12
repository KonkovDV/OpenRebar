namespace OpenRebar.Domain.Exceptions;

public sealed class OptimizationException : OpenRebarDomainException
{
    public OptimizationException(string message)
        : base("OPTIMIZATION_FAILED", message)
    {
    }
}