using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Authentication.SetupTwoFactor;

/// <summary>
/// Command to set up two-factor authentication (generate secret and QR code).
/// </summary>
/// <param name="UserId">The ID of the user setting up 2FA.</param>
public record SetupTwoFactorCommand(Guid UserId) : IRequest<ErrorOr<TwoFactorSetupResponse>>;

/// <summary>
/// Response containing 2FA setup information.
/// </summary>
public record TwoFactorSetupResponse(
    string Secret,
    string QrCodeUri,
    string ManualEntryKey);
