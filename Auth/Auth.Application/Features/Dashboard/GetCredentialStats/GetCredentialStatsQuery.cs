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

    /// <summary>
    /// Whether the caller's access token carries <c>apikeys:read</c> (wildcards
    /// included), set by the controller. Visibility needs the token AND the live
    /// grant: the token, so a session whose platform authority is withheld until it
    /// proves a second factor sees nothing; the live grant, so a permission revoked
    /// after the token was minted is not honoured.
    /// </summary>
    public bool TokenGrantsApiKeysRead { get; init; }

    /// <summary>
    /// Whether the caller's access token carries <c>webhookkeys:read</c>, set by the
    /// controller; see <see cref="TokenGrantsApiKeysRead"/>.
    /// </summary>
    public bool TokenGrantsWebhookKeysRead { get; init; }
}
