using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.UpdatePrivacyPolicyVersion;

/// <summary>
/// Updates a revision's editable metadata: its identifier (drafts only), when
/// it takes effect, and the note describing what changed.
/// </summary>
public record UpdatePrivacyPolicyVersionCommand(
    string Version,
    DateTime EffectiveDateUtc,
    string? ChangeNote) : IRequest<ErrorOr<PrivacyPolicyVersionDto>>
{
    /// <summary>
    /// New "YYYY.MM" identifier. Null or unchanged leaves it alone. Only a
    /// draft that was never announced may be renamed — see the handler.
    /// </summary>
    public string? NewVersion { get; init; }

    /// <summary>Gets the admin making the change (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
