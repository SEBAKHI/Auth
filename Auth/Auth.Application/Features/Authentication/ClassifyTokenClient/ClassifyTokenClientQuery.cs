using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.ClassifyTokenClient;

/// <summary>
/// Which application, if any, a token request authenticates as (OI-178): asked before the
/// token endpoint's rate limiter, so that a verified application's requests count on its own
/// budget and every other request on its address's.
/// </summary>
/// <remarks>
/// Spends nothing: the assertion is verified, and its <c>jti</c> only read, so the grant that
/// runs after the limiter still spends it once, and a request the limiter refuses leaves the
/// assertion usable for the retry.
/// </remarks>
/// <param name="GrantType">The form's <c>grant_type</c>; it decides the key purpose required.</param>
/// <param name="AssertionType">The form's <c>client_assertion_type</c>.</param>
/// <param name="Assertion">The form's <c>client_assertion</c>.</param>
/// <param name="ClientId">The form's <c>client_id</c>, when sent.</param>
/// <param name="IpAddress">The caller's address, for the log.</param>
public sealed record ClassifyTokenClientQuery(
    string? GrantType,
    string? AssertionType,
    string? Assertion,
    string? ClientId,
    string? IpAddress) : IRequest<ErrorOr<VerifiedTokenClient>>;

/// <summary>The application a token request verified as, and the assertion it verified with.</summary>
/// <param name="ApplicationId">The key's application.</param>
/// <param name="Jti">The assertion's identifier: one assertion counts on the application's budget once.</param>
/// <param name="ExpiresAtUtc">The assertion's <c>exp</c>, after which it verifies no more.</param>
public sealed record VerifiedTokenClient(Guid ApplicationId, string Jti, DateTime ExpiresAtUtc);
