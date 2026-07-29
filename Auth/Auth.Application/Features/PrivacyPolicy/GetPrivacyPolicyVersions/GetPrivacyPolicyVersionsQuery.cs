using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPrivacyPolicyVersions;

/// <summary>
/// Query for the full privacy-policy revision registry, newest first.
/// </summary>
public record GetPrivacyPolicyVersionsQuery : IRequest<ErrorOr<IReadOnlyList<PrivacyPolicyVersionDto>>>;
