using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Common;

/// <summary>
/// Base controller that provides unified ErrorOr-to-HTTP response mapping.
/// All API controllers should inherit from this instead of ControllerBase.
/// </summary>
[ApiController]
public abstract class ApiController : ControllerBase
{
    protected IActionResult Problem(IEnumerable<ErrorOr.Error> errors)
    {
        var firstError = errors.First();

        var statusCode = firstError.Type switch
        {
            ErrorOr.ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorOr.ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorOr.ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorOr.ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorOr.ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = firstError.Code,
            Detail = firstError.Description,
            Instance = Request.Path
        };

        if (errors.Count() > 1)
        {
            problemDetails.Extensions["errors"] = errors.Select(e => new
            {
                code = e.Code,
                description = e.Description
            });
        }

        return StatusCode(statusCode, problemDetails);
    }

    protected Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}
