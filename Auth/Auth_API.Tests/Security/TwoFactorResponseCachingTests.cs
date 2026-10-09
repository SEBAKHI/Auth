using System.Reflection;
using Auth_API.Modules.Authentication.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Security;

/// <summary>
/// S08 PR B, review fix F9 (b): the two-factor endpoints answer secrets — a new
/// authenticator key (setup, replace), recovery codes (enable, new codes,
/// confirm), tokens (verify) — so none of their answers may be stored by a
/// browser cache or a proxy. Nothing global sets <c>Cache-Control</c> on API
/// responses, so the controller carries it, and an action may not opt out.
/// </summary>
public class TwoFactorResponseCachingTests
{
    [Fact]
    public void TheController_ForbidsStoringItsAnswers()
    {
        var cache = typeof(TwoFactorController).GetCustomAttribute<ResponseCacheAttribute>();

        cache.Should().NotBeNull("every answer of this controller can carry a secret");
        cache!.NoStore.Should().BeTrue();
        cache.Location.Should().Be(ResponseCacheLocation.None);
    }

    [Fact]
    public void NoAction_OverridesIt()
    {
        var overriding = typeof(TwoFactorController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<ResponseCacheAttribute>() is not null)
            .Select(method => method.Name);

        overriding.Should().BeEmpty("an action-level attribute would replace the controller's");
    }
}
