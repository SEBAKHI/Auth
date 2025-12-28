using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApiKeyManagement.Commands;

/// <summary>
/// Command to create a new API key.
/// </summary>
public record CreateApiKeyCommand(
    Guid ApplicationId,
    string Name,
    string? Description = null,
    string Environment = "production",
    int RateLimitPerMinute = 60,
    int RateLimitPerDay = 10000,
    DateTime? ExpiresAt = null,
    IReadOnlyList<Guid>? PermissionIds = null) : IRequest<ErrorOr<CreateApiKeyResponse>>
{
    /// <summary>
    /// The ID of the user creating this API key (for audit).
    /// </summary>
    public Guid CreatedBy { get; set; }
}
