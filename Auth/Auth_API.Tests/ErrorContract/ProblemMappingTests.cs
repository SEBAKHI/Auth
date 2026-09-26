using System.Text.Json;
using System.Text.Json.Serialization;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth_API.Common;
using Auth_API.Common.Errors;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// <see cref="ApiController.Problem(IEnumerable{Error})"/> through <see cref="ProblemMapping"/>:
/// the status from the one map, <c>code</c> and <c>detail</c> from the first error, and
/// <c>errors</c> with pointers only for a Validation result with two or more failures (ADR 0001).
/// </summary>
public class ProblemMappingTests
{
    [Fact]
    public void Problem_WithNotFoundError_Returns404WithItsCodeAndSentence()
    {
        var userId = Guid.NewGuid();

        var (result, problem, json) = Map(bodyType: null, UserErrors.NotFound(userId));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Contains("application/problem+json", result.ContentTypes);
        Assert.Equal("User.NotFound", problem.Extensions["code"]);
        Assert.Equal($"User with ID '{userId}' was not found.", problem.Detail);
        Assert.Equal("Not Found", problem.Title);
        Assert.Equal("/api/v1/test", problem.Instance);
        Assert.False(json.TryGetProperty("errors", out _));
        Assert.DoesNotContain("User.NotFound", problem.Title);
    }

    [Fact]
    public void Problem_WithOneValidationFailure_Returns400WithoutErrors()
    {
        var (result, problem, json) = Map(typeof(Body), Failure("Password.NewRequired", "NewPassword"));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("Password.NewRequired", problem.Extensions["code"]);
        Assert.False(json.TryGetProperty("errors", out _));
    }

    [Fact]
    public void Problem_WithSeveralValidationFailures_Returns400WithErrorsInRuleOrder()
    {
        var (result, _, json) = Map(
            typeof(Body),
            Failure("Password.NewRequired", "NewPassword"),
            Failure("Notification.TranslationLanguageRequired", "Translations[1].LanguageCode"),
            Failure("Application.RedirectUriInvalid", "RedirectUri"),
            Failure("Paging.PageSizeOutOfRange", "PageSize"),
            Failure("Password.TooShort", string.Empty));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("Password.NewRequired", json.GetProperty("code").GetString());
        Assert.Equal(
            """[{"code":"Password.NewRequired","pointer":"#/newPassword"},{"code":"Notification.TranslationLanguageRequired","pointer":"#/translations/1/languageCode"},{"code":"Application.RedirectUriInvalid","pointer":"#/redirect_uri"},{"code":"Paging.PageSizeOutOfRange"},{"code":"Password.TooShort"}]""",
            json.GetProperty("errors").GetRawText());
    }

    [Fact]
    public void Problem_WithSeveralValidationFailuresAndNoBody_Returns400WithEntriesWithoutPointers()
    {
        var (_, _, json) = Map(
            bodyType: null,
            Failure("Paging.PageNumberOutOfRange", "PageNumber"),
            Failure("Paging.PageSizeOutOfRange", "PageSize"));

        Assert.Equal(
            """[{"code":"Paging.PageNumberOutOfRange"},{"code":"Paging.PageSizeOutOfRange"}]""",
            json.GetProperty("errors").GetRawText());
    }

    [Fact]
    public void Problem_WithSeveralConflictErrors_Returns409WithoutErrors()
    {
        var (result, problem, json) = Map(bodyType: null, ApiKeyErrors.AlreadyRevoked, PersistenceErrors.ReferenceConflict);

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(ApiKeyErrors.AlreadyRevoked.Code, problem.Extensions["code"]);
        Assert.False(json.TryGetProperty("errors", out _));
    }

    [Fact]
    public void Problem_WithAnErrorOrDefaultCode_Throws()
    {
        var controller = Controller(bodyType: null);

        Assert.Throws<InvalidOperationException>(() => controller.Map([Error.Validation(description: "no code")]));
    }

    private static Error Failure(string code, string property) => Error.Validation(
        code: code,
        description: "developer text",
        metadata: string.IsNullOrEmpty(property)
            ? null
            : new Dictionary<string, object> { [ErrorMetadataKeys.Property] = property });

    private static (ObjectResult Result, ProblemDetails Problem, JsonElement Json) Map(Type? bodyType, params Error[] errors)
    {
        var controller = Controller(bodyType);
        var result = Assert.IsType<ObjectResult>(controller.Map(errors));
        var problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);
        var options = controller.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value;
        var json = JsonSerializer.SerializeToElement(problem, options.JsonSerializerOptions);
        return (result, problem, json);
    }

    private static TestController Controller(Type? bodyType)
    {
        var httpContext = new DefaultHttpContext { RequestServices = ControllerServices.Build() };
        httpContext.Request.Path = "/api/v1/test";

        var action = new ControllerActionDescriptor
        {
            Parameters = bodyType is null
                ? []
                :
                [
                    new ParameterDescriptor
                    {
                        Name = "request",
                        ParameterType = bodyType,
                        BindingInfo = new BindingInfo { BindingSource = BindingSource.Body },
                    },
                ],
        };

        return new TestController
        {
            ControllerContext = new ControllerContext(new ActionContext(httpContext, new RouteData(), action)),
        };
    }

    private sealed class TestController : ApiController
    {
        public IActionResult Map(IEnumerable<Error> errors) => Problem(errors);
    }

    private sealed record Body(
        string NewPassword,
        List<Translation> Translations,
        [property: JsonPropertyName("redirect_uri")] string RedirectUri);

    private sealed record Translation(string LanguageCode);
}
