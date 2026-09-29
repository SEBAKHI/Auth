namespace Auth_API.Common.FirstParty;

/// <summary>
/// One of the platform's own browser apps, identified by the Origin the server
/// verified against <c>IdentityProvider:FirstPartySpaOrigins</c>.
/// </summary>
/// <param name="Origin">The normalized origin (lower case, no trailing slash).</param>
/// <param name="RefreshCookieName">
/// The name of this app's refresh cookie: <c>__Host-auth_rt_</c> and the first 16
/// hex digits of the SHA-256 of <paramref name="Origin"/>. One cookie per app, so
/// the console and the accounts app never spend each other's token; the name does
/// not depend on the order of the configured list.
/// </param>
public sealed record FirstPartyApp(string Origin, string RefreshCookieName);
