using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.RemovePasskey;
using Auth.Application.Features.Passkeys.RenamePasskey;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK12): removing a passkey. A recent sign-in that proved a first factor removes one
/// with no passkey proof — two factors when the account has the authenticator app. Only the
/// caller's own live passkey of the RP ID in force is found; removing it ends the other
/// sessions and keeps this one; and a platform administrator the policy binds keeps a
/// qualifying factor.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the owner check dropped (the repository asked by
/// passkey id alone — <c>PasskeyStoreSqlTextTests.GetLiveById_ReadsOnlyTheCallersLiveRowOfTheRpId</c>
/// pins the statement's <c>UserId</c> condition); a passkey assertion demanded for removal (a
/// password session refused); the revocation of the other sessions dropped. REVIEW 150: the
/// <c>try</c> around the session revocation dropped (F3); the floor's statement replaced by the
/// plain revocation for a bound administrator (F4).
/// </remarks>
public class RemovePasskeyCommandHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private const string IdpToken = "idp-session-token";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IPlatformMfaPolicy> _policy = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IUserPasskeyRepository> _passkeys = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly UserPasskey _passkey;

    public RemovePasskeyCommandHandlerTests()
    {
        _passkey = PasskeyTestDoubles.Live(_userId, DateTime.UtcNow.AddDays(-30), "Laptop");
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId, email: "owner@example.com"));
        GivenSession(AuthenticationMethods.Password);
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: true));
        _passkeys.Setup(r => r.GetLiveByIdAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_passkey);
        _passkeys.Setup(r => r.CountLiveAsync(_userId, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _passkeys.Setup(r => r.TryRevokeAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Revoked(_passkey));
        _passkeys.Setup(r => r.TryRevokeUnlessLastAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Revoked(_passkey));
        _revocation.Setup(r => r.RevokeCredentialsAsync(
                _userId, SessionId, IdpToken, _userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    [Fact]
    public async Task Handle_ARegularUsersPasswordSession_RemovesThePasskey_WithNoPasskeyProof()
    {
        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _passkeys.Verify(r => r.TryRevokeAsync(
            _userId, _passkey.Id, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, _userId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_EndsTheOtherSessions_AndKeepsThisOne()
    {
        await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        _revocation.Verify(r => r.RevokeCredentialsAsync(
            _userId, SessionId, IdpToken, _userId, It.IsAny<string>(), CancellationToken.None), Times.Once,
            "the current session and its SSO cookie are the ones kept (project_credential_revocation_rule)");
    }

    [Fact]
    public async Task Handle_PublishesTheRemoval_ForTheAuditRowAndTheNotice()
    {
        await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        _publisher.Verify(p => p.Publish(
            It.Is<PasskeyRemovedEvent>(e => e.UserId == _userId && e.PasskeyId == _passkey.Id && e.Name == "Laptop"
                && e.Reason == PasskeyRemovalReasons.User && e.RemovedBy == _userId && e.Email == "owner@example.com"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_AnotherUsersPasskeyId_IsNotFound_WithNoRevocationAndNoEvent()
    {
        // The repository looks up the caller's own passkey: another account's id finds nothing.
        var strangers = Guid.NewGuid();

        var result = await Handler().Handle(Command(strangers), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.NotFound.Code);
        _passkeys.Verify(r => r.GetLiveByIdAsync(_userId, strangers, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>()), Times.Once,
            "the lookup is keyed on the caller");
        AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_WithTheApp_FromAPasswordSession_IsAskedForTwoFactors()
    {
        GivenFactors(new QualifyingSecondFactors(Totp: true, Passkey: true));

        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_WithTheApp_FromATwoFactorSession_Removes()
    {
        GivenFactors(new QualifyingSecondFactors(Totp: true, Passkey: true));
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));

        (await Handler().Handle(Command(_passkey.Id), CancellationToken.None)).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_APasswordAccountsEmailedCodeSession_IsAskedToSignInAgain()
    {
        GivenSession(AuthenticationMethods.EmailCode);

        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        AssertNothingChanged();
    }

    [Fact]
    public async Task Handle_TheLastFactorOfABoundAdministrator_IsRefused_BeforeAnythingChanges()
    {
        _passkeys.Setup(r => r.CountLiveAsync(_userId, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _policy.Setup(p => p.IsEnforcedForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredByPolicy.Code);
        AssertNothingChanged();
    }

    [Theory]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    public async Task Handle_TheLastPasskey_IsRemovable_WhenThePolicyDoesNotBindOrAnotherFactorStays(int live, bool enforced, bool expectedRemoved)
    {
        _passkeys.Setup(r => r.CountLiveAsync(_userId, PasskeyTestDoubles.RpId, It.IsAny<CancellationToken>())).ReturnsAsync(live);
        _policy.Setup(p => p.IsEnforcedForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(enforced);

        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.IsError.Should().Be(!expectedRemoved);
    }

    /// <summary>
    /// REVIEW 150 F3: the passkey is revoked; ending the other sessions then fails. The removal
    /// stands — its audit row and its notice follow, and the failure is logged once — because a
    /// retry would find the passkey gone and could never end those sessions.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTheOtherSessionsCannotBeEnded_TheRemovalStands_WithItsAuditRowAndNotice()
    {
        _revocation.Setup(r => r.RevokeCredentialsAsync(
                _userId, SessionId, IdpToken, _userId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Execution Timeout Expired."));
        var log = new CapturingLogger<RemovePasskeyCommandHandler>();

        var result = await Handler(log).Handle(Command(_passkey.Id), CancellationToken.None);

        result.IsError.Should().BeFalse("the passkey is already revoked");
        _publisher.Verify(p => p.Publish(It.Is<PasskeyRemovedEvent>(e => e.PasskeyId == _passkey.Id), CancellationToken.None), Times.Once,
            "the audit row and the owner's notice follow the removal");
        log.Entries.Should().ContainSingle(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error)
            .Which.Exception.Should().BeOfType<InvalidOperationException>();
    }

    /// <summary>
    /// REVIEW 150 F4: for an administrator the policy binds, with no app, the removal is made by
    /// the statement that keeps one live passkey, so two removals at once cannot take the last two.
    /// </summary>
    [Fact]
    public async Task Handle_ABoundAdministratorsRemoval_IsMadeByTheStatementThatKeepsTheFloor()
    {
        _policy.Setup(p => p.IsEnforcedForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await Handler().Handle(Command(_passkey.Id), CancellationToken.None)).IsError.Should().BeFalse();

        _passkeys.Verify(r => r.TryRevokeUnlessLastAsync(
            _userId, _passkey.Id, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, _userId, CancellationToken.None), Times.Once);
        _passkeys.Verify(r => r.TryRevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// REVIEW 150 F4: two removals raced for the last two passkeys; the count read two, but the
    /// statement found the other gone and changed nothing. The passkey is still live, so the
    /// answer is the policy's, with nothing ended and no event.
    /// </summary>
    [Fact]
    public async Task Handle_TheFloorKeptTheLastPasskey_IsRequiredByPolicy_WithNothingChanged()
    {
        _policy.Setup(p => p.IsEnforcedForUserAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _passkeys.Setup(r => r.TryRevokeUnlessLastAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPasskey?)null);

        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.RequiredByPolicy.Code);
        _revocation.Verify(r => r.RevokeCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRemovedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ARevocationThatLosesARace_IsNotFound_WithNoSessionEnded()
    {
        _passkeys.Setup(r => r.TryRevokeAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, PasskeyRemovalReasons.User, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserPasskey?)null);

        var result = await Handler().Handle(Command(_passkey.Id), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.NotFound.Code);
        _revocation.Verify(r => r.RevokeCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRemovedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Rename ──

    [Fact]
    public async Task Rename_TheCallersOwnPasskey_StoresTheNormalizedName()
    {
        _passkeys.Setup(r => r.TryRenameAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, "Work laptop", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Rename().Handle(new RenamePasskeyCommand(_userId, _passkey.Id, "  Work laptop  "), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _passkeys.Verify(r => r.TryRenameAsync(_userId, _passkey.Id, PasskeyTestDoubles.RpId, "Work laptop", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rename_AnotherUsersPasskeyId_IsNotFound_AndWritesNothing()
    {
        var result = await Rename().Handle(new RenamePasskeyCommand(_userId, Guid.NewGuid(), "Mine now"), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.NotFound.Code);
        _passkeys.Verify(r => r.TryRenameAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rename_ToSixtyFiveCodePoints_IsRefused()
    {
        var result = await Rename().Handle(
            new RenamePasskeyCommand(_userId, _passkey.Id, string.Concat(Enumerable.Repeat("😀", 65))), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.NameTooLong.Code);
        new RenamePasskeyCommandValidator()
            .Validate(new RenamePasskeyCommand(_userId, _passkey.Id, string.Concat(Enumerable.Repeat("😀", 65))))
            .Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(PasskeyErrors.NameTooLong.Code);
    }

    // ── Fixture ──

    private void GivenSession(AuthenticationMethods methods) =>
        _guard.Setup(g => g.EnsureRecentSignInAsync(_userId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, "Firefox on Linux", methods));

    private void GivenFactors(QualifyingSecondFactors factors) =>
        _store.Setup(s => s.GetQualifyingFactorsAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(factors);

    private void AssertNothingChanged()
    {
        _passkeys.Verify(r => r.TryRevokeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _passkeys.Verify(r => r.TryRevokeUnlessLastAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _revocation.Verify(r => r.RevokeCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<PasskeyRemovedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UserPasskey Revoked(UserPasskey live) =>
        new(live.Id, live.UserId, live.CredentialId, live.PublicKey, live.SignCount, live.RpId, live.IsUserVerified,
            live.IsBackupEligible, live.IsBackedUp, live.Name, live.CreatedAt, live.LastUsedAt, live.LastCounterRegressionAt,
            revokedAt: DateTime.UtcNow, revokedReason: PasskeyRemovalReasons.User, revokedBy: live.UserId);

    private RemovePasskeyCommand Command(Guid passkeyId) => new(_userId, passkeyId, SessionId, IdpToken);

    private RemovePasskeyCommandHandler Handler(Microsoft.Extensions.Logging.ILogger<RemovePasskeyCommandHandler>? log = null) =>
        new(PasskeyTestDoubles.Feature(), _guard.Object, _store.Object, _policy.Object, _users.Object, _passkeys.Object,
            _revocation.Object, _publisher.Object, TimeProvider.System, log ?? NullLogger<RemovePasskeyCommandHandler>.Instance);

    private RenamePasskeyCommandHandler Rename() => new(PasskeyTestDoubles.Feature(), _passkeys.Object);
}
