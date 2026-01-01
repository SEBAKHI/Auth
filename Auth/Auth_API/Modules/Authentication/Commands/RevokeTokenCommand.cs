using Auth_API.Modules.Authentication.Contracts;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Commands;

/// <summary>
/// Command to revoke an access or refresh token.
/// </summary>
/// <param name="Token">The token to revoke.</param>
/// <param name="TokenTypeHint">Optional hint about the type of token.</param>
/// <param name="RevokedBy">The user ID revoking the token (if authenticated).</param>
public record RevokeTokenCommand(
    string Token,
    TokenTypeHint? TokenTypeHint,
    Guid? RevokedBy
) : IRequest<ErrorOr<Success>>;
