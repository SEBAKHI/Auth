using System.Globalization;
using Auth.Application.Features.PrivacyPolicy.Common;
using Auth.Application.Features.PrivacyPolicy.GetPolicyDocument;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Modules.NotificationManagement.Controllers;

/// <summary>
/// The public privacy notice, served as the document it is.
///
/// Deliberately outside <c>/api/</c> and outside API versioning: this is not an
/// endpoint an application consumes, it is a page a person reads, prints, cites
/// and archives. It returns complete HTML with the policy text in the first
/// response — the shape every large publisher of privacy notices uses, and the
/// only shape that keeps the notice readable with scripting unavailable.
///
/// The bytes were rendered when an operator published the revision, so nothing
/// here interpolates, templates or falls back. Either the published document
/// exists or the answer is 404.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("privacy")]
public class PublicPolicyController : ControllerBase
{
    /// <summary>
    /// How long a shared cache may serve without revalidating, and how long it
    /// may keep serving the last good copy when the origin is failing.
    ///
    /// There is deliberately no <c>must-revalidate</c> and no <c>no-cache</c>:
    /// per RFC 9111 those oblige a disconnected cache to produce an error rather
    /// than reuse what it holds, which converts an origin blip into a broken
    /// legal page. A short shared age plus a long stale-if-error is the pairing
    /// that keeps the notice reachable without letting it go quietly stale.
    /// </summary>
    private const string CachePolicy =
        "public, s-maxage=300, stale-while-revalidate=604800, stale-if-error=2592000";

    private readonly ISender _sender;

    public PublicPolicyController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>
    /// Redirects to the best available language for this reader.
    /// </summary>
    /// <remarks>
    /// A redirect rather than content negotiation on one URL: each language must
    /// have its own address so it can be linked, cited and cached distinctly.
    /// Accept-Language is only consulted here, at the entry point — an explicit
    /// <c>/privacy/{language}</c> always wins, since overriding a stated choice
    /// with a guess from the browser is itself a documented dark pattern.
    /// </remarks>
    [HttpGet("")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Index()
    {
        return Redirect($"/privacy/{PreferredLanguage()}");
    }

    /// <summary>Serves the published notice in one language.</summary>
    [HttpGet("{language}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Published(string language, CancellationToken cancellationToken) =>
        Document(new GetPolicyDocumentQuery(language), cancellationToken);

    /// <summary>
    /// Serves a superseded revision at a permanent address.
    /// </summary>
    /// <remarks>
    /// Every revision keeps its own URL so an acknowledgement record, a rights
    /// request or a regulator can cite the exact text that applied on a date.
    /// Serving versions as client-side state behind one URL would make that
    /// unprovable.
    /// </remarks>
    [HttpGet("v{version}/{language}")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Archived(
        string version, string language, CancellationToken cancellationToken) =>
        Document(new GetPolicyDocumentQuery(language, version), cancellationToken);

    private async Task<IActionResult> Document(
        GetPolicyDocumentQuery query, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(query, cancellationToken);
        if (result.IsError)
        {
            // A plain 404, not a problem+json envelope: the caller here is a
            // browser showing a page to a person, not a client parsing errors.
            return NotFound();
        }

        var document = result.Value;
        var etag = $"\"{document.ContentHash}\"";

        // Stricter than the API-wide policy, not looser: this document loads no
        // script, no font, no image and makes no request of any kind, so
        // everything is denied and the one exception is its own stylesheet,
        // named by hash. Without this the site-wide "default-src 'self'" blocked
        // the inline <style> and the notice rendered as unstyled text.
        Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; " +
            $"style-src 'sha256-{document.StyleHash}'; " +
            "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        Response.Headers.CacheControl = CachePolicy;
        Response.Headers.ETag = etag;
        Response.Headers.LastModified =
            document.RenderedAt.ToString("R", CultureInfo.InvariantCulture);
        // The document differs per language, and each language has its own URL,
        // so nothing may key a shared entry on the request's Accept-Language.
        Response.Headers.Vary = HeaderNames.AcceptEncoding;

        if (Request.Headers.IfNoneMatch.Contains(etag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Content(document.Html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Picks the best supported language from Accept-Language, defaulting to the
    /// neutral one. Quality values are honoured; unsupported tags are skipped.
    /// </summary>
    private string PreferredLanguage()
    {
        var accepted = Request.GetTypedHeaders().AcceptLanguage;
        if (accepted is null || accepted.Count == 0) return PolicyLanguages.Fallback;

        foreach (var entry in accepted.OrderByDescending(e => e.Quality ?? 1))
        {
            var normalized = PolicyLanguages.Normalize(entry.Value.Value);
            if (normalized is not null) return normalized;
        }

        return PolicyLanguages.Fallback;
    }
}
