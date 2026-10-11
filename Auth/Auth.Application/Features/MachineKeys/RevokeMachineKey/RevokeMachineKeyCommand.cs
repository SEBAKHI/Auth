using ErrorOr;
using MediatR;

namespace Auth.Application.Features.MachineKeys.RevokeMachineKey;

/// <summary>
/// Revokes one of an application's machine keys (OI-128). A machine token the key already obtained
/// keeps working until it expires, within five minutes.
/// </summary>
public record RevokeMachineKeyCommand(Guid ApplicationId, Guid MachineKeyId) : IRequest<ErrorOr<Success>>
{
    /// <summary>The administrator revoking the key.</summary>
    public Guid RevokedBy { get; init; }
}
