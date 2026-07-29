using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.PublishPrivacyPolicyVersion;

/// <summary>
/// Makes one revision the published policy served to end users.
/// </summary>
public record PublishPrivacyPolicyVersionCommand(string Version) : IRequest<ErrorOr<Success>>
{
    /// <summary>Gets the admin publishing the revision (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
