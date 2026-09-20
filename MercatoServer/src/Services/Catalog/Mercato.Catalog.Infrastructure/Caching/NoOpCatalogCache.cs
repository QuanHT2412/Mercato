using Mercato.Catalog.Application.Abstractions;

namespace Mercato.Catalog.Infrastructure.Caching;

// Placeholder until the Redis implementation lands: every read is a miss, every write is
// dropped. Lets the handlers be written against the cache port from day one.
internal sealed class NoOpCatalogCache : ICatalogCache
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class =>
        Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken) where T : class =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<long> GetListVersionAsync(CancellationToken cancellationToken) => Task.FromResult(0L);

    public Task BumpListVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
