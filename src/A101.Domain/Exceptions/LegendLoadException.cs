namespace A101.Domain.Exceptions;

public sealed class LegendLoadException : A101DomainException
{
    public LegendLoadException(string filePath, string reason)
        : base("LEGEND_INVALID", $"Invalid legend file '{filePath}': {reason}")
    {
    }
}