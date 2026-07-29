using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.NotifyPrivacyPolicyVersion;

/// <summary>
/// Sends the policy-change notice for one recorded revision to every active
/// user, in each user's preferred language, and stamps the revision with the
/// send time and recipient count.
/// </summary>
public record NotifyPrivacyPolicyVersionCommand(string Version)
    : IRequest<ErrorOr<PrivacyPolicyNotifyResultDto>>
{
    /// <summary>Gets the admin triggering the notification (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
