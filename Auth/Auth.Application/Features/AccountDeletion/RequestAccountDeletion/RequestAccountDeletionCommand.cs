using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RequestAccountDeletion;

/// <summary>
/// Command for the authenticated in-app deletion request. Re-authentication
/// is mandatory: password accounts confirm with their current password,
/// passwordless (external-only) accounts with an emailed OTP.
/// </summary>
/// <param name="UserId">The authenticated user requesting deletion.</param>
/// <param name="Password">Current password (password accounts).</param>
/// <param name="OtpCode">Deletion verification code (passwordless accounts).</param>
public record RequestAccountDeletionCommand(
    Guid UserId,
    string? Password,
    string? OtpCode) : IRequest<ErrorOr<AccountDeletionRequestedResult>>;

/// <summary>
/// Acknowledgment of a scheduled deletion.
/// </summary>
/// <param name="GraceEndsAtUtc">When the recovery window closes and destruction becomes eligible.</param>
public record AccountDeletionRequestedResult(DateTime GraceEndsAtUtc);
