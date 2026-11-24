using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.GetTwoFactorStatus;

/// <summary>
/// Query for the signed-in user's own two-factor status: how many recovery codes are
/// left, and what the security page warns an administrator about. Never another
/// account's: each answer tells how close an account is to having no way back.
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
/// <param name="SignsInWithPasskeyOnly">
/// The account administers the platform and its only usable second factor is a
/// passkey, so every other way in refuses it (D-S20-4 (ii-s)). The page reads it with
/// the passkey list: one passkey kept on one device leaves the account one device
/// away from no sign-in at all (owner, 2026-11-24).
/// </param>
/// <param name="AnotherAdministratorCanReset">
/// For an account that holds platform authority: whether another administrator could
/// reset its second factor now, by the reset's own rules. Null for every other
/// account, which the warning does not concern (owner, 2026-11-24).
/// </param>
public record TwoFactorStatusResponse(
    int? RecoveryCodesRemaining,
    bool SignsInWithPasskeyOnly,
    bool? AnotherAdministratorCanReset);
