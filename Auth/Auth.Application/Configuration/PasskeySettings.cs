using Microsoft.Extensions.Configuration;

namespace Auth.Application.Configuration;

/// <summary>
/// The <c>Passkeys</c> section (S20, PK2, PK19): whether people may add and use passkeys,
/// the relying party their passkeys are bound to, and how long a sign-in by code counts as the
/// mailbox proof when one is added (OI-280 PR B).
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

    /// <summary>The shortest time a sign-in by code counts as the mailbox proof, in minutes.</summary>
    public const int MinCodeSignInProofMinutes = 1;

    /// <summary>The longest time a sign-in by code counts as the mailbox proof, in minutes.</summary>
    public const int MaxCodeSignInProofMinutes = 15;

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

    /// <summary>
    /// Gets or sets how long, in minutes, a sign-in by emailed code counts as the proof of the
    /// account's mailbox when the session adds a passkey (OI-280 PR B, owner decision D-S20-21).
    /// Within it no second code is emailed for the passkey: an account with a password still gives
    /// its password first, and one without goes straight to the device. Past it, a fresh code is
    /// emailed, as before. Read per request; outside 1–15 it is brought inside
    /// (<see cref="CodeSignInProof"/>), so no stored value switches the code off or stretches it.
    /// </summary>
    /// <remarks>
    /// The re-authentication window (<c>TwoFactor:ReauthenticationMaxAgeMinutes</c>) stays the outer
    /// limit: a session older than the window never reaches this rule, so a window of 5 with 15
    /// here acts as 5. Its price: a session stolen within this time adds a passkey without the
    /// second code; the notice email and the 72-hour revocation remain.
    /// </remarks>
    public int CodeSignInProofMinutes { get; set; } = 5;

    /// <summary><see cref="CodeSignInProofMinutes"/>, brought inside its range.</summary>
    public TimeSpan CodeSignInProof =>
        TimeSpan.FromMinutes(Math.Clamp(CodeSignInProofMinutes, MinCodeSignInProofMinutes, MaxCodeSignInProofMinutes));
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
