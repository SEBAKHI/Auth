using System.Net;
using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.ForgotPassword;
using Auth.Application.Features.Authentication.Login;
using Auth.Application.Features.Authentication.StartEmailCodeSignIn;
using Auth.Application.Features.Authentication.StartRegistration;
using Auth_API.Modules.Authentication.Contracts;
using Auth_API.Modules.Authentication.Controllers;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 (D1, review of PR 167): each of the four protected endpoints hands the posted
/// <c>botToken</c> and the caller's address to its command. Every other test builds the command
/// itself, so without these a dropped hand-off would leave CI green while every real request was
/// TokenMissing under Enforce. Deliberate break, verified red: <c>request.BotToken</c> dropped from
/// the code start's command.
/// </summary>
public class BotDefenseControllerHandOffTests
{
    private const string Token = "a-widget-token";
    private const string Address = "203.0.113.7";

    private sealed class Sent : Exception;

    private readonly Mock<ISender> _sender = new();

    private AuthController Controller()
    {
        // The command is all these tests read: the sender stops there.
        _sender.Setup(s => s.Send(It.IsAny<IRequest<It.IsAnyType>>(), It.IsAny<CancellationToken>())).Throws<Sent>();
        var settings = new Mock<IOptionsSnapshot<IdentityProviderSettings>>();
        settings.Setup(s => s.Value).Returns(new IdentityProviderSettings());
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(Address);
        return new AuthController(_sender.Object, settings.Object, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private IBotProtectedRequest Handed() =>
        _sender.Invocations.Single().Arguments[0].Should().BeAssignableTo<IBotProtectedRequest>().Subject;

    [Fact]
    public async Task Login()
    {
        var act = () => Controller().Login(new LoginRequest { Email = "a@example.com", Password = "p", BotToken = Token }, CancellationToken.None);

        await act.Should().ThrowAsync<Sent>();
        Handed().Should().BeOfType<LoginCommand>();
        Handed().BotToken.Should().Be(Token);
        Handed().ClientIp.Should().Be(Address);
    }

    [Fact]
    public async Task RegistrationStart()
    {
        var act = () => Controller().StartRegistration(new StartRegistrationRequest { Email = "a@example.com", BotToken = Token }, CancellationToken.None);

        await act.Should().ThrowAsync<Sent>();
        Handed().Should().BeOfType<StartRegistrationCommand>();
        Handed().BotToken.Should().Be(Token);
        Handed().ClientIp.Should().Be(Address);
    }

    [Fact]
    public async Task CodeStart()
    {
        var act = () => Controller().StartEmailCodeSignIn(new StartEmailCodeSignInRequest { Email = "a@example.com", BotToken = Token }, CancellationToken.None);

        await act.Should().ThrowAsync<Sent>();
        Handed().Should().BeOfType<StartEmailCodeSignInCommand>();
        Handed().BotToken.Should().Be(Token);
        Handed().ClientIp.Should().Be(Address);
    }

    [Fact]
    public async Task ResetRequest()
    {
        var act = () => Controller().ForgotPassword(new ForgotPasswordRequest { Email = "a@example.com", BotToken = Token }, CancellationToken.None);

        await act.Should().ThrowAsync<Sent>();
        Handed().Should().BeOfType<ForgotPasswordCommand>();
        Handed().BotToken.Should().Be(Token);
        Handed().ClientIp.Should().Be(Address);
    }
}
