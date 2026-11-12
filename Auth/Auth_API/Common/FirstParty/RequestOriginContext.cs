using Auth.Application.Interfaces;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Reads <see cref="IRequestOriginContext.Origin"/> from the current request's <c>Origin</c>
/// header; null outside a request or without one.
/// </summary>
public sealed class RequestOriginContext(IHttpContextAccessor httpContextAccessor) : IRequestOriginContext
{
    /// <inheritdoc />
    public string? Origin
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.Request.Headers.Origin.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
