using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.TransferOwnership;

/// <summary>
/// Command to complete an organization ownership transfer. The owner path
/// requires the one-time code that was emailed to the new owner; the platform
/// administration path (PlatformScope) transfers directly — it is the recovery
/// valve for organizations whose owner can no longer act.
/// </summary>
public record TransferOwnershipCommand(
    Guid OrganizationId,
    Guid NewOwnerId,
    string? Code) : IRequest<ErrorOr<Success>>
{
    /// <summary>
    /// The ID of the user requesting the transfer.
    /// </summary>
    public Guid RequestedBy { get; init; }

    /// <summary>
    /// True when the caller holds the platform-wide organizations manage
    /// permission — allows transferring without being the owner and without a
    /// confirmation code. Set by the controller from JWT claims only, never
    /// bound from the request.
    /// </summary>
    public bool PlatformScope { get; init; }
}
