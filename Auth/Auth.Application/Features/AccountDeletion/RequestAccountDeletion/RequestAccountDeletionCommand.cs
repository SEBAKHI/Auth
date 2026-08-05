using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.RequestAccountDeletion;

/// <summary>
/// Command for the authenticated in-app deletion request. Re-authentication is
/// mandatory and is always possession of the account's mailbox — the same
/// emailed verification code the public no-login wizard uses. The password is
/// deliberately not a factor here: external-only accounts have none, so a
/// password prompt is a dead end for them.
/// </summary>
/// <param name="UserId">The authenticated user requesting deletion.</param>
/// <param name="OtpCode">The emailed deletion verification code.</param>
public record RequestAccountDeletionCommand(
    Guid UserId,
    string OtpCode) : IRequest<ErrorOr<AccountDeletionRequestedResult>>;

/// <summary>
/// Acknowledgment of a scheduled deletion.
/// </summary>
/// <param name="GraceEndsAtUtc">When the recovery window closes and destruction becomes eligible.</param>
public record AccountDeletionRequestedResult(DateTime GraceEndsAtUtc);
