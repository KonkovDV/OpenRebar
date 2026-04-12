namespace OpenRebar.Domain.Exceptions;

public sealed class LegendLoadException : OpenRebarDomainException
{
    public LegendLoadException(string filePath, string reason)
        : base("LEGEND_INVALID", $"Invalid legend file '{filePath}': {reason}")
    {
    }
}