using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Users.ResetUserTwoFactor;

/// <summary>
/// Command for an administrator to remove another account's second factor — the
/// way back for an owner who lost both the authenticator and the recovery codes.
/// The account's sessions and tokens are revoked with it, and its next sign-in
/// sets a factor up again.
/// </summary>
/// <param name="UserId">The account whose factor is removed.</param>
/// <param name="ResetBy">The administrator making the change.</param>
/// <param name="ActorSessionId">
/// The administrator's own session (the token's <c>sid</c>): it must be recent and
/// have proved two factors, whatever the enforcement switch says.
/// </param>
public record ResetUserTwoFactorCommand(Guid UserId, Guid ResetBy, Guid? ActorSessionId) : IRequest<ErrorOr<Success>>;
