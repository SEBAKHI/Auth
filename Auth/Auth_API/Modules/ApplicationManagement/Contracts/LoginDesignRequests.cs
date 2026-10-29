namespace Auth_API.Modules.ApplicationManagement.Contracts;

/// <summary>Request to create a sign-in page design. It starts inactive.</summary>
/// <param name="Name">The design's name.</param>
/// <param name="Content">Its HTML; left out, the design starts from the starter design.</param>
/// <param name="StringsJson">Its per-language strings: <c>{ "&lt;lang&gt;": { "&lt;key&gt;": "…" } }</c>.</param>
/// <param name="ShowPlatformLogo">Whether the platform's mark keeps the page's corner (default true).</param>
public record CreateLoginDesignRequest(string Name, string? Content, string? StringsJson, bool ShowPlatformLogo = true);

/// <summary>
/// Request to save a design. On the active design the save is what visitors see next: there is
/// no draft and no publish.
/// </summary>
/// <param name="Name">The design's name.</param>
/// <param name="Content">Its HTML.</param>
/// <param name="StringsJson">Its per-language strings.</param>
/// <param name="ShowPlatformLogo">Whether the platform's mark keeps the page's corner.</param>
/// <param name="ExpectedRevision">The <c>revision</c> the editor loaded; refused (409) when the design was saved since.</param>
public record UpdateLoginDesignRequest(
    string Name,
    string Content,
    string? StringsJson,
    bool ShowPlatformLogo,
    DateTime ExpectedRevision);

/// <summary>Request to copy a design into a new, inactive one.</summary>
/// <param name="Name">The copy's name.</param>
public record CopyLoginDesignRequest(string Name);

/// <summary>Request to render a design as the editor holds it, without saving.</summary>
/// <param name="Content">The HTML being edited.</param>
/// <param name="StringsJson">The strings being edited.</param>
/// <param name="Language">The language to render.</param>
public record PreviewLoginDesignRequest(string Content, string? StringsJson, string Language);
