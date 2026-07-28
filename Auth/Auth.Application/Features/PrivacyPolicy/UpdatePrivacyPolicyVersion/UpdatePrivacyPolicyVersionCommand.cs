using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.UpdatePrivacyPolicyVersion;

/// <summary>
/// Updates a revision's editable metadata: when it takes effect and the note
/// describing what changed.
/// </summary>
public record UpdatePrivacyPolicyVersionCommand(
    string Version,
    DateTime EffectiveDateUtc,
    string? ChangeNote) : IRequest<ErrorOr<PrivacyPolicyVersionDto>>
{
    /// <summary>Gets the admin making the change (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
