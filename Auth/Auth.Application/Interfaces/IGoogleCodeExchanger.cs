using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// Google's token endpoint, for the one call the Google sign-in makes to it: turning the
/// one-time authorization code Google sent back to an app's callback page into the ID token
/// (OAuth 2.0 authorization code grant with PKCE, RFC 6749 §4.1.3 and RFC 7636 §4.5).
/// </summary>
/// <remarks>
/// Only the ID token comes back. Google's answer also carries an access token, and sometimes
/// a refresh token; neither leaves the implementation, because nothing here calls a Google
/// API on the person's behalf.
/// </remarks>
public interface IGoogleCodeExchanger
{
    /// <summary>
    /// Exchanges <paramref name="code"/> for the ID token Google minted with it.
    /// </summary>
    /// <param name="code">The single-use code Google appended to the callback address.</param>
    /// <param name="codeVerifier">The PKCE verifier whose S256 challenge the sign-in sent to Google.</param>
    /// <param name="redirectUri">
    /// The callback address the sign-in sent to Google, which Google compares character for
    /// character. Built by the server from the verified Origin, never taken from the request.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The ID token, still unvalidated (the sign-in validates it, once); or
    /// <c>ExternalAuth.ProviderNotConfigured</c> when Google sign-in is off or its client id or
    /// secret is missing or refused; or <c>ExternalAuth.CodeExchangeFailed</c> when Google
    /// refuses the code.
    /// </returns>
    /// <exception cref="HttpRequestException">
    /// Google could not be reached or answered 5xx. Left to the global exception handler, which
    /// answers 503 <c>Http.Unavailable</c> with Retry-After (ADR 0001 §5).
    /// </exception>
    Task<ErrorOr<string>> ExchangeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken);
}
