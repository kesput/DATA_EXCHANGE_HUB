using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace DATA_EXCHANGE_HUB.Services;

public static class QueryResultCache
{
    public static void SetQueryResult<T>(
        this IMemoryCache memoryCache,
        ClaimsPrincipal user,
        ISession session,
        string scope,
        IEnumerable<string> keyParts,
        T result)
    {
        memoryCache.Set(
            BuildKey(user, session, scope, keyParts),
            result,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(20),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
            });
    }

    public static bool TryGetQueryResult<T>(
        this IMemoryCache memoryCache,
        ClaimsPrincipal user,
        ISession session,
        string scope,
        IEnumerable<string> keyParts,
        out T? result)
    {
        return memoryCache.TryGetValue(BuildKey(user, session, scope, keyParts), out result);
    }

    private static string BuildKey(
        ClaimsPrincipal user,
        ISession session,
        string scope,
        IEnumerable<string> keyParts)
    {
        var userName = user.Identity?.Name ?? session.Id;
        var rawKey = string.Join("|", new[] { userName, scope }.Concat(keyParts.Select(part => part.Trim())));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
        return $"query-result:{scope}:{hash}";
    }
}