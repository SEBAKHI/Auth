using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// One design of the panel half of the sign-in pages: free HTML and CSS with Liquid
/// placeholders and per-language strings, owned by an application or, with no application,
/// by the platform.
/// </summary>
/// <remarks>
/// <para>
/// There is no draft and no publish. A scope (one application, or the platform) has any number
/// of designs and at most one active: the one its sign-in pages show. Saving the active design
/// is what visitors see next; a new design and a copy start inactive, and only
/// <see cref="Activate"/> makes one active. The database holds the "at most one" (a filtered
/// unique index), because two activations can race; the swap itself is the repository's one
/// transaction.
/// </para>
/// <para>
/// The content is the administrator's. Nothing is stripped from it and no address in it is
/// checked: the sign-in pages show it in an isolated frame, and markup that cannot run there is
/// reported (<see cref="Warnings"/>), not refused.
/// </para>
/// </remarks>
public sealed partial class LoginDesign : AggregateRoot
{
    /// <summary>Longest name, in characters.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Largest content, in UTF-8 bytes (64 KB).</summary>
    public const int MaxContentBytes = 64 * 1024;

    /// <summary>Largest strings document, in UTF-8 bytes (64 KB).</summary>
    public const int MaxStringsBytes = 64 * 1024;

    /// <summary>
    /// Largest rendered HTML, in UTF-8 bytes, in any language (256 KB). The content's limit is
    /// on its source; a Liquid loop renders far more than it is long.
    /// </summary>
    public const int MaxRenderedBytes = 256 * 1024;

    /// <summary>Gets the owning application, or null for one of the platform's designs.</summary>
    public Guid? ApplicationId { get; private set; }

    /// <summary>Gets the administrator-facing name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the Liquid HTML of the panel.</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>Gets the per-language strings: <c>{ "&lt;lang&gt;": { "&lt;key&gt;": "…" } }</c>.</summary>
    public string StringsJson { get; private set; } = "{}";

    /// <summary>
    /// Gets whether the sign-in pages keep the platform's mark in their corner. False gives the
    /// corner to the application's logo; "Secured by" still names the platform.
    /// </summary>
    public bool ShowPlatformLogo { get; private set; } = true;

    /// <summary>Gets whether this is the design its scope's sign-in pages show.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Gets the SHA-256 (lower-case hex) of the content and the strings: the version a sign-in
    /// page keys its copy of the rendered design on.
    /// </summary>
    public string ContentHash { get; private set; } = string.Empty;

    private LoginDesign() : base()
    {
    }

    /// <summary>Rebuilds a stored design (persistence only).</summary>
    public LoginDesign(
        Guid id,
        Guid? applicationId,
        string name,
        string content,
        string stringsJson,
        bool showPlatformLogo,
        bool isActive,
        string contentHash,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        ApplicationId = applicationId;
        Name = name;
        Content = content;
        StringsJson = stringsJson;
        ShowPlatformLogo = showPlatformLogo;
        IsActive = isActive;
        ContentHash = contentHash;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    /// <summary>Creates a design for a scope. It starts inactive.</summary>
    public static ErrorOr<LoginDesign> Create(
        Guid? applicationId,
        string name,
        string content,
        string? stringsJson,
        bool showPlatformLogo,
        Guid createdBy)
    {
        var strings = NormalizeStrings(stringsJson);
        var valid = Validate(name, content, strings);
        if (valid.IsError)
        {
            return valid.Errors;
        }

        var design = new LoginDesign
        {
            ApplicationId = applicationId,
            Name = name.Trim(),
            Content = content,
            StringsJson = strings,
            ShowPlatformLogo = showPlatformLogo,
            IsActive = false,
            ContentHash = Fingerprint(content, strings)
        };
        design.SetCreated(createdBy);
        return design;
    }

    /// <summary>
    /// Replaces the name, content, strings and logo choice. On the active design this is what
    /// the scope's sign-in pages show next.
    /// </summary>
    public ErrorOr<Success> Update(
        string name,
        string content,
        string? stringsJson,
        bool showPlatformLogo,
        Guid modifiedBy)
    {
        var strings = NormalizeStrings(stringsJson);
        var valid = Validate(name, content, strings);
        if (valid.IsError)
        {
            return valid.Errors;
        }

        Name = name.Trim();
        Content = content;
        StringsJson = strings;
        ShowPlatformLogo = showPlatformLogo;
        ContentHash = Fingerprint(content, strings);
        SetModified(modifiedBy);
        return Result.Success;
    }

    /// <summary>A new, inactive design in the same scope with this one's content.</summary>
    public ErrorOr<LoginDesign> Copy(string name, Guid createdBy) =>
        Create(ApplicationId, name, Content, StringsJson, ShowPlatformLogo, createdBy);

    /// <summary>
    /// Marks this design as the one its scope shows. Whichever design was active before is
    /// deactivated by the repository in the same transaction.
    /// </summary>
    /// <remarks>
    /// Activation leaves <see cref="AuditableEntityBase.ModifiedAt"/> alone: that stamp is the
    /// revision an open editor saves against, and switching a design on or off does not change
    /// what the editor holds. The audit log records who switched it and when.
    /// </remarks>
    /// <returns>False when it was already active: nothing changes.</returns>
    public bool Activate()
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        return true;
    }

