using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The rule of an application that requires two-step verification (S23, AM2). The order
/// is the platform policy's (M7): a missing factor is decided before what the session
/// proved, so an account whose authenticator was removed after its sign-in is asked to set
/// one up, not let through on a proof it can no longer repeat.
/// </summary>
/// <remarks>
/// A passkey is not a factor yet (S20): <see cref="ApplicationMfaRequirement.Passkey"/> is
/// never produced, and the qualifying factor is the authenticator app alone
/// (<see cref="ITwoFactorStateStore.HasEnabledFactorAsync"/>).
/// </remarks>
public sealed class ApplicationMfaPolicy : IApplicationMfaPolicy
{
    private readonly ITwoFactorStateStore _twoFactorStateStore;

    public ApplicationMfaPolicy(ITwoFactorStateStore twoFactorStateStore)
    {
        _twoFactorStateStore = twoFactorStateStore;
    }

    /// <inheritdoc />
    public ApplicationMfaRequirement Evaluate(
        bool requireTwoFactor, AuthenticationMethods methods, bool hasQualifyingFactor)
    {
        if (!requireTwoFactor)
        {
            return ApplicationMfaRequirement.None;
        }

        if (!hasQualifyingFactor)
        {
            return ApplicationMfaRequirement.Enroll;
        }

        if (methods.IsMfaSatisfied)
        {
            return ApplicationMfaRequirement.None;
        }

        // The account has an authenticator app (the only qualifying factor before S20), so a
        // session that proved who the person is only lacks the second step.
        return methods.HasPrimary
            ? ApplicationMfaRequirement.StepUp
            : ApplicationMfaRequirement.Reauthenticate;
    }

    /// <inheritdoc />
    public async Task<ApplicationMfaRequirement> EvaluateAsync(
        bool requireTwoFactor, Guid userId, AuthenticationMethods methods, CancellationToken cancellationToken)
    {
        if (!requireTwoFactor)
        {
            return ApplicationMfaRequirement.None;
        }

        var hasQualifyingFactor = await _twoFactorStateStore.HasEnabledFactorAsync(userId, cancellationToken);
        return Evaluate(requireTwoFactor, methods, hasQualifyingFactor);
    }
}
