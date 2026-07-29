using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyContent;

/// <summary>
/// Admin query for one language document of a revision (drafts included).
/// </summary>
public record GetPrivacyPolicyContentQuery(string Version, string LanguageCode)
    : IRequest<ErrorOr<PrivacyPolicyContentDto>>;
