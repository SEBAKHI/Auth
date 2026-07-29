using ErrorOr;
using MediatR;

namespace Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;

/// <summary>
/// Command for step 2 of the public no-login deletion flow: confirm email
/// possession with the verification code and schedule the deletion.
/// </summary>
/// <param name="Email">The email address of the account to delete.</param>
/// <param name="OtpCode">The 6-digit verification code.</param>
public record ConfirmPublicDeletionCommand(
    string Email,
    string OtpCode) : IRequest<ErrorOr<Success>>;
