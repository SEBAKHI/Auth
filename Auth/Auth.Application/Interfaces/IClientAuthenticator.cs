using Auth.Domain.Enums;
using ErrorOr;

namespace Auth.Application.Interfaces;

/// <summary>
/// The client authentication a token request carries (<c>private_key_jwt</c>, RFC 7523 §2.2).
/// </summary>
/// <param name="AssertionType">The form's <c>client_assertion_type</c>.</param>
/// <param name="Assertion">The form's <c>client_assertion</c>.</param>
/// <param name="ClientId">The form's <c>client_id</c>, when sent; it must then name the key's application.</param>
/// <param name="IpAddress">The caller's address, for the log.</param>
public sealed record ClientAssertionRequest(
    string? AssertionType,
    string? Assertion,
    string? ClientId,
    string? IpAddress)
{
    /// <summary>
    /// True when the request carries any part of a client assertion. Such a request is
    /// authenticated, and refused as <c>Auth.InvalidClient</c> when it does not authenticate:
    /// half an assertion is never read as no assertion (OI-171, CA1).
    /// </summary>
    public bool IsPresent => !string.IsNullOrEmpty(AssertionType) || !string.IsNullOrEmpty(Assertion);
}

/// <summary>
/// An application that proved who it is with one of its keys.
/// </summary>
/// <param name="KeyId">The key that signed the assertion.</param>
/// <param name="Kid">That key's identifier.</param>
/// <param name="ApplicationId">The key's application.</param>
/// <param name="ApplicationCode">The key's application code: the client's <c>client_id</c>.</param>
/// <param name="Jti">The assertion's identifier.</param>
/// <param name="ExpiresAtUtc">The assertion's <c>exp</c>.</param>
public sealed record AuthenticatedClient(
    Guid KeyId,
    string Kid,
    Guid ApplicationId,
    string ApplicationCode,
    string Jti,
    DateTime ExpiresAtUtc);

/// <summary>
/// The one client authenticator of the token endpoint (OI-171, B3): <c>private_key_jwt</c> with an
/// application key of the purpose the grant requires. <c>client_credentials</c> requires a
/// machine key; <c>authorization_code</c> and <c>refresh_token</c> require a sign-in key.
/// </summary>
/// <remarks>
/// Every refusal is the one published answer <c>Auth.InvalidClient</c>; which check failed goes
/// to the log only, so the answer is no oracle for a caller probing the rules one by one.
/// </remarks>
public interface IClientAuthenticator
{
    /// <summary>
    /// Checks, in order: the assertion type; the key id; an active key of a live application;
    /// the key's purpose equals <paramref name="requiredPurpose"/>; the <c>client_id</c>, when
    /// sent, names the key's application; the signature and the assertion's rules (audience = the
    /// issuer). Spends nothing: the jti is recorded by <see cref="SpendAsync"/>, after whatever
    /// else the grant must check first.
    /// </summary>
    Task<ErrorOr<AuthenticatedClient>> VerifyAsync(
        ClientAssertionRequest request,
        MachineKeyPurpose requiredPurpose,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the verified assertion's jti, once per application. A second use of the same jti,
    /// by any grant, is <c>Auth.InvalidClient</c>.
    /// </summary>
    Task<ErrorOr<Success>> SpendAsync(
        AuthenticatedClient client,
        string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="VerifyAsync"/>, then <see cref="SpendAsync"/>, then records the key's use: the
    /// whole authentication of a grant with nothing to check in between.
    /// </summary>
    Task<ErrorOr<AuthenticatedClient>> AuthenticateAsync(
        ClientAssertionRequest request,
        MachineKeyPurpose requiredPurpose,
        CancellationToken cancellationToken);
}
