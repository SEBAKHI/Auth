using Auth.Application.Common;
using Auth.Application.Configuration;
using Auth.Domain.Enums;
using Auth_API.Tests.Helpers;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.ApplicationManagement;

/// <summary>
/// OI-192: where an accepted invitation sends the invitee. One rule, on the server, from
/// the application's registration alone: the initiate-login URI with the issuer appended
/// (OpenID Connect Core 1.0 §4), else the home URL the branding endpoint offers, else
/// nothing; and only when the invitee now holds the role in an application that is on.
/// </summary>
public class ApplicationLaunchTargetTests
{
    private const string Issuer = "https://id.example.com/auth";
    private const string EscapedIssuer = "https%3A%2F%2Fid.example.com%2Fauth";

    private static readonly ApplicationLaunchTarget Target =
        new(TestHelpers.CreateOptions(new JwtSettings { Issuer = Issuer }));

    private static ApplicationEntity App(
        string? initiateLoginUri = null,
        string? baseUrl = "https://crm.example.com",
        bool isActive = true,
        bool browserSignIn = true)
    {
        var application = TestHelpers.CreateApplication(code: "CRM", baseUrl: baseUrl, isActive: isActive);
        application.LoadInitiateLoginUri(initiateLoginUri);
        if (browserSignIn)
        {
            application.LoadRedirectUris(["https://crm.example.com/callback"]);
        }

        return application;
    }

    [Theory]
    [InlineData(InvitationApplicationRoleOutcome.Applied)]
    [InlineData(InvitationApplicationRoleOutcome.AlreadyHeld)]
    public void For_HeldRoleAndAnInitiateLoginUri_LaunchesItWithTheIssuer(InvitationApplicationRoleOutcome outcome)
    {
        Target.For(App("https://crm.example.com/signin/start"), outcome)
            .Should().Be($"https://crm.example.com/signin/start?iss={EscapedIssuer}");
    }

    [Fact]
    public void For_AnInitiateLoginUriWithAQuery_AppendsTheIssuerWithAnAmpersand()
    {
        Target.For(App("https://crm.example.com/signin/start?intent=signin"), InvitationApplicationRoleOutcome.Applied)
            .Should().Be($"https://crm.example.com/signin/start?intent=signin&iss={EscapedIssuer}");
    }

    [Fact]
    public void For_NoInitiateLoginUri_FallsBackToTheHomeUrlWithoutTheIssuer()
    {
        Target.For(App(initiateLoginUri: null, baseUrl: "https://crm.example.com/app"), InvitationApplicationRoleOutcome.Applied)
            .Should().Be("https://crm.example.com/app");
    }

    [Fact]
    public void For_NoInitiateLoginUriAndNoBrowserSignIn_LaunchesNothing()
    {
        // The home-URL rule, unchanged: a machine client never exposes its address.
        Target.For(App(initiateLoginUri: null, browserSignIn: false), InvitationApplicationRoleOutcome.Applied)
            .Should().BeNull();
    }

    [Fact]
    public void For_NoInitiateLoginUriAndAnUnsafeBaseUrl_LaunchesNothing()
    {
        Target.For(App(initiateLoginUri: null, baseUrl: "javascript:alert(1)"), InvitationApplicationRoleOutcome.Applied)
            .Should().BeNull();
        Target.For(App(initiateLoginUri: null, baseUrl: "http://crm.example.com"), InvitationApplicationRoleOutcome.Applied)
            .Should().BeNull();
    }

    [Fact]
    public void For_AStoredInitiateLoginUriTheRuleRefuses_IsNotLaunched()
    {
        // A save refuses these; a row written by any other means must not become a
        // destination either. The home URL still applies.
        Target.For(App("javascript:alert(1)", browserSignIn: false), InvitationApplicationRoleOutcome.Applied).Should().BeNull();
        Target.For(App("http://crm.example.com/login", browserSignIn: false), InvitationApplicationRoleOutcome.Applied).Should().BeNull();
        Target.For(App("https://crm.example.com@evil.example.com/login", browserSignIn: false), InvitationApplicationRoleOutcome.Applied).Should().BeNull();
        Target.For(App("https://crm.example.com/login#start", browserSignIn: false), InvitationApplicationRoleOutcome.Applied).Should().BeNull();
        Target.For(App("http://127.0.0.2/login", browserSignIn: false), InvitationApplicationRoleOutcome.Applied).Should().BeNull();
        Target.For(App("https://crm.example.com/login?iss=https://other.example.com", browserSignIn: false), InvitationApplicationRoleOutcome.Applied)
            .Should().BeNull("a second iss would leave the application to pick one of two");
        Target.For(App("/signin/start", baseUrl: "https://crm.example.com"), InvitationApplicationRoleOutcome.Applied)
            .Should().Be("https://crm.example.com/");
    }

    [Theory]
    [InlineData(InvitationApplicationRoleOutcome.Dropped)]
    [InlineData(null)]
    public void For_NoHeldRole_LaunchesNothing(InvitationApplicationRoleOutcome? outcome)
    {
        // The invitee would land in an application that turns them away.
        Target.For(App("https://crm.example.com/signin/start"), outcome).Should().BeNull();
    }

    [Fact]
    public void For_ASwitchedOffApplication_LaunchesNothing()
    {
        Target.For(App("https://crm.example.com/signin/start", isActive: false), InvitationApplicationRoleOutcome.Applied)
            .Should().BeNull();
    }

    [Fact]
    public void For_NoApplication_LaunchesNothing()
    {
        Target.For(null, InvitationApplicationRoleOutcome.Applied).Should().BeNull();
    }

    [Fact]
    public void HomeUrlOf_IsTheBrandingRule()
    {
        ApplicationLaunchTarget.HomeUrlOf(App(baseUrl: "http://localhost:3000")).Should().Be("http://localhost:3000/");
        ApplicationLaunchTarget.HomeUrlOf(App(baseUrl: "https://crm.example.com")).Should().Be("https://crm.example.com/");
        ApplicationLaunchTarget.HomeUrlOf(App(browserSignIn: false)).Should().BeNull();

        var serverSide = App(browserSignIn: false);
        serverSide.LoadConfidentialRedirectUris(["https://crm.example.com/signin/callback"]);
        ApplicationLaunchTarget.HomeUrlOf(serverSide).Should().Be("https://crm.example.com/");
    }
}
