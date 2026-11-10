using Microsoft.Extensions.Configuration;

namespace Auth.Application.Configuration;

/// <summary>
/// The <c>Passkeys</c> section (S20, PK2, PK19): whether people may add and use passkeys,
/// and the relying party their passkeys are bound to.
/// </summary>
/// <remarks>
/// <para>
/// In the console's registry since the screens that use it shipped (S20 PR C): Platform
/// settings › Passkeys, beside the settings files and the environment. A save there is checked
/// as the files are not (T-C9): the RP ID must be a host name that covers every first-party
/// origin (PK2), and may not replace the one live passkeys are bound to (PK3).
/// </para>
/// <para>
/// Off by default, and with no RP ID by default: a deployment that sets neither behaves as
/// before S20. Switching <see cref="Enabled"/> on is not enough — the feature also needs an
/// RP ID that covers every first-party origin, at least one such origin, and the schema;
/// without any of them it stays off and the posture says why (<c>passkey.configuration</c>).
/// </para>
/// </remarks>
public sealed class PasskeySettings
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Passkeys";

    /// <summary>
    /// Gets or sets whether passkeys can be added and used. Read per request (hot): off, every
    /// passkey endpoint answers 404, and no rule demands a passkey or leans on one (D-150-4,
    /// «off means off»): the MFA policies see none, setting up the app treats the account as
    /// having no factor, and the refusal of a passkey-only administrator at the other doors
    /// pauses. Nothing is revoked: the passkeys stay in the inventory (FA2), and when this is
    /// switched on again every rule applies again.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the RP ID (PK2): the registrable domain every passkey is bound to, such as
    /// <c>example.com</c> for apps at <c>console.example.com</c> and
    /// <c>accounts.example.com</c>, or <c>localhost</c> in development. Never derived from a
    /// URL or a request's host. Read once, at start-up (restart required): a passkey is bound
    /// to the RP ID it was made under, and one of another RP ID neither signs in nor counts
    /// (PK3). Empty (the default) switches the feature off.
    /// </summary>
    public string RpId { get; set; } = string.Empty;
}

/// <summary>
/// The <see cref="PasskeySettings.RpId"/> this process started with (PK2: restart required),
/// normalized: trimmed and lower-case. Every read of a passkey by RP ID uses this value, so an
/// edit of the setting while the API runs changes nothing until it restarts.
/// </summary>
/// <param name="Value">The RP ID, or empty when none is set.</param>
public sealed record PasskeyRpIdAtStartup(string Value)
{
    /// <summary>No RP ID: the feature is off, and no passkey counts as a factor.</summary>
    public static PasskeyRpIdAtStartup None { get; } = new(string.Empty);

    /// <summary>Gets whether an RP ID is set.</summary>
    public bool IsSet => Value.Length > 0;

    /// <summary>
    /// Reads the RP ID from the configuration the process starts with: every layer, the
    /// console's database layer included, so <c>Program.cs</c> reads it only once that layer has
    /// joined (an RP ID saved in the console takes effect at the next start, as it says).
    /// </summary>
    public static PasskeyRpIdAtStartup From(IConfiguration configuration) =>
        new(Normalize(configuration[$"{PasskeySettings.SectionName}:{nameof(PasskeySettings.RpId)}"]));

    /// <summary>An RP ID as every reader compares it: trimmed and lower-case; empty for none.</summary>
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
