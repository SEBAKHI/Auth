using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPublishedPrivacyPolicy;

/// <summary>
/// Public query for the published policy in one language (falling back to the
/// neutral language), together with the live numeric disclosures.
/// </summary>
public record GetPublishedPrivacyPolicyQuery(string? LanguageCode)
    : IRequest<ErrorOr<PublishedPrivacyPolicyDto>>;
