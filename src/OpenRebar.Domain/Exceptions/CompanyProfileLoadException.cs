namespace OpenRebar.Domain.Exceptions;

public sealed class CompanyProfileLoadException : OpenRebarDomainException
{
  public CompanyProfileLoadException(string message)
      : base("PROFILE_INVALID", message)
  {
  }
}
