using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The IdP session reader as the handlers meet it in production (OI-154): the real
/// rule (<see cref="IdpSession.IsValidAt"/>) over a repository mock, so a handler
/// test that hands it a session sees the same validity the API applies.
/// </summary>
internal static class TestIdpSessions
{
    /// <summary>
    /// The real reader over <paramref name="repository"/>. Every use is recorded,
    /// as the database does for a live session; a test about the write overrides
    /// the setup after this call.
    /// </summary>
    public static IdpSessionReader Reader(
        Mock<IIdpSessionRepository> repository,
        ICredentialHasher keys,
        IdentityProviderSettings? settings = null,
        TimeProvider? clock = null)
    {
        repository
            .Setup(r => r.RecordActivityAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return new IdpSessionReader(
            repository.Object,
            keys,
            TestHelpers.CreateOptions(settings ?? new IdentityProviderSettings()),
            clock ?? TimeProvider.System,
            NullLogger<IdpSessionReader>.Instance);
    }

    /// <summary>
    /// A browser session (no "Stay signed in") signed in just now, valid for
    /// <paramref name="lifetime"/>.
    /// </summary>
    public static IdpSession SignedInNow(Guid userId, string tokenHash, TimeSpan lifetime) =>
        IdpSession.Create(
            Guid.NewGuid(), userId, tokenHash, persistent: false, lifetime, DateTime.UtcNow, null, null);
}
