using Auth.Application.DTOs;
using Auth.Domain.Constants;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.VerifyTwoFactorLogin;

/// <summary>
/// Command to complete a two-factor login by verifying a TOTP code or a
/// recovery code against a pending login challenge.
/// </summary>
/// <param name="StaySignedIn">
/// The person ticked "Stay signed in" on the sign-in page that opened the
/// challenge (OI-154); the app carries it here in memory. Absent, after a reload
/// of the second-factor page for instance, means a browser session.
/// </param>
/// <param name="Method">
/// The factor the code is presented as: one of <see cref="TwoFactorMethodNames"/>
/// (S24, EO7). Absent, as from a client built before the emailed code, means what it
/// always meant: <paramref name="UseRecoveryCode"/> decides.
/// </param>
public record VerifyTwoFactorLoginCommand(
    string ChallengeToken,
    string Code,
    bool UseRecoveryCode,
    string? IpAddress,
    string? UserAgent,
    string? DeviceId = null,
    bool StaySignedIn = false,
    string? Method = null) : IRequest<ErrorOr<LoginResponse>>
{
    /// <summary>
    /// The factor the code is presented as, or null for a <see cref="Method"/> that
    /// names none (the validator answers <c>TwoFactor.MethodInvalid</c> for it). Total:
    /// it never throws, so neither does anything that reads it — the record's own
    /// <see cref="ToString"/> included (OI-220 (9)).
    /// </summary>
    public SecondFactorMethod? PresentedMethod =>
        Method is not null
            ? TwoFactorMethodNames.TryMethodOf(Method)
            : UseRecoveryCode ? SecondFactorMethod.RecoveryCode : SecondFactorMethod.Totp;

    // The code and the challenge token are secrets; the synthesized ToString would
    // print them into any log line or assertion message the command reaches.
    public override string ToString() =>
        $"VerifyTwoFactorLoginCommand {{ Method = {Method}, PresentedMethod = {PresentedMethod}, StaySignedIn = {StaySignedIn} }}";
}
