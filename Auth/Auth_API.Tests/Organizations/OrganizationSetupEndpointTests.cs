using System.Net;
using System.Reflection;
using Auth.Application.Configuration;
using Auth.Application.Features.Organizations.OrganizationSetup;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Application.SystemSettings;
using Auth_API.Common.FirstParty;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Modules.Authentication.Controllers;
using Auth_API.Tests.Authentication.FirstParty;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using static Auth_API.Tests.Authentication.FirstParty.FirstPartyHost;

namespace Auth_API.Tests.Organizations;

/// <summary>
/// OI-63: the two organization-setup endpoints on the real controller, the
/// audit row the step leaves, and the platform setting behind its limit.
/// </summary>
public class OrganizationSetupEndpointTests
{
    private const string Path = "/api/v1/auth/organization-setup";
    private const string Body = """{"clientId":"EDIS","name":"Expo House"}""";

    private static MethodInfo Action(string name) =>
        typeof(AuthController).GetMethod(name) ?? throw new InvalidOperationException(name);

    [Theory]
    [InlineData(nameof(AuthController.GetOrganizationSetup), "sign-in-page")]
    [InlineData(nameof(AuthController.SetUpOrganization), "login")]
    public void BothActions_AreCookieAuthenticated_OriginGuarded_AndThrottled(string action, string policy)
    {
        var method = Action(action);

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull(
            "the browser arrives with the single sign-on cookie, not a bearer token");
        method.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
        method.GetCustomAttribute<RequireFirstPartyOriginAttribute>().Should().NotBeNull(
            "a cookie credential needs the first-party Origin barrier");
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.Should().Be(policy);
    }

    [Theory]
    [InlineData(Sibling)]
    [InlineData(Apex)]
    [InlineData("null")]
    public async Task Post_FromAnOriginThatIsNotFirstParty_IsRefused_BeforeTheHandler(string origin)
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);

        var response = await host.PostAsync(Path, Body, origin: origin, cookies: ["auth_idp=session-cookie"]);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ProblemCodeAsync(response)).Should().Be(AuthErrors.FirstPartyOriginRequired.Code);
        host.Sender.Verify(
            s => s.Send(It.IsAny<SetUpOrganizationCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Post_WithoutTheCookie_ButSignedInByBearer_Is401_AndTheHandlerSeesNoSession()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);
        SetUpOrganizationCommand? sent = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<SetUpOrganizationCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<SetUpOrganizationResult>> command, CancellationToken _) =>
                sent = (SetUpOrganizationCommand)command)
            .ReturnsAsync((ErrorOr<SetUpOrganizationResult>)SetUpOrganizationResult.SignIn);

        var response = await host.PostAsync(Path, Body, origin: AccountsApp, signedIn: true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ProblemCodeAsync(response)).Should().Be("Http.Unauthenticated");
        sent!.IdpSessionToken.Should().BeNull("only the single sign-on cookie identifies the user here");
    }

    [Fact]
    public async Task Post_WithTheCookie_ForwardsItAndAnswersTheOrganization()
    {
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);
        var organizationId = Guid.NewGuid();
        SetUpOrganizationCommand? sent = null;
        host.Sender
            .Setup(s => s.Send(It.IsAny<SetUpOrganizationCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<SetUpOrganizationResult>> command, CancellationToken _) =>
                sent = (SetUpOrganizationCommand)command)
            .ReturnsAsync((ErrorOr<SetUpOrganizationResult>)new SetUpOrganizationResult(false, organizationId));

        var response = await host.PostAsync(Path, Body, origin: AccountsApp, cookies: ["auth_idp=session-cookie"]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(response)).GetProperty("organizationId").GetGuid().Should().Be(organizationId);
        sent!.IdpSessionToken.Should().Be("session-cookie");
        sent.ClientId.Should().Be("EDIS");
        sent.Name.Should().Be("Expo House");
    }

    [Theory]
    [InlineData("&create_organization=", "")]
    [InlineData("&create_organization=true", "true")]
    [InlineData("", null)]
    public async Task Authorize_PassesCreateOrganizationAsSent_EmptyIncluded(string query, string? expected)
    {
        // Model binding reads "create_organization=" as null, which would make an
        // empty value mean "not asked" instead of the invalid_request it is.
        await using var host = await StartAsync([ConsoleApp, AccountsApp], cookieEnabled: false);
        Auth.Application.Features.Authentication.Authorize.AuthorizeCommand? sent = null;
        host.Sender
            .Setup(s => s.Send(
                It.IsAny<Auth.Application.Features.Authentication.Authorize.AuthorizeCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback((IRequest<ErrorOr<Auth.Application.Features.Authentication.Authorize.AuthorizeResult>> command, CancellationToken _) =>
                sent = (Auth.Application.Features.Authentication.Authorize.AuthorizeCommand)command)
            .ReturnsAsync((ErrorOr<Auth.Application.Features.Authentication.Authorize.AuthorizeResult>)
                new Auth.Application.Features.Authentication.Authorize.AuthorizeResult { RedirectUrl = "https://app.example.com/cb" });

        await host.Client.GetAsync($"/api/v1/auth/authorize?client_id=EDIS{query}");

        sent.Should().NotBeNull();
        sent!.CreateOrganization.Should().Be(expected);
    }

    [Fact]
    public async Task OrganizationProvisioned_WritesOneAuditRow_NamingTheSettingAsTheAuthority()
    {
        var repository = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        repository.Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);
        var handler = new OrganizationProvisionedForApplicationAuditEventHandler(
            repository.Object, new Mock<ILogger<OrganizationProvisionedForApplicationAuditEventHandler>>().Object);
        var notification = new OrganizationProvisionedForApplicationEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), OrganizationCreated: true, DateTime.UtcNow);

        await handler.Handle(notification, CancellationToken.None);

        repository.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
        written!.Action.Should().Be(AuditActions.OrganizationProvisionedForApplication);
        written.ActionType.Should().Be(AuditActionTypes.OrganizationManagement);
        written.UserId.Should().Be(notification.UserId);
        written.PerformedBy.Should().Be(notification.UserId);
        written.ApplicationId.Should().Be(notification.ApplicationId);
        written.EntityId.Should().Be(notification.OrganizationId);
        written.NewValues.Should().Contain(notification.CreatorRoleId.ToString());
        written.AdditionalData.Should().Contain("\"organizationCreated\":true")
            .And.Contain("application-creator-role-setting");
        AuditActions.ByCode[AuditActions.OrganizationProvisionedForApplication]
            .Should().Be(AuditActionTypes.OrganizationManagement);
    }

    [Fact]
    public void TheLimit_IsAnInteger_From0To100_DefaultOne_InTheOrganizationsSection()
    {
        var section = SystemSettingsRegistry.Sections.Single(s => s.Key == "Organizations");
        var field = section.Fields.Single(f => f.Path == "MaxSelfServiceOrganizationsPerUser");

        field.Kind.Should().Be(SettingKind.Int);
        field.Min.Should().Be(0);
        field.Max.Should().Be(100);
        field.DefaultValue.Should().Be(1);
        new OrganizationSettings().MaxSelfServiceOrganizationsPerUser.Should().Be(1);
    }
}
