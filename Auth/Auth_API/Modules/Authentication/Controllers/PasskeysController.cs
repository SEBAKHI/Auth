using System.Text.Json;
using Asp.Versioning;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;
using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using Auth.Application.Features.Passkeys.BeginPasskeyAssertion;
using Auth.Application.Features.Passkeys.BeginPasskeyRegistration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Features.Passkeys.CompletePasskeyRegistration;
using Auth.Application.Features.Passkeys.GetMyPasskeys;
using Auth.Application.Features.Passkeys.RemovePasskey;
using Auth.Application.Features.Passkeys.RenamePasskey;
using Auth.Application.Features.Passkeys.SendPasskeyRegistrationEmailCode;
using Auth.Application.Features.Passkeys.VerifyPasskeyAssertion;
using Auth_API.Common;
using Auth_API.Common.FirstParty;
using Auth_API.Common.Passkeys;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Auth_API.Modules.Authentication.Controllers;

/// <summary>
/// Passkeys (S20): the signed-in account adds one, lists them, renames and removes one; and
/// anyone signs in with one, or cancels their account's pending deletion with one (S20 PR B).
/// </summary>
/// <remarks>
/// <para>
/// Every action answers 404 while passkeys do not run — switched off, or switched on without
/// an RP ID, a first-party origin or the schema (PK19, <see cref="PasskeysAvailabilityMiddleware"/>).
/// The three sign-in actions are anonymous and stay here, behind the same gate: placed anywhere
/// else they would answer while passkeys are off.
/// </para>
/// <para>
/// The route is a template (<c>api/v{version}/auth/passkeys</c>), matched as every route is:
/// the 404 gate reads the endpoint's metadata, never the path, so a path's case, a trailing
/// slash or an encoding cannot slip past it. The ceremony cookie is <c>__Host-</c>, Strict and
/// <c>Path=/</c> (<see cref="WebAuthnChallengeCookie"/>), so the path's case does not matter to
/// it either.
/// </para>
/// <para>
/// No answer here may be stored by a browser or a proxy.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/passkeys")]
[Produces("application/json")]
[Authorize]
[PasskeysEndpoint]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PasskeysController : ApiController
{
    /// <summary>The largest registration answer accepted: an RSA key's attestation fits many times over.</summary>
    public const int MaxRegistrationBodyBytes = 64 * 1024;

    /// <summary>
    /// The largest sign-in answer accepted: a 1023-byte credential id, a 64-byte handle and an
    /// RSA signature, base64url, fit many times over.
    /// </summary>
    public const int MaxAssertionBodyBytes = 16 * 1024;

    private readonly ISender _sender;
    private readonly IdentityProviderSettings _idpSettings;

    public PasskeysController(ISender sender, IOptionsSnapshot<IdentityProviderSettings> idpSettings)
    {
        _sender = sender;
        _idpSettings = idpSettings.Value;
    }

    /// <summary>
    /// Starts adding a passkey: the options for <c>navigator.credentials.create()</c>. The
    /// challenge they carry is held for five minutes; its handle travels in the
    /// <c>__Host-auth_webauthn</c> cookie, never in the body.
    /// </summary>
    /// <remarks>
    /// Needs a recent sign-in that proved a first factor, and two factors when the account
    /// already has a qualifying one (<c>Auth.ReauthenticationRequired</c>). <c>emailCodeRequired</c>
    /// says whether completing it needs the code emailed to the account (its first factor).
    /// </remarks>
    [HttpPost("registration/options")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(PasskeyRegistrationOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BeginRegistration(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new BeginPasskeyRegistrationCommand(userId, GetCurrentSessionId()), cancellationToken);
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Apply(Response, result.Value.ChallengeHandle);
        using var options = JsonDocument.Parse(result.Value.OptionsJson);
        return Ok(new PasskeyRegistrationOptionsResponse(
            options.RootElement.Clone(), result.Value.ExpiresAt, result.Value.EmailCodeRequired));
    }

    /// <summary>
    /// Emails the code an account enters before it binds a passkey as its first qualifying
    /// second factor, to its confirmed address. A fresh code supersedes any earlier one.
    /// </summary>
    /// <remarks>
    /// Answers <c>emailCodeRequired: false</c>, and sends nothing, while no code is needed:
    /// email off, or a qualifying factor already. <c>TwoFactor.SetupRequired</c> when no
    /// registration was started. The code is never returned.
    /// </remarks>
    [HttpPost("registration/email-code")]
    [EnableRateLimiting("two-factor-email-code")]
    [ProducesResponseType(typeof(TwoFactorEmailCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendRegistrationEmailCode(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new SendPasskeyRegistrationEmailCodeCommand(userId, GetCurrentSessionId(), GetClientIpAddress()),
            cancellationToken);

        return result.Match<IActionResult>(response => Ok(response), errors => Problem(errors));
    }

    /// <summary>
    /// Completes adding a passkey: the browser's <c>PublicKeyCredential</c>, checked against
    /// the challenge the cookie names. The challenge is spent whatever the outcome, once the
    /// emailed code (when one is needed) was accepted.
    /// </summary>
    /// <remarks>
    /// Every rejection of the ceremony is <c>Passkey.VerificationFailed</c>; the eleventh live
    /// passkey <c>Passkey.LimitReached</c>; a name over 64 characters <c>Passkey.NameTooLong</c>.
    /// </remarks>
    [HttpPost("registration/verify")]
    [EnableRateLimiting("login")]
    [RequestSizeLimit(MaxRegistrationBodyBytes)]
    [ProducesResponseType(typeof(PasskeyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CompleteRegistration(
        [FromBody] CompletePasskeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var (handle, repeated) = WebAuthnChallengeCookie.Read(Request);

        var result = await _sender.Send(
            new CompletePasskeyRegistrationCommand(
                userId,
                GetCurrentSessionId(),
                handle,
                repeated,
                CredentialJson(request.Credential),
                request.Name,
                request.EmailCode),
            cancellationToken);

        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Delete(Response);
        return Ok(result.Value);
    }

    /// <summary>The account's live passkeys, oldest first.</summary>
    [HttpGet]
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(typeof(IReadOnlyList<PasskeyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new GetMyPasskeysQuery(userId), cancellationToken);
        return result.Match<IActionResult>(passkeys => Ok(passkeys), errors => Problem(errors));
    }

    /// <summary>Gives one of the account's passkeys another name.</summary>
    [HttpPatch("{id:guid}")]
    [EnableRateLimiting("sign-in-page")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenamePasskeyRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(new RenamePasskeyCommand(userId, id, request.Name), cancellationToken);
        return result.Match<IActionResult>(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>
    /// Removes one of the account's passkeys, and signs out every other session of the
    /// account; this one stays.
    /// </summary>
    /// <remarks>
    /// Needs a recent sign-in that proved a first factor, and two factors when the account has
    /// the authenticator app. The last qualifying factor of an administrator the two-factor
    /// policy binds stays (<c>TwoFactor.RequiredByPolicy</c>).
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = await _sender.Send(
            new RemovePasskeyCommand(userId, id, GetCurrentSessionId(), IdpSessionCookie.Read(Request, _idpSettings)),
            cancellationToken);
        return result.Match<IActionResult>(_ => NoContent(), errors => Problem(errors));
    }

    /// <summary>
    /// Starts a sign-in with a passkey: the options for <c>navigator.credentials.get()</c>. The
    /// challenge they carry is held for five minutes; its handle travels in the
    /// <c>__Host-auth_webauthn</c> cookie, never in the body. A page that asks again while its
    /// challenge has more than a minute left gets the same options, and the cookie stays.
    /// </summary>
    /// <remarks>
    /// Anonymous: the options name no account. Only the platform's own sign-in pages may ask
    /// (<c>Auth.FirstPartyOriginRequired</c> for any other browser origin), so a page of another
    /// host of the same site cannot sign its visitor in to an account of its choice.
    /// </remarks>
    [HttpPost("assertion/options")]
    [AllowAnonymous]
    [EnableRateLimiting("sign-in-page")]
    [RequireFirstPartyOrigin]
    [ProducesResponseType(typeof(PasskeyAssertionOptionsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> BeginAssertion(CancellationToken cancellationToken)
    {
        var (handle, repeated) = WebAuthnChallengeCookie.Read(Request);

        var result = await _sender.Send(new BeginPasskeyAssertionCommand(handle, repeated), cancellationToken);
        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        // A new challenge, a new cookie; the live one offered again keeps the cookie it has.
        if (result.Value.ChallengeHandle is { } newHandle)
        {
            WebAuthnChallengeCookie.Apply(Response, newHandle);
        }

        using var options = JsonDocument.Parse(result.Value.OptionsJson);
        return Ok(new PasskeyAssertionOptionsResponse(options.RootElement.Clone(), result.Value.ExpiresAt));
    }

    /// <summary>
    /// Signs in with a passkey: the browser's <c>PublicKeyCredential</c>, checked against the
    /// challenge the cookie names. The challenge is spent whatever the outcome. A passkey the
    /// device verified the person for proves two factors, so no second step follows.
    /// </summary>
    /// <remarks>
    /// Every rejection of the ceremony is <c>Passkey.VerificationFailed</c>. An account waiting
    /// for deletion answers <c>User.AccountPendingDeletion</c> with the end of its grace period;
    /// an inactive or pending account, and an administrator's lock, answer as the password
    /// sign-in does. A lock raised by wrong passwords admits the passkey and lifts nothing.
    /// </remarks>
    [HttpPost("assertion/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [RequestSizeLimit(MaxAssertionBodyBytes)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyAssertion(
        [FromBody] PasskeyAssertionRequest request,
        CancellationToken cancellationToken)
    {
        var (handle, repeated) = WebAuthnChallengeCookie.Read(Request);

        var result = await _sender.Send(
            new VerifyPasskeyAssertionCommand(
                handle,
                repeated,
                CredentialJson(request.Credential),
                GetClientIpAddress(),
                GetUserAgent(),
                GetDeviceId(request.DeviceId),
                request.StaySignedIn),
            cancellationToken);

        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Delete(Response);
        return Ok(result.Value);
    }

    /// <summary>
    /// Cancels the pending deletion of the passkey's account and signs it in (D-S20-6 (a)): the
    /// passkey entry of the grace-period recovery, chosen explicitly on the recovery page. The
    /// same checks as <see cref="VerifyAssertion"/>, then the recovery: the deletion request is
    /// cancelled and the account restored.
    /// </summary>
    /// <remarks>
    /// An account that is not waiting for deletion answers <c>User.InvalidCredentials</c>, as
    /// the password entry does; an administrator's lock answers as it does there.
    /// </remarks>
    [HttpPost("assertion/recover")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [RequireFirstPartyOrigin]
    [IssuesFirstPartySession]
    [RequestSizeLimit(MaxAssertionBodyBytes)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RecoverWithAssertion(
        [FromBody] PasskeyRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var (handle, repeated) = WebAuthnChallengeCookie.Read(Request);

        var result = await _sender.Send(
            new RecoverAccountWithPasskeyCommand(
                handle,
                repeated,
                CredentialJson(request.Credential),
                GetClientIpAddress(),
                GetUserAgent(),
                GetDeviceId(request.DeviceId)),
            cancellationToken);

        if (result.IsError)
        {
            return Problem(result.Errors);
        }

        WebAuthnChallengeCookie.Delete(Response);
        return Ok(result.Value);
    }

    /// <summary>
    /// The credential as JSON text. A body without a credential object is checked like any
    /// malformed answer: it fails verification, rather than the request failing on reading it.
    /// </summary>
    private static string CredentialJson(JsonElement credential) =>
        credential.ValueKind == JsonValueKind.Object ? credential.GetRawText() : string.Empty;
}

/// <summary>The options of a passkey sign-in.</summary>
/// <param name="Options">The options for <c>navigator.credentials.get()</c>, as JSON (WebAuthn L3 PublicKeyCredentialRequestOptionsJSON).</param>
/// <param name="ExpiresAt">When the challenge stops being accepted (UTC): renew the options a minute before.</param>
public sealed record PasskeyAssertionOptionsResponse(JsonElement Options, DateTime ExpiresAt);

/// <summary>The answer to a passkey sign-in.</summary>
/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as JSON (AuthenticationResponseJSON).</param>
/// <param name="DeviceId">The browser's own identifier, as the password sign-in sends it (the header wins).</param>
/// <param name="StaySignedIn">Whether the person ticked "Stay signed in".</param>
public sealed record PasskeyAssertionRequest(JsonElement Credential, string? DeviceId, bool StaySignedIn);

/// <summary>The answer to a passkey sign-in that recovers an account waiting for deletion.</summary>
/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as JSON (AuthenticationResponseJSON).</param>
/// <param name="DeviceId">The browser's own identifier (the header wins).</param>
public sealed record PasskeyRecoveryRequest(JsonElement Credential, string? DeviceId);

/// <summary>The options of a passkey registration.</summary>
/// <param name="Options">The options for <c>navigator.credentials.create()</c>, as JSON (WebAuthn L3 PublicKeyCredentialCreationOptionsJSON).</param>
/// <param name="ExpiresAt">When the challenge stops being accepted (UTC).</param>
/// <param name="EmailCodeRequired">Whether completing it needs the code emailed to the account.</param>
public sealed record PasskeyRegistrationOptionsResponse(JsonElement Options, DateTime ExpiresAt, bool EmailCodeRequired);

/// <summary>The answer to a passkey registration.</summary>
/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as JSON (RegistrationResponseJSON).</param>
/// <param name="Name">The name the owner gives the passkey; blank takes a default.</param>
/// <param name="EmailCode">The code emailed for a first qualifying factor, when one is needed.</param>
public sealed record CompletePasskeyRegistrationRequest(JsonElement Credential, string? Name, string? EmailCode)
{
    // The code is a secret while it lives.
    public override string ToString() => $"CompletePasskeyRegistrationRequest {{ Name = {Name} }}";
}

/// <summary>A passkey's new name.</summary>
/// <param name="Name">The name; blank takes a default.</param>
public sealed record RenamePasskeyRequest(string? Name);
