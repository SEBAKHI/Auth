using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// The pieces of an application's two-step requirement (S23) as the handlers meet them in
/// production: the real ticket service over a single-key keyring, and the real policy. For
/// a suite that is not about the requirement, its applications leave the flag off, so the
/// policy reads nothing and the handlers behave as before.
/// </summary>
public static class TestAppMfa
{
    /// <summary>The real ticket service, over a single-key keyring.</summary>
    public static AppMfaTicketService Tickets() => new(TestCredentialHashers.SingleKey());

    /// <summary>The real policy, over <paramref name="store"/>, or a store that is never read.</summary>
    public static ApplicationMfaPolicy Policy(ITwoFactorStateStore? store = null) =>
        new(store ?? new Mock<ITwoFactorStateStore>(MockBehavior.Strict).Object);

    /// <summary>The identity-provider settings as shipped: <c>Monitor</c>.</summary>
    public static TestHelpers.TestOptions<IdentityProviderSettings> Settings(
        ApplicationMfaEnforcementMode mode = ApplicationMfaEnforcementMode.Monitor) =>
        TestHelpers.CreateOptions(new IdentityProviderSettings { ApplicationMfaEnforcement = mode.ToString() });
}
