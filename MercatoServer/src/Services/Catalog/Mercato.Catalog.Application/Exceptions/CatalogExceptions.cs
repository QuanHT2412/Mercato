namespace Mercato.Catalog.Application.Exceptions;

// 404: the thing the caller addressed does not exist (or is not visible to them).
public sealed class CatalogNotFoundException : Exception
{
    public CatalogNotFoundException(string resource, Guid id) : base($"{resource} '{id}' was not found.")
    {
    }
}

// 409: the request is valid on its own but clashes with existing data (duplicate SKU,
// duplicate category slug).
public sealed class CatalogConflictException : Exception
{
    public CatalogConflictException(string message) : base(message)
    {
    }
}
