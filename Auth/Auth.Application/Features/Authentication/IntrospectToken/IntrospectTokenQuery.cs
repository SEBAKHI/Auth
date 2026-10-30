using Auth.Application.DTOs;
using Auth.Application.Features.MachineCredentialChecks;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Authentication.IntrospectToken;

/// <summary>
/// An application's server asks whether a token is active (RFC 7662), and learns only about the
/// tokens issued to it (OI-128 PR B, owner decision D-128-6 (a)).
/// </summary>
/// <param name="Caller">The application asking, from its machine token.</param>
/// <param name="Token">The token to introspect.</param>
/// <param name="TokenTypeHint">Optional hint about the type of token.</param>
public record IntrospectTokenQuery(
    MachineCredentialCaller Caller,
    string Token,
    TokenTypeHint? TokenTypeHint
) : IRequest<ErrorOr<IntrospectTokenResponse>>;
