namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// A freshly issued provider sign-in nonce.
/// </summary>
/// <param name="Nonce">
/// The plain value to hand to the provider's SDK, which seals it into the signed
/// ID token. Its counterpart hash is in an HttpOnly cookie set alongside this
/// response, and the two are checked against each other at sign-in.
/// </param>
public record ExternalNonceResponse(string Nonce);
