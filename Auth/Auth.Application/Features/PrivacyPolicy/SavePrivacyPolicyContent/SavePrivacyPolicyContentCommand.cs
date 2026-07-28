using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.SavePrivacyPolicyContent;

/// <summary>
/// Creates or replaces one language document of a policy revision.
/// </summary>
public record SavePrivacyPolicyContentCommand(
    string Version,
    string LanguageCode,
    string ContentJson) : IRequest<ErrorOr<PrivacyPolicyContentDto>>
{
    /// <summary>Gets the admin saving the document (set by the endpoint).</summary>
    public Guid RequestedBy { get; init; }
}
