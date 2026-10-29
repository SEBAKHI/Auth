using Auth.Application.Features.LoginDesigns.Common;
using ErrorOr;
using Microsoft.Extensions.Caching.Memory;

namespace Auth.Infrastructure.LoginDesigns;

/// <summary>
/// In-process cache of the designs sign-in pages are served, with a size bound of its own.
/// </summary>
/// <remarks>
/// <para>
/// The shared <see cref="IMemoryCache"/> has no size limit, and giving it one would oblige every
/// other entry to state a size; this cache owns a <see cref="MemoryCache"/> instead. An entry
/// costs its HTML's length in characters (a refusal costs one), and the whole cache at most
/// <see cref="SizeLimit"/>: a render that does not fit is served, not kept, and the cache
/// compacts. The key carries the design's fingerprint, so an entry is never stale, only unused:
/// the sliding expiry lets the renders of a superseded design go.
/// </para>
/// <para>
/// Two visitors asking at once before the first render ends both render; each render is bounded
/// by <c>LoginDesign.MaxRenderedBytes</c>, and the requests after it are served from here.
/// </para>
/// </remarks>
public sealed class LoginDesignRenderCache : ILoginDesignRenderCache, IDisposable
{
    /// <summary>Total kept, in UTF-16 characters (16 MB): 32 renders at the 256 KB limit.</summary>
    public const long SizeLimit = 8 * 1024 * 1024;

    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(30);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });

    /// <inheritdoc />
    public ErrorOr<string> GetOrRender(LoginDesignRenderKey key, Func<ErrorOr<string>> render)
    {
        if (_cache.TryGetValue(key, out ErrorOr<string> kept))
        {
            return kept;
        }

        var rendered = render();
        _cache.Set(key, rendered, new MemoryCacheEntryOptions
        {
            Size = rendered.IsError ? 1 : Math.Max(1, rendered.Value.Length),
            SlidingExpiration = SlidingExpiration
        });
        return rendered;
    }

    /// <inheritdoc />
    public void Dispose() => _cache.Dispose();
}
