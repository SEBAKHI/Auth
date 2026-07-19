using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.InitiateOwnershipTransfer;

/// <summary>
/// Command to initiate an organization ownership transfer. Emails a one-time
/// confirmation code to the prospective new owner; only the current owner can
/// initiate.
/// </summary>
public record InitiateOwnershipTransferCommand(
    Guid OrganizationId,
    Guid NewOwnerId) : IRequest<ErrorOr<InitiateOwnershipTransferResponse>>
{
    /// <summary>
    /// The ID of the user requesting the transfer (must be the owner).
    /// </summary>
    public Guid RequestedBy { get; init; }
}
