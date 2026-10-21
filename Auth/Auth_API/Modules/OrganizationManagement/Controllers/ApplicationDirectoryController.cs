using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common;
using Auth_API.Common.Errors;
using Auth_API.Common.Middleware;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Auth_API.Modules.OrganizationManagement.Controllers;

/// <summary>
/// What the application directory's two front doors share (OI-194, OI-202): the answer for a
/// directory result, and the query binder. A user's application token reads through
/// <see cref="ApplicationOrganizationsController"/>; an application's machine token through
/// <see cref="ApplicationMachineOrganizationsController"/>. Each names its own scheme and its own
/// scope challenge; neither names a guard of any other kind.
/// </summary>
public abstract class ApplicationDirectoryController : ApiController
{
    /// <summary>RFC 6750 §3.1: the challenge of a 403 for the scope this door requires.</summary>
    protected abstract string InsufficientScopeChallenge { get; }

    /// <summary>
    /// The answer for a directory result: 200; the revoked-token 401 for a caller that can no
    /// longer be served; the scope challenge with a missing scope's 403; otherwise the error
    /// contract's problem. Every answer on these endpoints carries <c>Cache-Control: no-store</c>,
    /// set before the limiter by <see cref="ApplicationReadClassificationMiddleware"/>, so the 401
    /// written here and the blacklist's carry the same headers.
    /// </summary>
    protected IActionResult Answer<TValue>(ErrorOr<TValue> result)
    {
        if (result.IsError && result.FirstError.Code == ApplicationDirectoryRefusals.CallerNotServable.Code)
        {
            BearerTokenRejection.Reject(HttpContext, ChallengeReasonCodes.TokenRevoked);
            return new EmptyResult();
        }

        if (!result.IsError)
        {
            return Ok(result.Value);
        }

        var first = result.FirstError;

        if (first.Code == ApplicationDirectoryRefusals.InsufficientScope.Code)
        {
            Response.Headers.WWWAuthenticate = InsufficientScopeChallenge;
        }

        return Problem(result.Errors);
    }

    /// <summary>
    /// Binds a query parameter exactly as the request carried it: a string, or every value of a
    /// repeated parameter, with no conversion and no failure. Nothing about the query can be
    /// refused before the action runs, so the handler answers "who are you" first and reads the
    /// values after it (<see cref="ApplicationDirectoryPaging"/>). An absent parameter is null; an
    /// empty one is the empty string, which the handler refuses, rather than null.
    /// </summary>
    internal sealed class RawQueryValuesBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (values == ValueProviderResult.None)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
                return Task.CompletedTask;
            }

            object model = bindingContext.ModelType == typeof(string[])
                ? values.Values.Select(value => value ?? string.Empty).ToArray()
                : values.FirstValue ?? string.Empty;
            bindingContext.Result = ModelBindingResult.Success(model);
            return Task.CompletedTask;
        }
    }
}
