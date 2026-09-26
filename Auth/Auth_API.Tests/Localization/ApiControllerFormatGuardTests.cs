using Auth_API.Common;
using Auth_Localization.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Xunit;

namespace Auth_API.Tests.Localization;

/// <summary>
/// Covers the format guard in <see cref="ApiController"/>'s success messages. A localized resource
/// whose placeholders do not match the supplied arguments must degrade to the caller's English text
/// rather than throw, which would turn a completed operation into a 500. Error sentences have the
/// same guard in ProblemText (<see cref="ErrorContract.ProblemTextTests"/>).
/// </summary>
public class ApiControllerFormatGuardTests
{
    private sealed class TestController : ApiController
    {
        public string InvokeLocalizeMessage(string code, string fallback, params object[] args) =>
            LocalizeMessage(code, fallback, args);
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

    private static TestController CreateController(string authMessageValue)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(StubLocalizer<AuthMessages>(authMessageValue));

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        return new TestController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    /// <summary>
    /// A localizer that returns <paramref name="value"/> for any key.
    /// </summary>
    private static IStringLocalizer<T> StubLocalizer<T>(string value)
    {
        var localizer = new Mock<IStringLocalizer<T>>();
        localizer
            .Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => new LocalizedString(name, value, resourceNotFound: false));

        return localizer.Object;
    }
}
