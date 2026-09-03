using System.Reflection;
using Auth.Application.DTOs;
using Auth_API.Modules.Administration.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth_API.Tests.Api;

/// <summary>
/// <c>GET /Platform/password-policy</c> is anonymous, so its payload is a
/// statement to the whole internet about how this deployment is configured.
///
/// The five composition rules are safe to publish: a registration form has to
/// show them, and every one is already implied by the validation error it
/// produces. The rest of <c>PasswordSettings</c> is not. <c>MaxFailedAttempts</c>
/// tells an attacker exactly how many guesses a lock-out costs, <c>HistoryCount</c>
/// and the Argon2 parameters describe the defence, and the pepper and
/// breach-check sections are operational detail. A property added to the DTO is
/// therefore a disclosure decision, and this test makes it cost a deliberate
/// edit here rather than a silently passing build.
/// </summary>
public class PasswordPolicyDisclosureTests
{
    private static readonly string[] Disclosed =
    [
        nameof(PasswordPolicyDto.MinimumLength),
        nameof(PasswordPolicyDto.RequireUppercase),
        nameof(PasswordPolicyDto.RequireLowercase),
        nameof(PasswordPolicyDto.RequireDigit),
        nameof(PasswordPolicyDto.RequireSpecialCharacter)
    ];

    [Fact]
    public void PasswordPolicyDto_DisclosesExactlyTheCompositionRules()
    {
        var published = typeof(PasswordPolicyDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name);

        published.Should().BeEquivalentTo(
            Disclosed,
            "the public policy payload may carry only what a person needs to compose a password");
    }

    [Fact]
    public void PasswordPolicyEndpoint_IsAnonymousAndReadOnly()
    {
        var endpoint = typeof(PlatformController).GetMethod(nameof(PlatformController.GetPasswordPolicy));

        endpoint.Should().NotBeNull();
        endpoint!.GetCustomAttributes<HttpGetAttribute>().Should().ContainSingle()
            .Which.Template.Should().Be("password-policy");
        endpoint.GetCustomAttributes<AllowAnonymousAttribute>().Should().NotBeEmpty(
            "sign-up, invitation and reset forms fetch the policy before any session exists");
        endpoint.GetCustomAttributes<HttpPostAttribute>().Should().BeEmpty();
        endpoint.GetCustomAttributes<HttpPutAttribute>().Should().BeEmpty();
    }
}
