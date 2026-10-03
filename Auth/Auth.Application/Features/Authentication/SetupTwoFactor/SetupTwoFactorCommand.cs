using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.SetupTwoFactor;

/// <summary>
/// Command to set up two-factor authentication (generate secret and QR code).
/// </summary>
/// <param name="UserId">The ID of the user setting up 2FA.</param>
/// <param name="CurrentSessionId">
/// The session the access token belongs to (its <c>sid</c>), measured by the
/// re-authentication check.
/// </param>
public record SetupTwoFactorCommand(Guid UserId, Guid? CurrentSessionId) : IRequest<ErrorOr<TwoFactorSetupResponse>>;

/// <summary>
/// Response containing 2FA setup information.
/// </summary>
/// <param name="Secret">The raw TOTP secret.</param>
/// <param name="QrCodeUri">The <c>otpauth://</c> URI to render as a QR code.</param>
/// <param name="ManualEntryKey">The secret in four-character groups, for typing by hand.</param>
/// <param name="EmailCodeRequired">
/// Whether enable will also need the code emailed to the account's confirmed
/// address (sent by <c>POST /auth/2fa/email-code</c>), because this is the account's
/// first second factor and email is on. A client that does not find the member
/// treats it as false.
/// </param>
public record TwoFactorSetupResponse(
    string Secret,
    string QrCodeUri,
    string ManualEntryKey,
    bool EmailCodeRequired);
