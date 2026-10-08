using System.Globalization;
using System.Security.Claims;
using Auth.Domain.Constants;
using Auth_API.Authorization;
using Auth_API.Common.Errors;
using Auth_Localization.Resources;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace Auth_API.Common;

/// <summary>
/// Base controller for every API controller: handler errors reach HTTP through
/// <see cref="Problem(IEnumerable{Error})"/>, the one mapper of ADR 0001.
/// All API controllers should inherit from this instead of ControllerBase.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    /// <summary>
    /// The problem for a handler's errors (<see cref="ProblemMapping"/>). Failures point into
    /// the type this action binds from the body, when it binds one.
    /// </summary>
    protected IActionResult Problem(IEnumerable<Error> errors) =>
        ProblemMapping.ToProblem(HttpContext, ProblemDetailsFactory, errors.ToList(), BodyType());

    private Type? BodyType() =>
        ControllerContext.ActionDescriptor?.Parameters
            .FirstOrDefault(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            ?.ParameterType;

    protected Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }

    /// <summary>
    /// The session the caller's access token belongs to. The stable session id lives
    /// in the <c>sid</c> claim (constant across token refreshes, and equal to the
    /// session row's id); a token issued before <c>sid</c> carries only its legacy
    /// <c>jti</c>, which names no session row — so a check that needs the session,
    /// such as the recent sign-in before a two-factor change, refuses it.
    /// </summary>
    protected Guid? GetCurrentSessionId()
    {
        var sessionIdClaim = User.FindFirstValue(JwtClaimNames.Sid)
                             ?? User.FindFirstValue(JwtClaimNames.JwtId);

        return Guid.TryParse(sessionIdClaim, out var sessionId) ? sessionId : null;
    }

    /// <summary>
    /// True when the caller's JWT permission claims satisfy
    /// <paramref name="permission"/>, using the same wildcard semantics as
    /// <c>[RequirePermission]</c>. For widening handler scoping (e.g. platform
    /// administration over all organizations) — endpoint gating still belongs
    /// to the attribute.
    /// </summary>
    protected bool HasPermissionClaim(string permission)
    {
        var held = User.FindAll(JwtClaimNames.Permissions).Select(c => c.Value);
        return PermissionRequirementHandler.PermissionMatches(held, permission);
    }

    /// <summary>
    /// True when the caller's access token carries any platform permission at all.
    /// False for an account without platform authority, and for a platform
    /// administrator whose permissions are withheld until the session proves a
    /// second factor — so a handler that reads platform grants live can refuse to
    /// count what the token was deliberately minted without.
    /// </summary>
    protected bool HasAnyPermissionClaim() => User.HasClaim(c => c.Type == JwtClaimNames.Permissions);

    protected string? GetClientIpAddress()
    {
        return ClientIpResolver.Resolve(HttpContext);
    }

    protected string? GetUserAgent()
    {
        return Request.Headers.UserAgent.FirstOrDefault();
    }

    /// <summary>
    /// The calling browser's own identifier, used only to tell one browser from
    /// another when deciding whether a sign-in deserves an email. Client-supplied
    /// and therefore forgeable: never an authorization input.
    ///
    /// Read from a header alongside the IP and the user agent, because it is the
    /// same kind of fact. It used to arrive in the body of each request that
    /// creates a session, which left every such endpoint free to forget it —
    /// verify-email did, so the sign-in that completes registration was filed
    /// under a signature built from an empty id and the user's next login looked
    /// like a different browser.
    /// </summary>
    /// <param name="fromBody">
    /// The legacy body field, still honoured so that an older client, or an
    /// integration posting directly, keeps working. The header wins when both
    /// are present.
    /// </param>
    protected string? GetDeviceId(string? fromBody = null)
    {
        var header = Request.Headers[DeviceHeaderNames.DeviceId].FirstOrDefault();
        var value = string.IsNullOrWhiteSpace(header) ? fromBody : header;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Bounded before it reaches the signature: the value is client-supplied
        // and the column that ultimately stores it is NVARCHAR(64).
        value = value.Trim();
        return value.Length > DeviceHeaderNames.MaxDeviceIdLength
            ? value[..DeviceHeaderNames.MaxDeviceIdLength]
            : value;
    }

    /// <summary>
    /// Resolves a success-message resource from <see cref="AuthMessages"/> for
    /// the current request culture, falling back to the English text produced
    /// by the handler when the code is missing or has no resource entry.
    /// </summary>
    protected string LocalizeMessage(string? code, string fallback, params object[] args)
    {
        if (string.IsNullOrEmpty(code))
        {
            return fallback;
        }

        var localizer = HttpContext.RequestServices
            .GetService<IStringLocalizer<AuthMessages>>();
        if (localizer is null)
        {
            return fallback;
        }

        var localized = localizer[code];
        if (localized.ResourceNotFound)
        {
            return fallback;
        }

        if (args.Length == 0)
        {
            return localized.Value;
        }

        var logger = HttpContext.RequestServices.GetService<ILogger<ApiController>>();
        return SafeFormat(localized.Value, args, fallback, logger);
    }

    /// <summary>
    /// Formats a localized resource, falling back to <paramref name="fallback"/> when its
    /// placeholders do not match the supplied arguments. Without this guard a mis-indexed
    /// format string throws while the response is being built, turning a completed operation
    /// into a 500. BaselineCoverageTests keeps placeholders consistent across cultures; this
    /// guards the neutral resource against an argument-count change on the C# side. (Error
    /// sentences have the same guard in ProblemText.)
    /// </summary>
    private static string SafeFormat(string format, object[] args, string fallback, ILogger? logger)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException exception)
        {
            logger?.LogError(
                exception,
                "Localized resource '{Format}' does not match its {ArgumentCount} argument(s) for culture {Culture}. Falling back.",
                format,
                args.Length,
                CultureInfo.CurrentUICulture.Name);

            return fallback;
        }
    }
}