    /// <summary>
    /// Stops showing this design: the scope falls back to the platform's active design, or, for
    /// the platform, to the plain panel. Leaves the revision alone, as <see cref="Activate"/> does.
    /// </summary>
    /// <returns>False when it was not active: nothing changes.</returns>
    public bool Deactivate()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        return true;
    }

    /// <summary>
    /// Whether the stored design is still the revision an editor loaded (its last save, or its
    /// creation when never saved since).
    /// </summary>
    public bool IsRevision(DateTime revision) => (ModifiedAt ?? CreatedAt) == revision;

    /// <summary>Refuses to delete the design visitors see.</summary>
    public ErrorOr<Success> EnsureDeletable() =>
        IsActive ? LoginDesignErrors.DeleteActive : Result.Success;

    /// <summary>Whether this design belongs to the scope (an application's id, or null for the platform).</summary>
    public bool BelongsTo(Guid? applicationId) => ApplicationId == applicationId;

    /// <summary>Markup in the content that will not run on a sign-in page.</summary>
    public IReadOnlyList<LoginDesignWarning> Warnings => WarningsOf(Content);

    /// <summary>Markup in <paramref name="content"/> that will not run on a sign-in page.</summary>
    public static IReadOnlyList<LoginDesignWarning> WarningsOf(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var warnings = new List<LoginDesignWarning>(2);
        if (ScriptRegex().IsMatch(content))
        {
            warnings.Add(LoginDesignWarning.Script);
        }

        if (EventHandlerRegex().IsMatch(content))
        {
            warnings.Add(LoginDesignWarning.EventHandler);
        }

        return warnings;
    }

    /// <summary>Whether the content fits: not blank, at most <see cref="MaxContentBytes"/> of UTF-8.</summary>
    public static ErrorOr<Success> CheckContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return LoginDesignErrors.ContentRequired;
        }

        return Encoding.UTF8.GetByteCount(content) > MaxContentBytes
            ? LoginDesignErrors.ContentTooLarge
            : Result.Success;
    }

    /// <summary>
    /// Whether the strings document is well formed: empty, or a JSON object of languages, each
    /// an object of text strings, at most <see cref="MaxStringsBytes"/> of UTF-8.
    /// </summary>
    public static ErrorOr<Success> CheckStrings(string? stringsJson)
    {
        if (string.IsNullOrWhiteSpace(stringsJson))
        {
            return Result.Success;
        }

        if (Encoding.UTF8.GetByteCount(stringsJson) > MaxStringsBytes)
        {
            return LoginDesignErrors.StringsTooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(stringsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return LoginDesignErrors.StringsInvalidJson;
            }

            foreach (var language in root.EnumerateObject())
            {
                if (language.Value.ValueKind != JsonValueKind.Object ||
                    language.Value.EnumerateObject().Any(text => text.Value.ValueKind != JsonValueKind.String))
                {
                    return LoginDesignErrors.StringsInvalidJson;
                }
            }

            return Result.Success;
        }
        catch (JsonException)
        {
            return LoginDesignErrors.StringsInvalidJson;
        }
    }

    /// <summary>Whether the name fits: not blank, at most <see cref="MaxNameLength"/> characters.</summary>
    public static ErrorOr<Success> CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return LoginDesignErrors.NameRequired;
        }

        return name.Trim().Length > MaxNameLength ? LoginDesignErrors.NameTooLong : Result.Success;
    }

    /// <summary>The version of a design's visible part: SHA-256 of its content and strings.</summary>
    public static string Fingerprint(string content, string stringsJson)
    {
        // A NUL cannot occur in either half's meaning, so no two pairs share one input.
        var bytes = Encoding.UTF8.GetBytes(content + "\0" + stringsJson);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string NormalizeStrings(string? stringsJson) =>
        string.IsNullOrWhiteSpace(stringsJson) ? "{}" : stringsJson;

    private static ErrorOr<Success> Validate(string? name, string? content, string strings)
    {
        var checks = new[] { CheckName(name), CheckContent(content), CheckStrings(strings) };
        var errors = checks.Where(check => check.IsError).SelectMany(check => check.Errors).ToList();
        return errors.Count == 0 ? Result.Success : errors;
    }

    [GeneratedRegex(@"<script\b|javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptRegex();

    // An attribute whose name starts with "on", inside a tag: <img src=x onerror=…>. The scan from
    // a tag's start stops at the next "<" as well as at ">", so every character is scanned from
    // one start at most: with [^>] alone, "<a" repeated 32,768 times rescans to the end from each
    // start (27 s). A "<" inside an attribute value before the handler hides that handler; it is
    // a warning, and nothing in the design runs either way.
    [GeneratedRegex(@"<[a-z][^<>]*?\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandlerRegex();
}
