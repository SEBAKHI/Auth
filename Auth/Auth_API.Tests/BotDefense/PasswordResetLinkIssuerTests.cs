using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RequestPasswordSetupLink;
using Auth.Application.Features.Users.SendUserPasswordReset;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 TS6 (D7): the three ways to ask for a reset link (the anonymous request, an
/// administrator's button, a holder setting a password) issue it through one service, which keeps
/// one live link per account; the two signed-in ways are audited with the account as subject and
/// whoever asked as performer.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: the administrator's command writing its own token row instead
/// of going through the issuer.
/// </remarks>
public class PasswordResetLinkIssuerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordResetLinkIssuer> _issuer = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly User _account = TestHelpers.CreateUser(email: "holder@example.com");
    private readonly Guid _admin = Guid.NewGuid();
    private readonly DateTime _expiresAt = DateTime.UtcNow.AddMinutes(30);

    public PasswordResetLinkIssuerTests()
    {
        _users.Setup(u => u.GetByIdAsync(_account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_account);
    }

    private void IssuerAnswers(bool issued) =>
        _issuer.Setup(i => i.IssueAsync(_account, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResetLinkIssue(issued, _expiresAt));

    [Fact]
    public async Task AnAdministrator_GoesThroughTheIssuer_AndTheLinkIsAuditedAsTheirs()
    {
        IssuerAnswers(issued: true);
        var handler = new SendUserPasswordResetCommandHandler(_users.Object, _issuer.Object, _publisher.Object, TimeProvider.System);

        var result = await handler.Handle(new SendUserPasswordResetCommand(_account.Id, _admin), CancellationToken.None);

        result.Value.Should().Be(new PasswordResetLinkResponse(_expiresAt, "h****r@example.com"));
        _issuer.Verify(i => i.IssueAsync(_account, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(
            p => p.Publish(
                It.Is<PasswordResetLinkIssuedEvent>(e => e.UserId == _account.Id && e.TriggeredBy == _admin && e.EventId != Guid.Empty),
                CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task AHolder_GoesThroughTheIssuer_AndTheLinkIsAuditedAsTheirOwn()
    {
        IssuerAnswers(issued: true);
        var handler = new RequestPasswordSetupLinkCommandHandler(_users.Object, _issuer.Object, _publisher.Object, TimeProvider.System);

        var result = await handler.Handle(new RequestPasswordSetupLinkCommand(_account.Id), CancellationToken.None);

        result.Value.ExpiresAt.Should().Be(_expiresAt);
        result.Value.MaskedEmail.Should().Be("h****r@example.com", "the panel shows where the link went");
        _publisher.Verify(
            p => p.Publish(It.Is<PasswordResetLinkIssuedEvent>(e => e.UserId == _account.Id && e.TriggeredBy == _account.Id), CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task WhileALinkIsLive_NothingIsIssuedOrAudited()
    {
        IssuerAnswers(issued: false);

        await new SendUserPasswordResetCommandHandler(_users.Object, _issuer.Object, _publisher.Object, TimeProvider.System)
            .Handle(new SendUserPasswordResetCommand(_account.Id, _admin), CancellationToken.None);

        _publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownAccount_IsNotFound()
    {
        var other = Guid.NewGuid();

        var result = await new SendUserPasswordResetCommandHandler(_users.Object, _issuer.Object, _publisher.Object, TimeProvider.System)
            .Handle(new SendUserPasswordResetCommand(other, _admin), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.NotFound(other).Code);
        _issuer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TheIssuer_KeepsOneLiveLinkPerAccount()
    {
        var tokens = new Mock<IPasswordResetTokenRepository>();
        var notifications = new Mock<INotificationService>();
        tokens.Setup(t => t.HasLiveTokenAsync(_account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var issuer = new PasswordResetLinkIssuer(
            tokens.Object,
            Mock.Of<ISecureTokenGenerator>(),
            new FakeCredentialHasher(),
            notifications.Object,
            TestHelpers.CreateOptions(new EmailSettings { ResetTokenExpirationMinutes = 30, FrontendBaseUrl = "https://accounts.example.com" }),
            new FakeEnvironment(production: false),
            NullLogger<PasswordResetLinkIssuer>.Instance);

        var issue = await issuer.IssueAsync(_account, CancellationToken.None);

        issue.Issued.Should().BeFalse();
        tokens.Verify(t => t.CreateAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        tokens.Verify(t => t.InvalidateAllForUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        notifications.Verify(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheAuditRow_NamesTheAccountAsSubject_AndTheCallerAsPerformer()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLog? written = null;
        audit.Setup(a => a.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);

        await new PasswordResetLinkIssuedAuditEventHandler(audit.Object, NullLogger<PasswordResetLinkIssuedAuditEventHandler>.Instance)
            .Handle(new PasswordResetLinkIssuedEvent(Guid.NewGuid(), _account.Id, _admin, DateTimeOffset.UtcNow), CancellationToken.None);

        written.Should().NotBeNull();
        written!.Action.Should().Be(AuditActions.PasswordResetLinkIssued);
        written.UserId.Should().Be(_account.Id);
        written.PerformedBy.Should().Be(_admin);
        AuditActions.ByCode[AuditActions.PasswordResetLinkIssued].Should().Be(AuditActionTypes.Security);
    }

    [Fact]
    public async Task AFailedAuditWrite_IsLogged_NeverThrown()
    {
        var audit = new Mock<IAuditLogRepository>();
        audit.Setup(a => a.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var handle = () => new PasswordResetLinkIssuedAuditEventHandler(audit.Object, NullLogger<PasswordResetLinkIssuedAuditEventHandler>.Instance)
            .Handle(new PasswordResetLinkIssuedEvent(Guid.NewGuid(), _account.Id, _admin, DateTimeOffset.UtcNow), CancellationToken.None);

        await handle.Should().NotThrowAsync("the link was already issued and mailed");
    }
}
