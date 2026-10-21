namespace Auth.Domain.ValueObjects;

/// <summary>
/// The sessions a second factor proved inside a signed-in session upgrades: the
/// caller's own session row (the token's <c>sid</c>) and, when the browser
/// presented it, its SSO session. The proven method is OR-ed into both, in the
/// transaction that settles the factor, so the next refresh carries it.
/// </summary>
/// <param name="SessionId">The session row the access token names.</param>
/// <param name="IdpHashes">
/// Every fingerprint the SSO cookie the request carried may be stored under, or
/// null when it carried none — the cookie travels only to the API's own origin,
/// so it may not arrive.
/// </param>
/// <param name="Method">The method the request just proved.</param>
public sealed record SessionUpgrade(Guid SessionId, CredentialHashCandidates? IdpHashes, AuthenticationMethods Method)
{
    // The SSO cookie hash names a credential; keep it out of log lines.
    public override string ToString() =>
        $"SessionUpgrade {{ SessionId = {SessionId}, Method = {Method} }}";
}
