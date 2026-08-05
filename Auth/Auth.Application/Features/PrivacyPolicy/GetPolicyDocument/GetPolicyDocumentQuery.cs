using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.PrivacyPolicy.GetPolicyDocument;

/// <summary>
/// Fetches the served HTML document for a language.
/// </summary>
/// <param name="LanguageCode">Requested language; unsupported values are rejected.</param>
/// <param name="Version">
/// A specific revision for the permanent archive, or null for the one currently
/// published. Every revision keeps its own address so a rights request, an
/// acknowledgement record or a regulator can cite the exact text that applied on
/// a given date.
/// </param>
public record GetPolicyDocumentQuery(string LanguageCode, string? Version = null)
    : IRequest<ErrorOr<PolicyDocumentDto>>;
