namespace Auth.Application.DTOs;

/// <summary>
/// The account bar of a linked application: who is signed in to IAM in this browser, and the
/// tiles of the applications they can open. Read by the accounts app's bar pages only, framed
/// inside a linked application on a subdomain of the IAM site.
/// </summary>
/// <remarks>
/// Nothing here leaves the frame: the pages that read it pass none of it to the application
/// that frames them.
/// </remarks>
public sealed class AccountBarDto
{
    /// <summary>The name the person is shown by: their display name, else their email.</summary>
    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    /// <summary>Public URL of the profile picture, or null when there is none.</summary>
    public string? ProfileImageUrl { get; init; }

    /// <summary>
    /// The profile's language, which the frames use only when this browser has no language of
    /// its own stored for the accounts app.
    /// </summary>
    public string? PreferredLanguage { get; init; }

    /// <summary>The profile's colour mode, under the same rule as the language.</summary>
    public string? Theme { get; init; }

    /// <summary>The tiles, by the rule IAM's Home uses (<c>MyApplicationsList</c>).</summary>
    public IReadOnlyList<MyApplicationDto> Applications { get; init; } = [];
}
