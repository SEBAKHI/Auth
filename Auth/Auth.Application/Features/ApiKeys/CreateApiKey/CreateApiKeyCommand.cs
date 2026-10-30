using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.ApiKeys.CreateApiKey;

/// <summary>
/// Command to create a new API key.
/// </summary>
/// <param name="PermissionIds">The key's scopes: at least one, each an active permission of the key's application (OI-128 PR B).</param>
/// <param name="AllowedIps">The addresses the key may be presented from: IPv4 or IPv6 addresses and CIDR ranges; none means any.</param>
public record CreateApiKeyCommand(
    Guid ApplicationId,
    string Name,
    string? Description = null,
    string Environment = "production",
    DateTime? ExpiresAt = null,
    IReadOnlyList<Guid>? PermissionIds = null,
    IReadOnlyList<string>? AllowedIps = null) : IRequest<ErrorOr<CreateApiKeyResponse>>
{
    /// <summary>
    /// The ID of the user creating this API key (for audit).
    /// </summary>
    public Guid CreatedBy { get; init; }
}
