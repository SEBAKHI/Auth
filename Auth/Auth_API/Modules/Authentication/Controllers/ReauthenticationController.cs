using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Reauthenticate;
using Auth.Application.Features.Passkeys.ReauthenticateWithPasskey;
using Auth.Application.Interfaces;
using Auth_API.Common;
using Auth_API.Common.Passkeys;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// A signed-in person proving themself again where they stand, with no new sign-in (owner decision,
/// 2026-11-09): what a change to a security setting asks for once the sign-in is more than
/// <c>TwoFactor:ReauthenticationMaxAgeMinutes</c> old.
/// </summary>
/// <remarks>
/// The proof is the one a sign-in of the same account asks for. Tokens issued to an application
/// never reach here: this is the platform scheme's. No answer here may be stored.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/reauthentication")]
[Produces("application/json")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReauthenticationController : ApiController
{
    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;

    public ReauthenticationController(ISender sender, IOptionsSnapshot<IdentityProviderSettings> idpSettings)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
    }

    /// <summary>What this account must prove: what the window asks for.</summary>
    /// <remarks>
    /// 403 <c>Auth.ReauthenticationRequired</c> for a token with no live session row, which can only
    /// sign in again.
    /// </remarks>
    [HttpGet]
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(typeof(ReauthenticationOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetOptions(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new GetReauthenticationOptionsQuery(userId, GetCurrentSessionId()), cancellationToken);
        return result.Match<IActionResult>(options => Ok(options), errors => Problem(errors));
    }

    /// <summary>
    /// Emails the code this proof takes: the first proof of an account without a password, or the
    /// second step of an account whose second factor is the emailed code.
    /// </summary>
    [HttpPost("email-code")]
    [EnableRateLimiting("two-factor-email-code")]
    [ProducesResponseType(typeof(EmailCodeSent), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendEmailCode(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SendReauthenticationEmailCodeCommand(userId, GetCurrentSessionId(), GetClientIpAddress()),
            cancellationToken);
        return result.Match<IActionResult>(sent => Ok(sent), errors => Problem(errors));
    }

    /// <summary>
    /// Proves the person with the password, or the emailed code of an account without one, and the
    /// second step a sign-in would ask for.
    /// </summary>
    /// <remarks>
    /// A wrong password or code is counted as at a sign-in. 403 <c>Auth.ReauthenticationRequired</c>
    /// for a token with no live session row; 403 <c>Auth.PasskeyRequired</c> for an administrator
    /// whose only usable factor is a passkey.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Prove([FromBody] ReauthenticateRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new ReauthenticateCommand(
                userId,
                GetCurrentSessionId(),
                IdpSessionCookie.Read(Request, _idpSettings),
                request.Password,
                request.EmailCode,
                request.SecondStepCode,
                request.UseRecoveryCode,
                GetClientIpAddress(),
                GetUserAgent(),
                GetDeviceId()),
            cancellationToken);

        return result.Match<IActionResult>(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>
    /// Proves the person with a passkey: the browser's <c>PublicKeyCredential</c>, answering the
    /// options of <c>auth/passkeys/assertion/options</c> and checked against the challenge the
    /// cookie names. The challenge is spent whatever the outcome.
    /// </summary>
    /// <remarks>
    /// 404 while passkeys do not run. Every rejection of the ceremony is
    /// <c>Passkey.VerificationFailed</c>; a passkey of another account <c>Passkey.NotThisAccount</c>.
    /// </remarks>
    [HttpPost("passkey")]
    [PasskeysEndpoint]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(PasskeysController.MaxAssertionBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ProveWithPasskey(
        [FromBody] ReauthenticateWithPasskeyRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var (handle, repeated) = WebAuthnChallengeCookie.Read(Request);

        var result = await _sender.Send(
            new ReauthenticateWithPasskeyCommand(
                userId,
                GetCurrentSessionId(),
                IdpSessionCookie.Read(Request, _idpSettings),
                handle,
                repeated,
                // A body without a credential object fails verification, as any malformed answer does.
                request.Credential.ValueKind == JsonValueKind.Object ? request.Credential.GetRawText() : string.Empty),
            cancellationToken);

        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Delete(Response);
        return NoContent();
    }
}

/// <summary>The proof a signed-in person gives with no new sign-in.</summary>
/// <param name="Password">The password, when it is the first proof.</param>
/// <param name="EmailCode">The emailed code, when it is the first proof.</param>
/// <param name="SecondStepCode">The second step's code, when the account has one.</param>
/// <param name="UseRecoveryCode">Whether <paramref name="SecondStepCode"/> is a recovery code.</param>
public sealed record ReauthenticateRequest(
    string? Password,
    string? EmailCode,
    string? SecondStepCode,
    bool UseRecoveryCode)
{
    // Every member but the last is a secret.
    public override string ToString() => $"ReauthenticateRequest {{ UseRecoveryCode = {UseRecoveryCode} }}";
}

/// <summary>A signed-in person's answer to a passkey challenge.</summary>
/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as JSON (AuthenticationResponseJSON).</param>
public sealed record ReauthenticateWithPasskeyRequest(JsonElement Credential)
{
    // The credential names the account and carries the signed client data (PK16).
    public override string ToString() => "ReauthenticateWithPasskeyRequest { }";
}
