using System.Text.Json;
using Mercato.Catalog.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Mercato.Catalog.Infrastructure.Caching;

// Redis behind the cache-aside port. Every operation is best-effort: if Redis is slow or
// down the catalog keeps answering from Postgres (a miss / a dropped write), it just loses
// the speed-up. A cache must never be able to take the read path down with it.
internal sealed class RedisCatalogCache(IConnectionMultiplexer redis, ILogger<RedisCatalogCache> logger) : ICatalogCache
{
    private const string ListVersionKey = "catalog:products:version";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(key);

            return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Cache read failed for {CacheKey}; falling back to the database.", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken) where T : class
    {
        try
        {
            // Always with a TTL: it is the safety net for any invalidation that is missed
            // (or lost in the race between a concurrent read-populate and a write).
            await redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(value, JsonOptions), timeToLive);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Cache write failed for {CacheKey}.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Cache invalidation failed for {CacheKey}; entry will expire by TTL.", key);
        }
    }

    public async Task<long> GetListVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(ListVersionKey);

            return value.TryParse(out long version) ? version : 0;
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Could not read the product list cache version.");
            return 0;
        }
    }

    public async Task BumpListVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // INCR is atomic: concurrent writers each get their own new version.
            await redis.GetDatabase().StringIncrementAsync(ListVersionKey);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Could not bump the product list cache version; list pages will expire by TTL.");
        }
    }

    private static bool IsCacheFailure(Exception exception) =>
        exception is RedisException or TimeoutException or JsonException;
}
