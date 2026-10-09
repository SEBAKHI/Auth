using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetTwoFactorStatus;

/// <summary>
/// Query for the signed-in user's own two-factor status — today, how many recovery
/// codes are left. Never another account's: the number tells how close an account
/// is to having no way back but an administrator.
/// </summary>
/// <param name="UserId">The signed-in user.</param>
public record GetTwoFactorStatusQuery(Guid UserId) : IRequest<ErrorOr<TwoFactorStatusResponse>>;

/// <summary>
/// The signed-in user's two-factor status.
/// </summary>
/// <param name="RecoveryCodesRemaining">
/// How many unused recovery codes the enabled factor holds; null when the account
/// has no enabled factor. Whether the factor is on is read from the profile, as
/// before: this is not a second source for it.
/// </param>
public record TwoFactorStatusResponse(int? RecoveryCodesRemaining);
