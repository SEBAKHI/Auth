using Auth.Application.Interfaces;

namespace Auth_API.Common.FirstParty;

/// <summary>
/// Reads <see cref="IBrowserSignInContext.IsBrowserSignIn"/> from the current
/// request's <c>Origin</c> header: any non-empty value, the opaque <c>"null"</c>
/// some pages send included, is a browser. Outside a request there is no
/// browser.
/// </summary>
public sealed class RequestOriginBrowserSignInContext(IHttpContextAccessor httpContextAccessor) : IBrowserSignInContext
{
    /// <inheritdoc />
    public bool IsBrowserSignIn =>
        !string.IsNullOrWhiteSpace(httpContextAccessor.HttpContext?.Request.Headers.Origin.ToString());
}
