using System.Text.Json;

namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to replace one settings section's override set.
/// </summary>
public record UpdateSystemSettingsRequest
{
    /// <summary>
    /// The complete new sparse override object for the section, mirroring
    /// the appsettings shape (only fields that should differ from the
    /// configuration files). Fields omitted here revert to file values.
    /// </summary>
    public JsonElement Overrides { get; init; }

    /// <summary>
    /// The base64 rowversion returned by the last read; null when no
    /// override row existed yet. Stale values fail with 409.
    /// </summary>
    public string? RowVersion { get; init; }
}
