using Auth.Application.Features.Passkeys.Common;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.GetMyPasskeys;

/// <summary>
/// The signed-in account's live passkeys of the current RP ID, oldest first (S20, PK3): one
/// bound to another relying party is not listed, since it signs in nowhere. With them, the RP ID
/// and the account's user handle, which the security page passes to the browser (D-S20-7).
/// </summary>
/// <param name="UserId">The signed-in account.</param>
public record GetMyPasskeysQuery(Guid UserId) : IRequest<ErrorOr<PasskeyListDto>>;
