using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// D-S20-4 (ii-s): an account that administers the platform and whose only usable second
/// factor is a passkey signs in with that passkey alone. Every other sign-in exit asks this
/// rule after its own proof and before its second step, and refuses such an account with
/// <see cref="AuthErrors.PasskeyRequired"/>.
/// </summary>
/// <remarks>
/// <para>
/// Asked by a signed-in person proving themself again with a password or an emailed code, by the password sign-in, the provider sign-in, the sign-in by emailed code, the three
/// recovery entries of an account waiting for deletion (in their shared recoverer, and once more
/// by the emailed-code entry before its password step), the request for the recovery's emailed
/// code, the sign-in that follows a verified address, and the second step itself. Not asked by
/// the passkey's own doors (its sign-in and its recovery: they are the passkey), by the refresh and the code
/// exchange (they keep the platform policy's withholding instead, D-S20-4 (ii)), nor by the
/// completion of a registration (a new account holds no passkey).
/// <c>PasskeyOnlyAdministratorDoorGuardTests</c> pins who asks, and where.
/// </para>
/// <para>
/// «Holds platform authority» is read from the platform claims (<see cref="EmailCodeFullProofRule"/>),
/// never from <c>TwoFactor:EnforceForPlatformAdmins</c>; they answer for an account waiting for
/// deletion too, whose role assignments the soft delete leaves in place. «Passkey-only» is the
/// usable factors (<see cref="UsableSecondFactors"/>), so the refusal pauses while passkeys do
/// not run (D-150-4): such an account then signs in as it did before passkeys.
/// </para>
/// <para>
/// A refusal opens nothing: no challenge, no email, no sign-in row, no session, and no lock is
/// lifted. A door that spends its own proof before its decision keeps it spent, so it cannot be
/// presented again: the code of a sign-in by emailed code, and the code of the second step (whose
/// ceremony row, opened by the first step, is left as it was). It writes one Information line
/// (<see cref="PasskeySignalCodes.Required"/>) naming the door.
/// </para>
/// <para>
/// One exception, by the door's own order: the provider sign-in links a provider identity whose
/// verified address matches an existing account, and stores the provider's refresh token, before
/// it judges the lock and asks this rule. A refused provider sign-in can therefore leave that new
/// link. It signs nothing in while passkeys run; while they do not, the provider opens the account
/// as such a link always has.
/// </para>
/// </remarks>
public sealed class PasskeyOnlyAdministratorRule
{
    private readonly UsableSecondFactors _usableFactors;
    private readonly EmailCodeFullProofRule _platformAuthority;
    private readonly ILogger<PasskeyOnlyAdministratorRule> _logger;

    public PasskeyOnlyAdministratorRule(
        UsableSecondFactors usableFactors,
        EmailCodeFullProofRule platformAuthority,
        ILogger<PasskeyOnlyAdministratorRule> logger)
    {
        _usableFactors = usableFactors;
        _platformAuthority = platformAuthority;
        _logger = logger;
    }

    /// <summary>
    /// Whether the account administers the platform and its only usable second factor is a
    /// passkey, for a reader that refuses nothing: the window that says what it will accept.
    /// </summary>
    public async Task<bool> AppliesToAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Off means off: while passkeys do not run nothing is read at all.
        if (!await _usableFactors.PasskeysCountAsync(cancellationToken))
        {
            return false;
        }

        // The factors first: one statement, and false for every account without a passkey,
        // so the claims are resolved only for the accounts the rule can concern.
        var factors = await _usableFactors.GetAsync(userId, passkeysCount: true, cancellationToken);
        return factors.IsPasskeyOnly
            && await _platformAuthority.HoldsPlatformAuthorityAsync(userId, cancellationToken);
    }

    /// <summary>
    /// The refusal for <paramref name="userId"/> at <paramref name="door"/>, or null when the
    /// door may go on.
    /// </summary>
    /// <param name="userId">The account the door's own proof established.</param>
    /// <param name="door">The door, for the log line: a fixed word, never input.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Error?> RefusalAsync(Guid userId, string door, CancellationToken cancellationToken)
    {
        if (!await AppliesToAsync(userId, cancellationToken))
        {
            return null;
        }

        _logger.LogInformation(
            "{EventCode}: the {Door} sign-in of user {UserId} was refused: the account administers the platform and signs in with its passkey alone",
            PasskeySignalCodes.Required, door, userId);
        return AuthErrors.PasskeyRequired;
    }
}

/// <summary>The doors <see cref="PasskeyOnlyAdministratorRule"/> names in its log line.</summary>
public static class PasskeyOnlyAdministratorDoors
{
    /// <summary>The password sign-in.</summary>
    public const string Password = "password";

    /// <summary>The sign-in through an external identity provider.</summary>
    public const string ExternalProvider = "external-provider";

    /// <summary>The sign-in by emailed code.</summary>
    public const string EmailCode = "email-code";

    /// <summary>The recovery of an account waiting for deletion (any of its three entries).</summary>
    public const string DeletionRecovery = "deletion-recovery";

    /// <summary>The sign-in that follows a verified address.</summary>
    public const string VerifyEmail = "verify-email";

    /// <summary>The second step of a sign-in.</summary>
    public const string SecondStep = "second-step";

    /// <summary>A signed-in person proving themself again with a password or an emailed code.</summary>
    public const string Reauthentication = "reauthentication";
}
