namespace Mercato.Catalog.Application.Abstractions;

// Cache-aside port. Implementations must be best-effort: a cache outage degrades to
// "every read hits the database", never to a failed request.
public interface ICatalogCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken) where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken);

    // List/search results cannot be invalidated key by key (the set of cached queries is
    // unbounded), so every list key embeds this version and one increment orphans them
    // all; the orphans then age out by TTL.
    Task<long> GetListVersionAsync(CancellationToken cancellationToken);

    Task BumpListVersionAsync(CancellationToken cancellationToken);
}

public static class CatalogCacheKeys
{
    public static readonly TimeSpan ProductTimeToLive = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ListTimeToLive = TimeSpan.FromMinutes(1);

    public static string Product(Guid id) => $"catalog:product:{id:N}";

    public static string ProductList(long version, string? search, Guid? categoryId, int page, int pageSize) =>
        $"catalog:products:v{version}:{categoryId?.ToString("N") ?? "-"}:{page}:{pageSize}:{search?.Trim().ToLowerInvariant()}";
}
