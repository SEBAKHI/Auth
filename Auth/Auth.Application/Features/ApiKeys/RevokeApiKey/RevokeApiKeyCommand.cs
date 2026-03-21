using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.RevokeApiKey;

/// <summary>
/// Command to revoke an API key.
/// </summary>
public record RevokeApiKeyCommand(
    Guid Id,
    string? Reason = null) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user revoking this API key (for audit).
    /// </summary>
    public Guid RevokedBy { get; init; }
}
