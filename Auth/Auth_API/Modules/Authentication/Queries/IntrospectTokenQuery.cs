using Auth_API.Modules.Authentication.Contracts;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.Authentication.Queries;

/// <summary>
/// Query to introspect a token and get its metadata.
/// </summary>
/// <param name="Token">The token to introspect.</param>
/// <param name="TokenTypeHint">Optional hint about the type of token.</param>
public record IntrospectTokenQuery(
    string Token,
    TokenTypeHint? TokenTypeHint
) : IRequest<ErrorOr<IntrospectTokenResponse>>;
