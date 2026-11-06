using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B, D-S20-4 (ii-s): the refusal every other door asks. An account that holds a platform
/// permission and whose only usable second factor is a passkey is refused with
/// <c>Auth.PasskeyRequired</c> (403), named in one Information line; every other account, and
/// every account while passkeys do not run, goes on (D-150-4: off means off, and nothing is read).
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: «passkey-only» read from the inventory instead of the
/// usable factors (refuses while passkeys are off); «administrator» read from
/// <c>TwoFactor:EnforceForPlatformAdmins</c> instead of the claims; the claims resolved before the
/// factors (resolved for every account).
/// </remarks>
public class PasskeyOnlyAdministratorRuleTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<ITwoFactorStateStore> _store = new(MockBehavior.Strict);
    private readonly Mock<ITokenClaimsResolver> _claims = new(MockBehavior.Strict);
    private readonly CapturingLogger<PasskeyOnlyAdministratorRule> _log = new();

    [Fact]
    public async Task RefusalAsync_PasskeyOnlyAdministrator_RefusesAndNamesTheDoor()
    {
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: true));
        GivenPlatformPermissions("users.read");

        var refusal = await Rule().RefusalAsync(UserId, PasskeyOnlyAdministratorDoors.Password, CancellationToken.None);

        refusal.Should().NotBeNull();
        refusal!.Value.Code.Should().Be(AuthErrors.PasskeyRequired.Code);
        refusal.Value.Type.Should().Be(ErrorType.Forbidden);
        _log.Entries.Should().ContainSingle().Which.Should().Match<CapturedLogEntry>(entry =>
            entry.Level == LogLevel.Information
            && entry.Values.Contains(PasskeySignalCodes.Required)
            && entry.Values.Contains(PasskeyOnlyAdministratorDoors.Password)
            && entry.Values.Contains(UserId.ToString()));
    }

    public static TheoryData<string, bool, bool, string[]> AccountsThatGoOn => new()
    {
        // Because, app, passkey, platform permissions.
        { "an administrator with the app as well", true, true, ["users.read"] },
        { "an administrator with the app alone", true, false, ["users.read"] },
        { "an administrator with no factor", false, false, ["users.read"] },
        { "a passkey-only account that administers nothing on the platform", false, true, [] },
    };

    [Theory]
    [MemberData(nameof(AccountsThatGoOn))]
    public async Task RefusalAsync_AnyOtherAccount_GoesOnAndWritesNothing(string because, bool app, bool passkey, string[] permissions)
    {
        GivenFactors(new QualifyingSecondFactors(app, passkey));
        GivenPlatformPermissions(permissions);

        (await Rule().RefusalAsync(UserId, PasskeyOnlyAdministratorDoors.EmailCode, CancellationToken.None))
            .Should().BeNull(because);
        _log.Entries.Should().BeEmpty(because);
    }

    [Fact]
    public async Task RefusalAsync_AccountWithoutAPasskey_NeverResolvesItsClaims()
    {
        GivenFactors(new QualifyingSecondFactors(Totp: true, Passkey: false));

        (await Rule().RefusalAsync(UserId, PasskeyOnlyAdministratorDoors.SecondStep, CancellationToken.None)).Should().BeNull();
        _claims.Verify(c => c.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("switched off", false, true)]
    [InlineData("switched on, but the passkey table is missing", true, false)]
    public async Task RefusalAsync_PasskeysDoNotRun_ReadsNothingAndRefusesNoOne(string because, bool enabled, bool schemaPresent)
    {
        // Strict collaborators with no setup: any read would throw.
        var rule = Rule(PasskeyTestDoubles.Feature(enabled: enabled, schemaPresent: schemaPresent));

        (await rule.RefusalAsync(UserId, PasskeyOnlyAdministratorDoors.Password, CancellationToken.None)).Should().BeNull(because);
        _store.VerifyNoOtherCalls();
        _claims.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefusalAsync_PassesTheCallersToken_ToEveryRead()
    {
        using var source = new CancellationTokenSource();
        GivenFactors(new QualifyingSecondFactors(Totp: false, Passkey: true));
        GivenPlatformPermissions("users.read");

        await Rule().RefusalAsync(UserId, PasskeyOnlyAdministratorDoors.VerifyEmail, source.Token);

        _store.Verify(s => s.GetQualifyingFactorsAsync(UserId, source.Token), Times.Once);
        _claims.Verify(c => c.ResolveAsync(UserId, null, source.Token), Times.Once);
    }

    private PasskeyOnlyAdministratorRule Rule(PasskeyFeature? feature = null) =>
        new(new UsableSecondFactors(_store.Object, feature ?? PasskeyTestDoubles.Feature()),
            new EmailCodeFullProofRule(_claims.Object),
            _log);

    private void GivenFactors(QualifyingSecondFactors factors) =>
        _store.Setup(s => s.GetQualifyingFactorsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(factors);

    private void GivenPlatformPermissions(params string[] permissions) =>
        _claims.Setup(c => c.ResolveAsync(UserId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], permissions, []));
}
