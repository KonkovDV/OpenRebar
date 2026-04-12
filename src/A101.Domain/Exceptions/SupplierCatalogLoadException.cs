namespace A101.Domain.Exceptions;

public sealed class SupplierCatalogLoadException : A101DomainException
{
    public SupplierCatalogLoadException(string filePath, string reason, Exception? innerException = null)
        : base("SUPPLIER_CATALOG_INVALID", $"Invalid supplier catalog '{filePath}': {reason}", innerException)
    {
    }
}