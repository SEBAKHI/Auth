using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.CreatePrivacyPolicyVersion;

/// <summary>
/// Records a new privacy-policy revision in the registry.
/// </summary>
public record CreatePrivacyPolicyVersionCommand(
    string Version,
    DateTime EffectiveDateUtc) : IRequest<ErrorOr<PrivacyPolicyVersionDto>>
{
    /// <summary>Gets the admin recording the revision (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
