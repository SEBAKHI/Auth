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
/// The qualifying factors are FA2's (<see cref="ITwoFactorStateStore.GetQualifyingFactorsAsync"/>):
/// the authenticator app, and (S20) a live, user-verified passkey of the current RP ID. An
/// account whose only one is a passkey is <see cref="ApplicationMfaRequirement.Passkey"/>:
/// stepping up needs an app it does not have.
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
        bool requireTwoFactor, AuthenticationMethods methods, QualifyingSecondFactors factors)
    {
        if (!requireTwoFactor)
        {
            return ApplicationMfaRequirement.None;
        }

        if (!factors.HasAny)
        {
            return ApplicationMfaRequirement.Enroll;
        }

        if (methods.IsMfaSatisfied)
        {
            return ApplicationMfaRequirement.None;
        }

        // S20: a passkey sign-in is the only proof of two factors an account whose one
        // qualifying factor is a passkey can give.
        if (factors.IsPasskeyOnly)
        {
            return ApplicationMfaRequirement.Passkey;
        }

        // The account has an authenticator app, so a session that proved who the person is
        // only lacks the second step.
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

        var factors = await _twoFactorStateStore.GetQualifyingFactorsAsync(userId, cancellationToken);
        return Evaluate(requireTwoFactor, methods, factors);
    }
}
