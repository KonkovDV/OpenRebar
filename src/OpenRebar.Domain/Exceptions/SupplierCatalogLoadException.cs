namespace OpenRebar.Domain.Exceptions;

public sealed class SupplierCatalogLoadException : OpenRebarDomainException
{
  public SupplierCatalogLoadException(string filePath, string reason, Exception? innerException = null)
      : base("SUPPLIER_CATALOG_INVALID", $"Invalid supplier catalog '{filePath}': {reason}", innerException)
  {
  }
}
