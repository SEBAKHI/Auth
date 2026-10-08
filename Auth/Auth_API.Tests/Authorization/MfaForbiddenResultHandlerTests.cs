using System.Security.Claims;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Auth_API.Tests.Authorization;

/// <summary>
/// S08 T12: a refusal of a token that carries <c>mfa_req</c> answers 403 with the
/// published code <c>TwoFactor.RequiredByPolicy</c> and the RFC 9470 challenge; every
/// other result goes to the framework's default handler, exactly as before. The
/// handler writes no body: it records the code and the status, and the status-code
/// pages write the problem (the wire is asserted in UserInfoEndpointTests).
/// </summary>
public class MfaForbiddenResultHandlerTests
{
    private static readonly AuthorizationPolicy Policy =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    private readonly Mock<IAuthenticationService> _authentication = new();

    private DefaultHttpContext Context(params Claim[] claims)
    {
        var services = new ServiceCollection()
            .AddSingleton(_authentication.Object)
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtClaimNames.Subject, Guid.NewGuid().ToString()), .. claims], "Bearer")),
        };
    }

    private static PolicyAuthorizationResult Forbid() =>
        PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([new DenyAnonymousAuthorizationRequirement()]));

    [Fact]
    public async Task HandleAsync_ForbiddenWithMfaReq_Returns403WithTheCodeAndTheChallenge()
    {
        var context = Context(new Claim(JwtClaimNames.MfaRequirement, MfaRequirementValues.StepUp));
        var nextCalled = false;

        await new MfaForbiddenResultHandler().HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, Policy, Forbid());

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Items[ProblemItems.Code].Should().Be(TwoFactorErrors.RequiredByPolicy.Code);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("error=\"insufficient_user_authentication\"");
        nextCalled.Should().BeFalse("the endpoint never runs");
        _authentication.Verify(
            a => a.ForbidAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()),
            Times.Never, "the code is the handler's to name, not the scheme's");
    }

    [Fact]
    public async Task HandleAsync_ForbiddenWithoutMfaReq_IsTheDefaultBehaviour()
    {
        // The break this exists for: a handler that ignored the claim would answer
        // every refusal with the two-factor code.
        var context = Context(new Claim(JwtClaimNames.Permissions, "users:read"));

        await new MfaForbiddenResultHandler().HandleAsync(_ => Task.CompletedTask, context, Policy, Forbid());

        context.Items.Should().NotContainKey(ProblemItems.Code);
        context.Response.Headers.WWWAuthenticate.ToString().Should().NotContain("insufficient_user_authentication");
        _authentication.Verify(
            a => a.ForbidAsync(context, It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()),
            Times.Once, "the framework's default handler forbids through the scheme, as before");
    }

    [Fact]
    public async Task HandleAsync_SuccessWithMfaReq_RunsTheEndpoint()
    {
        // The token is still a token: the endpoints that need no platform permission
        // — the profile, the two-factor ones, /me — answer as usual.
        var context = Context(new Claim(JwtClaimNames.MfaRequirement, MfaRequirementValues.Enroll));
        var nextCalled = false;

        await new MfaForbiddenResultHandler().HandleAsync(
            _ => { nextCalled = true; return Task.CompletedTask; }, context, Policy, PolicyAuthorizationResult.Success());

        nextCalled.Should().BeTrue();
        context.Items.Should().NotContainKey(ProblemItems.Code);
    }

    [Fact]
    public async Task HandleAsync_ChallengeWithMfaReq_IsTheDefaultBehaviour()
    {
        // An unauthenticated answer is the scheme's, whatever the principal says.
        var context = Context(new Claim(JwtClaimNames.MfaRequirement, MfaRequirementValues.StepUp));

        await new MfaForbiddenResultHandler().HandleAsync(_ => Task.CompletedTask, context, Policy, PolicyAuthorizationResult.Challenge());

        context.Items.Should().NotContainKey(ProblemItems.Code);
        _authentication.Verify(
            a => a.ChallengeAsync(context, It.IsAny<string?>(), It.IsAny<AuthenticationProperties?>()), Times.Once);
    }
}
