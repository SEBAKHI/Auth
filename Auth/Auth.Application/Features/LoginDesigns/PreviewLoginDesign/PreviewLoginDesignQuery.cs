using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.LoginDesigns.PreviewLoginDesign;

/// <summary>
/// Renders a design as its editor holds it, for one language, without saving anything. A
/// query: it reads the scope's names and changes no state.
/// </summary>
/// <param name="ApplicationId">The scope whose names the preview shows: the application, or null for the platform.</param>
/// <param name="Content">The HTML being edited.</param>
/// <param name="StringsJson">The strings being edited.</param>
/// <param name="Language">The language to render.</param>
public sealed record PreviewLoginDesignQuery(
    Guid? ApplicationId,
    string Content,
    string? StringsJson,
    string Language) : IRequest<ErrorOr<LoginDesignPreviewDto>>;
