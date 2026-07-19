using System.Globalization;
using Auth.Domain.Constants;
using Auth_API.Authorization;
using Auth_Localization.Resources;
using Auth_Localization.Resources.Errors;
using Auth_Localization.Resources.Validation;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Auth_API.Common;

/// <summary>
/// Base controller that provides unified ErrorOr-to-HTTP response mapping
/// with localized error descriptions.
/// All API controllers should inherit from this instead of ControllerBase.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected IActionResult Problem(IEnumerable<Error> errors)
    {
        var domainLocalizer = HttpContext.RequestServices
            .GetService<IStringLocalizer<DomainErrors>>();
        var validationLocalizer = HttpContext.RequestServices
            .GetService<IStringLocalizer<ValidationMessages>>();
        var logger = HttpContext.RequestServices
            .GetService<ILogger<ApiController>>();

        var firstError = errors.First();

        var statusCode = firstError.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = firstError.Code,
            Detail = LocalizeError(firstError, domainLocalizer, validationLocalizer, logger),
            Instance = Request.Path
        };

        if (errors.Count() > 1)
        {
            problemDetails.Extensions["errors"] = errors.Select(e => new
            {
                code = e.Code,
                description = LocalizeError(e, domainLocalizer, validationLocalizer, logger)
            });
        }

        return StatusCode(statusCode, problemDetails);
    }

    protected Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
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

    protected string? GetClientIpAddress()
    {
        return ClientIpResolver.Resolve(HttpContext);
    }

    protected string? GetUserAgent()
    {
        return Request.Headers.UserAgent.FirstOrDefault();
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
    /// format string throws while the error response is being built, turning a clean 404 or
    /// 400 into a 500 — the failure surfaces on the error path, where it is least visible.
    /// BaselineCoverageTests keeps placeholders consistent across cultures; this guards the
    /// neutral resource against an argument-count change on the C# side.
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

    private static string LocalizeError(
        Error error,
        IStringLocalizer<DomainErrors>? domainLocalizer,
        IStringLocalizer<ValidationMessages>? validationLocalizer,
        ILogger? logger)
    {
        // 1. Try domain errors (keyed by error code, e.g., "User.InvalidCredentials")
        if (domainLocalizer is not null)
        {
            var localized = domainLocalizer[error.Code];
            if (!localized.ResourceNotFound)
            {
                if (error.Metadata?.TryGetValue("args", out var argsObj) == true
                    && argsObj is object[] args)
                {
                    return SafeFormat(localized.Value, args, error.Description, logger);
                }

                return localized.Value;
            }
        }

        // 2. Try validation messages (keyed by error description as resource key)
        if (validationLocalizer is not null)
        {
            var localized = validationLocalizer[error.Description];
            if (!localized.ResourceNotFound)
            {
                return localized.Value;
            }
        }

        // 3. Fallback to original English description
        return error.Description;
    }
}
