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
    /// The factor the code is presented as. Valid only once the validator accepted
    /// <see cref="Method"/>.
    /// </summary>
    public SecondFactorMethod PresentedMethod =>
        Method is not null
            ? TwoFactorMethodNames.MethodOf(Method)
            : UseRecoveryCode ? SecondFactorMethod.RecoveryCode : SecondFactorMethod.Totp;
}
