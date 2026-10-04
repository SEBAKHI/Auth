using System.Text.Json;
using Auth.Application.Features.Applications.CreateApplication;
using Auth.Application.Features.Applications.UpdateApplication;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth_API.Common;
using Auth_API.Modules.ApplicationManagement.Contracts;
using Auth_API.Tests.ErrorContract;
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

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// An application's <c>allowedScopes</c> at the API edge (OI-58, B9): an unknown name
/// is a 400 with the published code <c>Application.AllowedScopesInvalid</c> and the
/// pointer <c>#/allowedScopes</c>, on both the create and the update contract.
/// </summary>
public class AllowedScopesValidationTests
{
    [Theory]
    [InlineData("address")]
    [InlineData("Phone")]
    [InlineData("phone_number")]
    [InlineData("offline_access")]
    public void Validate_CreateWithUnknownScope_FailsWithAllowedScopesInvalidOnTheList(string scope)
    {
        var result = new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("EDIS", "EDIS", AllowedScopes: ["email", scope]));

        var failure = Assert.Single(result.Errors);
        Assert.Equal(ApplicationErrors.AllowedScopesInvalid.Code, failure.ErrorCode);
        Assert.Equal(nameof(CreateApplicationCommand.AllowedScopes), failure.PropertyName);
    }

    [Fact]
    public void Validate_UpdateWithUnknownScope_FailsWithAllowedScopesInvalidOnTheList()
    {
        var result = new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "EDIS", AllowedScopes: ["address"]));

        var failure = Assert.Single(result.Errors);
        Assert.Equal(ApplicationErrors.AllowedScopesInvalid.Code, failure.ErrorCode);
        Assert.Equal(nameof(UpdateApplicationCommand.AllowedScopes), failure.PropertyName);
    }

    public static TheoryData<string[]?> ValidScopeLists => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "openid" },
        new[] { "openid", "profile", "email", "phone" },
    };

    [Fact]
    public void ValidScopeLists_HasCases()
    {
        ValidScopeLists.Count<object[]>().Should().BeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(ValidScopeLists))]
    public void Validate_KnownOrAbsentScopes_AreValid(string[]? scopes)
    {
        Assert.True(new CreateApplicationCommandValidator().Validate(
            new CreateApplicationCommand("EDIS", "EDIS", AllowedScopes: scopes)).IsValid);
        Assert.True(new UpdateApplicationCommandValidator().Validate(
            new UpdateApplicationCommand(Guid.NewGuid(), "EDIS", AllowedScopes: scopes)).IsValid);
    }

    [Fact]
    public void TheCode_IsPublishedAsA400WithThePointerToTheList()
    {
        var published = PublishedErrorCodes.All[ApplicationErrors.AllowedScopesInvalid.Code];

        Assert.Equal(400, published.Status);
        Assert.Equal("catalog", published.Source);
        Assert.Equal("#/allowedScopes", published.Pointer);
    }

    [Theory]
    [InlineData(typeof(CreateApplicationRequest))]
    [InlineData(typeof(UpdateApplicationRequest))]
    public void TheValidatorsProperty_MapsToThePublishedPointer_OnTheRequestBody(Type bodyType)
    {
        // Two failures, so the problem carries `errors` with pointers: the pointer
        // the API derives from the validator's property must be the one published.
        var json = Map(
            bodyType,
            Failure(ApplicationErrors.AllowedScopesInvalid.Code, "AllowedScopes"),
            Failure(NameErrors.Required.Code, "Name"));

        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.Equal(ApplicationErrors.AllowedScopesInvalid.Code, json.GetProperty("code").GetString());
        var entry = json.GetProperty("errors")[0];
        Assert.Equal(ApplicationErrors.AllowedScopesInvalid.Code, entry.GetProperty("code").GetString());
        Assert.Equal("#/allowedScopes", entry.GetProperty("pointer").GetString());
    }

    private static Error Failure(string code, string property) => Error.Validation(
        code: code,
        description: "developer text",
        metadata: new Dictionary<string, object> { [ErrorMetadataKeys.Property] = property });

    private static JsonElement Map(Type bodyType, params Error[] errors)
    {
        var httpContext = new DefaultHttpContext { RequestServices = ControllerServices.Build() };
        httpContext.Request.Path = "/api/v1/applications";

        var action = new ControllerActionDescriptor
        {
            Parameters =
            [
                new ParameterDescriptor
                {
                    Name = "request",
                    ParameterType = bodyType,
                    BindingInfo = new BindingInfo { BindingSource = BindingSource.Body },
                },
            ],
        };

        var controller = new TestController
        {
            ControllerContext = new ControllerContext(new ActionContext(httpContext, new RouteData(), action)),
        };

        var result = Assert.IsType<ObjectResult>(controller.Map(errors));
        var problem = Assert.IsAssignableFrom<ProblemDetails>(result.Value);
        var options = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value;
        return JsonSerializer.SerializeToElement(problem, options.JsonSerializerOptions);
    }

    private sealed class TestController : ApiController
    {
        public IActionResult Map(IEnumerable<Error> errors) => Problem(errors);
    }
}
