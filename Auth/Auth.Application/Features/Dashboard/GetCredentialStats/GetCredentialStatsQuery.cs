using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Dashboard.GetCredentialStats;

/// <summary>
/// Query to get the expiry posture of issued API and webhook keys over a forward horizon.
/// </summary>
/// <remarks>
/// The horizon runs forward and is deliberately not the dashboard's trailing window:
/// "the last 14 days" and "expiring within 14 days" are different questions and must
/// never share a parameter.
/// </remarks>
public record GetCredentialStatsQuery(int HorizonDays = 14) : IRequest<ErrorOr<CredentialStatsDto>>
{
    /// <summary>
    /// Caller identity, set by the controller. Decides which buckets are filled: the
    /// two families carry two different permissions and RequirePermission takes only
    /// one, so the gate lives in the handler.
    /// </summary>
    public Guid RequestedBy { get; init; }
}
