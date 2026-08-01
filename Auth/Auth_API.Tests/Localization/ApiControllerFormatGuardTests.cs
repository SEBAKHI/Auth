using Auth_API.Common;
using Auth_Localization.Resources;
using Auth_Localization.Resources.Errors;
using Auth_Localization.Resources.Validation;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Auth_API.Tests.Localization;

/// <summary>
/// Covers the format guard in <see cref="ApiController"/>. A localized resource whose placeholders
/// do not match the supplied arguments must degrade to the untranslated fallback rather than throw:
/// the formatting happens while the error response is being built, so an exception there converts a
/// clean 400/404 into a 500 on the one path least likely to be exercised in testing.
/// </summary>
public class ApiControllerFormatGuardTests
{
    private sealed class TestController : ApiController
    {
        public IActionResult InvokeProblem(IEnumerable<Error> errors) => Problem(errors);

        public string InvokeLocalizeMessage(string code, string fallback, params object[] args) =>
            LocalizeMessage(code, fallback, args);
    }

    [Fact]
    public void Problem_WhenDomainResourceExpectsMoreArgumentsThanSupplied_FallsBackToDescription()
    {
        // "{1}" needs two arguments; the error carries one. Unguarded, string.Format throws.
        var controller = CreateController(domainErrorValue: "Locked until {0} by {1}.");
        var error = Error.NotFound(
            code: "User.NotFound",
            description: "User with ID 'abc' was not found.",
            metadata: new Dictionary<string, object> { ["args"] = new object[] { "abc" } });

        var result = controller.InvokeProblem([error]);

        var problemDetails = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        problemDetails.Detail.Should().Be(
            "User with ID 'abc' was not found.",
            "a placeholder mismatch must degrade to the English description, never throw");
        problemDetails.Status.Should().Be(StatusCodes.Status404NotFound,
            "the guard must preserve the original status code rather than escalate to 500");
    }

    [Fact]
    public void Problem_WhenDomainResourceHasMalformedBrace_FallsBackToDescription()
    {
        // An unclosed brace — e.g. a translator typing a literal "{" — is also a FormatException.
        var controller = CreateController(domainErrorValue: "Missing user {0");
        var error = Error.NotFound(
            code: "User.NotFound",
            description: "User with ID 'abc' was not found.",
            metadata: new Dictionary<string, object> { ["args"] = new object[] { "abc" } });

        var result = controller.InvokeProblem([error]);

        var problemDetails = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        problemDetails.Detail.Should().Be("User with ID 'abc' was not found.");
    }

    [Fact]
    public void Problem_WhenDomainResourcePlaceholdersMatch_StillFormatsNormally()
    {
        // Regression: the guard must not change the behaviour of well-formed resources.
        var controller = CreateController(domainErrorValue: "User '{0}' was not found.");
        var error = Error.NotFound(
            code: "User.NotFound",
            description: "User with ID 'abc' was not found.",
            metadata: new Dictionary<string, object> { ["args"] = new object[] { "abc" } });

        var result = controller.InvokeProblem([error]);

        var problemDetails = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        problemDetails.Detail.Should().Be("User 'abc' was not found.");
    }

    [Fact]
    public void Problem_WhenValidationResourceHasPlaceholders_FillsThemFromMetadata()
    {
        // Regression: validation messages quote configured policy values (the
        // password minimum length is the live PasswordSettings value). Skipping
        // the format showed users a literal "{0}" instead of the policy.
        var controller = CreateController(validationMessageValue: "Password must be at least {0} characters long.");
        var error = Error.Validation(
            code: "Password.TooShort",
            description: "Validation.Password.TooShort",
            metadata: new Dictionary<string, object> { ["args"] = new object[] { 24 } });

        var result = controller.InvokeProblem([error]);

        var problemDetails = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        problemDetails.Detail.Should().Be("Password must be at least 24 characters long.");
    }

    [Fact]
    public void Problem_WhenValidationResourcePlaceholdersMismatch_FallsBackToDescription()
    {
        var controller = CreateController(validationMessageValue: "At least {0} and {1}.");
        var error = Error.Validation(
            code: "Password.TooShort",
            description: "Validation.Password.TooShort",
            metadata: new Dictionary<string, object> { ["args"] = new object[] { 24 } });

        var result = controller.InvokeProblem([error]);

        var problemDetails = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(result).Value);
        problemDetails.Detail.Should().Be("Validation.Password.TooShort");
    }

    [Fact]
    public void LocalizeMessage_WhenResourceExpectsMoreArgumentsThanSupplied_FallsBackToCallerText()
    {
        var controller = CreateController(authMessageValue: "Rotated {0} of {1}.");

        var message = controller.InvokeLocalizeMessage("ApiKey.Rotated", "API key rotated.", "key-1");

        message.Should().Be("API key rotated.", "the caller's English text is the documented fallback");
    }

    [Fact]
    public void LocalizeMessage_WhenResourcePlaceholdersMatch_StillFormatsNormally()
    {
        var controller = CreateController(authMessageValue: "Rotated {0}.");

        var message = controller.InvokeLocalizeMessage("ApiKey.Rotated", "API key rotated.", "key-1");

        message.Should().Be("Rotated key-1.");
    }

    private static TestController CreateController(
        string? domainErrorValue = null,
        string? authMessageValue = null,
        string? validationMessageValue = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(StubLocalizer<DomainErrors>(domainErrorValue));
        services.AddSingleton(StubLocalizer<ValidationMessages>(validationMessageValue));
        services.AddSingleton(StubLocalizer<AuthMessages>(authMessageValue));

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        return new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    /// <summary>
    /// A localizer that returns <paramref name="value"/> for any key, or reports the resource as
    /// missing when <paramref name="value"/> is null — mirroring ResourceManager's contract.
    /// </summary>
    private static IStringLocalizer<T> StubLocalizer<T>(string? value)
    {
        var localizer = new Mock<IStringLocalizer<T>>();
        localizer
            .Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => value is null
                ? new LocalizedString(name, name, resourceNotFound: true)
                : new LocalizedString(name, value, resourceNotFound: false));

        return localizer.Object;
    }
}
