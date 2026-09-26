using Auth.Shared.Http.ErrorContract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Auth_API.Common.Errors;

/// <summary>
/// The API host's part of the error contract (ADR 0001), on top of the pipeline both hosts share.
/// </summary>
public static class ApiErrorContractExtensions
{
    /// <summary>
    /// Registers the shared pipeline and the SQL translator, and routes MVC's own 400s through
    /// the same writer:
    /// <list type="bullet">
    /// <item>a malformed body or a failed binding is a problem with
    /// <see cref="TransportErrorCodes.BadRequest"/> and no <c>errors</c>;</item>
    /// <item>requiredness is the validators' to report, with its catalog code, so a
    /// non-nullable member no longer carries an implicit <c>[Required]</c>.</item>
    /// </list>
    /// </summary>
    public static IMvcBuilder AddApiErrorContract(this IMvcBuilder mvc)
    {
        mvc.Services.AddErrorContract();
        mvc.Services.AddSingleton<IExceptionProblemTranslator, SqlExceptionProblemTranslator>();

        mvc.AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
        mvc.ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
        {
            var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
            var problem = factory.CreateProblemDetails(context.HttpContext, StatusCodes.Status400BadRequest);

            return new ObjectResult(problem)
            {
                StatusCode = problem.Status,
                ContentTypes = { "application/problem+json" },
            };
        });

        return mvc;
    }
}
