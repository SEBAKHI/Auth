using Microsoft.AspNetCore.Mvc;
using Foundation_Lib.Api.Responses;

namespace AuthShared_Lib.Api;

/// <summary>
/// Base controller providing standardized API response methods for all controllers.
/// Uses Foundation_Lib response types for consistency across all United Education APIs.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Returns a successful response (200 OK) with data
    /// </summary>
    protected IActionResult Ok<T>(T data, string? message = null)
    {
        return base.Ok(ApiResponse<T>.SuccessResponse(data, message));
    }

    /// <summary>
    /// Returns a created response (201 Created) with data
    /// </summary>
    protected IActionResult Created<T>(T data, string? message = null)
    {
        return StatusCode(201, ApiResponse<T>.SuccessResponse(data, message));
    }

    /// <summary>
    /// Returns a bad request response (400 Bad Request)
    /// </summary>
    protected IActionResult BadRequest(string message, string errorCode, Dictionary<string, string>? errors = null)
    {
        return base.BadRequest(ApiResponse.ErrorResponse(message, errorCode, errors));
    }

    /// <summary>
    /// Returns a not found response (404 Not Found)
    /// </summary>
    protected IActionResult NotFound(string message, string errorCode)
    {
        return base.NotFound(ApiResponse.ErrorResponse(message, errorCode));
    }

    /// <summary>
    /// Returns an unauthorized response (401 Unauthorized)
    /// </summary>
    protected IActionResult Unauthorized(string message, string errorCode)
    {
        return base.Unauthorized(ApiResponse.ErrorResponse(message, errorCode));
    }

    /// <summary>
    /// Returns a forbidden response (403 Forbidden)
    /// </summary>
    protected IActionResult Forbidden(string message, string errorCode)
    {
        return StatusCode(403, ApiResponse.ErrorResponse(message, errorCode));
    }

    /// <summary>
    /// Returns an internal server error response (500 Internal Server Error)
    /// </summary>
    protected IActionResult InternalServerError(string message, string errorCode)
    {
        return StatusCode(500, ApiResponse.ErrorResponse(message, errorCode));
    }
}
