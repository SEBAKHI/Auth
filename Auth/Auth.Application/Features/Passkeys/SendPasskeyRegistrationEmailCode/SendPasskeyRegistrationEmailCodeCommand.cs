using Auth.Application.Features.Authentication.SendTwoFactorEmailCode;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.SendPasskeyRegistrationEmailCode;

/// <summary>
/// Emails the code an account enters before it binds a passkey as its FIRST qualifying
/// second factor (ARR-1049, T-A6): X02's proof of the mailbox, made reachable for a passkey,
/// because the authenticator app's sender mints a code only while an app is being set up.
/// </summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="CurrentSessionId">The session the access token belongs to.</param>
/// <param name="IpAddress">The requesting client address, for the email and the audit column.</param>
public record SendPasskeyRegistrationEmailCodeCommand(Guid UserId, Guid? CurrentSessionId, string? IpAddress)
    : IRequest<ErrorOr<TwoFactorEmailCodeResponse>>;
