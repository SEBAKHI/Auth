using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// What a password reset, a password change and an administrator's lock do to the account's
/// passkeys, beyond the handlers' own tests: the list read for the notice, and the events.
/// </summary>
public class RecentPasskeyRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 14, 0, TimeSpan.Zero);

    /// <summary>
    /// REVIEW-FIXES 150 F14: while no RP ID is set no passkey is of it, so the notice's list is
    /// empty and the database is not asked — every deployment that never turns passkeys on resets
    /// and changes passwords without the read.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the early answer removed.</remarks>
    [Fact]
    public async Task StillLive_WithNoRpIdSet_IsEmpty_AndRunsNoQuery()
    {
        var repository = PasskeyTestDoubles.EmptyRepository();
        var revocation = new RecentPasskeyRevocation(
            PasskeyTestDoubles.Feature(enabled: false, rpId: string.Empty),
            repository.Object,
            new Mock<IPublisher>().Object,
            new FixedTimeProvider(Now),
            NullLogger<RecentPasskeyRevocation>.Instance);

        var live = await revocation.StillLiveAsync(Guid.NewGuid(), CancellationToken.None);

        live.Should().BeEmpty();
        repository.Verify(r => r.GetLiveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// REVIEW-FIXES 150 F18 (i): the revocation never throws. A passkey is revoked whatever its
    /// audit row and notice do, so an event that fails is logged at Error and the others still go.
    /// </summary>
    /// <remarks>Deliberate break, verified red: the <c>try</c> around each publish removed.</remarks>
    [Fact]
    public async Task RevokeRecent_AnEventThatFails_IsLogged_AndTheOthersStillGo()
    {
        var user = TestHelpers.CreateUser(email: "someone@example.com");
        var repository = new Mock<IUserPasskeyRepository>();
        var first = PasskeyTestDoubles.Live(user.Id, Now.UtcDateTime.AddHours(-2), "First");
        var second = PasskeyTestDoubles.Live(user.Id, Now.UtcDateTime.AddHours(-1), "Second");
        PasskeyTestDoubles.AsTable(repository, revocationFails: false, first, second);
        var publisher = new Mock<IPublisher>();
        var published = new List<Guid>();
        publisher
            .Setup(p => p.Publish(It.IsAny<PasskeyRemovedEvent>(), It.IsAny<CancellationToken>()))
            .Returns((PasskeyRemovedEvent removed, CancellationToken _) =>
            {
                published.Add(removed.PasskeyId);
                return removed.PasskeyId == first.Id
                    ? Task.FromException(new InvalidOperationException("the audit store is unreachable"))
                    : Task.CompletedTask;
            });
        var log = new CapturingLogger<RecentPasskeyRevocation>();
        var revocation = new RecentPasskeyRevocation(
            PasskeyTestDoubles.Feature(), repository.Object, publisher.Object, new FixedTimeProvider(Now), log);

        var revoked = await revocation.RevokeRecentAsync(
            user, Now.UtcDateTime.AddHours(-72), PasskeyRemovalReasons.PasswordChange, user.Id);

        revoked.Should().Be(2, "both passkeys are revoked, whatever their events do");
        published.Should().BeEquivalentTo([first.Id, second.Id], "the second event still goes after the first failed");
        log.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error)
            .Which.Message.Should().Contain(first.Id.ToString());
    }
}
