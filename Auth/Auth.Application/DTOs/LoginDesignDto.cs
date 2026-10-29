using Auth.Domain.Entities;
using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>One sign-in page design in full, for its editor.</summary>
public sealed record LoginDesignDto
{
    public required Guid Id { get; init; }

    /// <summary>The owning application; null for one of the platform's designs.</summary>
    public Guid? ApplicationId { get; init; }

    public required string Name { get; init; }

    /// <summary>The Liquid HTML of the panel.</summary>
    public required string Content { get; init; }

    /// <summary>The per-language strings: <c>{ "&lt;lang&gt;": { "&lt;key&gt;": "…" } }</c>.</summary>
    public required string StringsJson { get; init; }

    public required bool ShowPlatformLogo { get; init; }

    /// <summary>Whether the scope's sign-in pages show this design.</summary>
    public required bool IsActive { get; init; }

    /// <summary>SHA-256 of the content and the strings.</summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// The revision the editor loaded: sent back with a save, which is refused when the design
    /// was saved by someone else since (<c>LoginDesign.ConcurrencyConflict</c>).
    /// </summary>
    public required DateTime Revision { get; init; }

    public required DateTime CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }

    /// <summary>Markup in the content that will not run on a sign-in page (reported, never refused).</summary>
    public required IReadOnlyList<LoginDesignWarning> Warnings { get; init; }

    /// <summary>The DTO of <paramref name="design"/>.</summary>
    public static LoginDesignDto From(LoginDesign design) => new()
    {
        Id = design.Id,
        ApplicationId = design.ApplicationId,
        Name = design.Name,
        Content = design.Content,
        StringsJson = design.StringsJson,
        ShowPlatformLogo = design.ShowPlatformLogo,
        IsActive = design.IsActive,
        ContentHash = design.ContentHash,
        Revision = design.ModifiedAt ?? design.CreatedAt,
        CreatedAt = design.CreatedAt,
        ModifiedAt = design.ModifiedAt,
        Warnings = design.Warnings
    };
}

/// <summary>One line of a scope's list of designs.</summary>
public sealed record LoginDesignSummaryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required bool ShowPlatformLogo { get; init; }
    public required bool IsActive { get; init; }
    public required string ContentHash { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }

    /// <summary>The summary of <paramref name="design"/>.</summary>
    public static LoginDesignSummaryDto From(LoginDesign design) => new()
    {
        Id = design.Id,
        Name = design.Name,
        ShowPlatformLogo = design.ShowPlatformLogo,
        IsActive = design.IsActive,
        ContentHash = design.ContentHash,
        CreatedAt = design.CreatedAt,
        ModifiedAt = design.ModifiedAt
    };
}

/// <summary>A design rendered for one language, without saving it.</summary>
public sealed record LoginDesignPreviewDto
{
    /// <summary>The rendered HTML, as a sign-in page would receive it.</summary>
    public required string Html { get; init; }

    public required string Language { get; init; }

    /// <summary><c>ltr</c> or <c>rtl</c>.</summary>
    public required string Dir { get; init; }

    public required IReadOnlyList<LoginDesignWarning> Warnings { get; init; }
}

/// <summary>
/// The design a sign-in page shows in its panel, rendered for one language. Returned as JSON,
/// never as an HTML document: the API's origin must never serve markup an administrator wrote.
/// </summary>
public sealed record PublicLoginDesignDto
{
    public required Guid Id { get; init; }

    /// <summary>The version rendered (SHA-256 of the content and the strings).</summary>
    public required string ContentHash { get; init; }

    public required string Language { get; init; }

    /// <summary><c>ltr</c> or <c>rtl</c>.</summary>
    public required string Dir { get; init; }

    public required string Html { get; init; }
}

/// <summary>
/// What the branding payloads say of the design a sign-in page shows: which one and which
/// version, and whether the platform's mark keeps the corner. The page fetches the rendered
/// design only when this is present.
/// </summary>
public sealed record LoginDesignRefDto
{
    public required Guid Id { get; init; }
    public required string ContentHash { get; init; }
    public required bool ShowPlatformLogo { get; init; }

    /// <summary>The reference of <paramref name="design"/>, or null when there is none.</summary>
    public static LoginDesignRefDto? FromOrNull(LoginDesign? design) => design is null
        ? null
        : new LoginDesignRefDto
        {
            Id = design.Id,
            ContentHash = design.ContentHash,
            ShowPlatformLogo = design.ShowPlatformLogo
        };
}
