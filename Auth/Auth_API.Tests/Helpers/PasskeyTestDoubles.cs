using Auth.Application.Configuration;
using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The passkey collaborators a handler test builds (S20): a feature switched on or off over a
/// fixed RP ID and origins, and the 72-hour revocation over a repository the test controls.
/// </summary>
internal static class PasskeyTestDoubles
{
    public const string RpId = "example.com";
    public const string ConsoleOrigin = "https://console.example.com";
    public const string AccountsOrigin = "https://accounts.example.com";

    public static PasskeyRpIdAtStartup RpIdAtStartup { get; } = new(RpId);

    /// <summary>A schema probe that answers <paramref name="present"/>.</summary>
    public static IPasskeySchema Schema(bool present = true)
    {
        var schema = new Mock<IPasskeySchema>();
        schema.Setup(s => s.IsPresentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(present);
        return schema.Object;
    }

    /// <summary>
    /// The feature over the given settings: on, the example RP ID and both example origins by
    /// default, with no live passkey of another RP ID unless <paramref name="otherRpIdLive"/>.
    /// </summary>
    public static PasskeyFeature Feature(
        bool enabled = true,
        string rpId = RpId,
        string[]? origins = null,
        bool schemaPresent = true,
        bool otherRpIdLive = false) =>
        new(
            Monitor(new PasskeySettings { Enabled = enabled, RpId = rpId }),
            Monitor(new IdentityProviderSettings { FirstPartySpaOrigins = origins ?? [ConsoleOrigin, AccountsOrigin] }),
            new PasskeyRpIdAtStartup(rpId),
            Schema(schemaPresent),
            OtherRpIdRows(otherRpIdLive).Object);

    /// <summary>A repository that answers whether live passkeys of another RP ID exist.</summary>
    public static Mock<IUserPasskeyRepository> OtherRpIdRows(bool live)
    {
        var repository = new Mock<IUserPasskeyRepository>();
        repository.Setup(r => r.HasLiveOfAnotherRpIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);
        return repository;
    }

    /// <summary>A repository with no passkey at all.</summary>
    public static Mock<IUserPasskeyRepository> EmptyRepository()
    {
        var repository = new Mock<IUserPasskeyRepository>();
        repository.Setup(r => r.GetLiveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        repository.Setup(r => r.RevokeRegisteredAfterAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return repository;
    }

    /// <summary>The 72-hour revocation over <paramref name="repository"/> (none by default).</summary>
    public static RecentPasskeyRevocation Revocation(
        Mock<IUserPasskeyRepository>? repository = null,
        IPublisher? publisher = null,
        TimeProvider? clock = null) =>
        new(
            Feature(),
            (repository ?? EmptyRepository()).Object,
            publisher ?? new Mock<IPublisher>().Object,
            clock ?? TimeProvider.System,
            NullLogger<RecentPasskeyRevocation>.Instance);

    /// <summary>A live passkey of the example RP ID, registered at <paramref name="createdAt"/>.</summary>
    public static UserPasskey Live(Guid userId, DateTime createdAt, string name = "Laptop", string rpId = RpId) =>
        new(
            Guid.NewGuid(), userId, [1, 2, 3, (byte)createdAt.Minute], [9, 9], 0, rpId,
            isUserVerified: true, isBackupEligible: false, isBackedUp: false, name, createdAt,
            lastUsedAt: null, lastCounterRegressionAt: null, revokedAt: null, revokedReason: null, revokedBy: null);

    private static IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = new Mock<IOptionsMonitor<T>>();
        monitor.Setup(m => m.CurrentValue).Returns(value);
        return monitor.Object;
    }
}
