using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Passkeys.RenamePasskey;

/// <summary>Gives one of the signed-in account's live passkeys another name.</summary>
/// <param name="UserId">The signed-in account.</param>
/// <param name="PasskeyId">The passkey's row id.</param>
/// <param name="Name">The new name; blank takes a default.</param>
public record RenamePasskeyCommand(Guid UserId, Guid PasskeyId, string? Name) : IRequest<ErrorOr<Success>>;
