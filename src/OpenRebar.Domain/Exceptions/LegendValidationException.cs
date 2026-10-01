namespace OpenRebar.Domain.Exceptions;

/// <summary>
/// An As legend failed a structural check. <see cref="OpenRebarDomainException.ErrorCode"/> is a LEGEND_* code.
/// </summary>
public sealed class LegendValidationException : OpenRebarDomainException
{
  public LegendValidationException(string code, string message)
      : base(code, message)
  {
  }
}
