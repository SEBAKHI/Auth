using Auth.Application.DTOs;
using ErrorOr;

namespace Auth.Application.Features.PrivacyPolicy.Common;

/// <summary>
/// Turns an authored policy document into the standalone HTML that is served to
/// the public.
///
/// Rendering happens once, at publish time. That is the whole point: a document
/// rendered on the read path can only be as available and as correct as the
/// request that renders it, which is how a bracketed placeholder ended up on a
/// public legal page.
/// </summary>
public interface IPolicyDocumentRenderer
{
    /// <summary>
    /// Renders one language. Returns
    /// <see cref="Auth.Domain.Errors.PrivacyPolicyErrors.InvalidContent"/> when
    /// the document cannot be parsed, or when a <c>{{token}}</c> survives
    /// interpolation — an unresolved token would reach the reader verbatim.
    /// </summary>
    /// <param name="request">The document, its disclosure and its identity.</param>
    ErrorOr<RenderedPolicyDocument> Render(PolicyRenderRequest request);
}

/// <summary>Everything the renderer needs; grouped so the signature stays stable.</summary>
/// <param name="LanguageCode">Language of the reader — the document's <c>lang</c>.</param>
/// <param name="Content">The authored document to render.</param>
/// <param name="Disclosure">Values interpolated into the text and frozen with it.</param>
/// <param name="Version">Version label shown beside the effective date.</param>
/// <param name="AvailableLanguages">Languages this version can be read in, for the switcher.</param>
/// <param name="IsFallbackLanguage">
/// True when <paramref name="Content"/> is the neutral document standing in for
/// an unwritten language, so the renderer states that in the reader's language.
/// Silent locale fallback is a documented deceptive pattern (EDPB Guidelines
/// 03/2022); disclosed fallback is a disclosed limitation. The layer that knows
/// the fact states the fact; the layer that renders owns the wording.
/// </param>
/// <param name="AccountsBaseUrl">Origin the deletion entry point links to.</param>
public sealed record PolicyRenderRequest(
    string LanguageCode,
    PolicyDocumentModel Content,
    PrivacyPolicyDisclosureDto Disclosure,
    string Version,
    IReadOnlyList<string> AvailableLanguages,
    bool IsFallbackLanguage,
    string AccountsBaseUrl);

/// <summary>The bytes to serve and the hashes that identify them.</summary>
/// <param name="Html">A complete document: own head, inline styles, no script.</param>
/// <param name="ContentHash">Lowercase hex SHA-256 of <paramref name="Html"/>.</param>
/// <param name="StyleHash">
/// Base64 SHA-256 of the inline stylesheet, for the response's
/// <c>style-src 'sha256-…'</c>.
///
/// Travels with the document rather than being recomputed from the current
/// template: an artifact published by an earlier build carries that build's
/// stylesheet, and a hash taken from today's constant would not match it — the
/// browser would silently drop the styling of an already-published document.
/// </param>
public sealed record RenderedPolicyDocument(
    string Html, string ContentHash, string StyleHash);
