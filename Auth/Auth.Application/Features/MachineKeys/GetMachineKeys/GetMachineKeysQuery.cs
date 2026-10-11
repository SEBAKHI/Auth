using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.MachineKeys.GetMachineKeys;

/// <summary>
/// Lists one application's machine keys, revoked ones included, newest first (OI-128).
/// </summary>
public record GetMachineKeysQuery(Guid ApplicationId) : IRequest<ErrorOr<IReadOnlyList<MachineKeyDto>>>;
