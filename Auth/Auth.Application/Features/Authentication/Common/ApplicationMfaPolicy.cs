using Auth.Application.Features.Passkeys.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The rule of an application that requires two-step verification (S23, AM2). The order
/// is the platform policy's (M7): a missing factor is decided before what the session
/// proved, so an account whose authenticator was removed after its sign-in is asked to set
/// one up, not let through on a proof it can no longer repeat.
/// </summary>
/// <remarks>
/// The qualifying factors are the usable ones (<see cref="UsableSecondFactors"/>): the
/// authenticator app, and (S20) a live, user-verified passkey of the current RP ID while
/// passkeys run (D-150-4). An account whose only one is a passkey is
/// <see cref="ApplicationMfaRequirement.Passkey"/>: stepping up needs an app it does not have.
/// While passkeys do not run such an account has no usable factor and is asked to set one up,
/// as it was before passkeys.
/// </remarks>
public sealed class ApplicationMfaPolicy : IApplicationMfaPolicy
{
    private readonly UsableSecondFactors _usableFactors;

    public ApplicationMfaPolicy(UsableSecondFactors usableFactors)
    {
        _usableFactors = usableFactors;
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

        var factors = await _usableFactors.GetAsync(userId, cancellationToken);
        return Evaluate(requireTwoFactor, methods, factors);
    }
}
