namespace Auth.Application.Security.Keyring;

/// <summary>
/// One HMAC key of the credential keyring: its material and where it stands in
/// its life (staged, issuing, verify-only, compromised).
/// </summary>
/// <remarks>
/// Immutable. A change to a key is a new <see cref="HmacKeyring"/> holding a new
/// instance, so a value derived from an instance (a purpose subkey) can be cached
/// against that instance for as long as it lives.
/// </remarks>
public sealed class HmacKey
{
    /// <summary>The shortest material accepted: HMAC-SHA256's own output size.</summary>
    public const int MinimumMaterialBytes = 32;

    private readonly byte[] _material;

    public HmacKey(
        int id,
        ReadOnlySpan<byte> material,
        DateTime? createdAtUtc = null,
        DateTime? verifyUntilUtc = null,
        DateTime? demotedAtUtc = null,
        bool compromised = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        if (material.Length < MinimumMaterialBytes)
        {
            throw new ArgumentException(
                $"HMAC key {id} must be at least {MinimumMaterialBytes} bytes; it is {material.Length}.",
                nameof(material));
        }

        Id = id;
        _material = material.ToArray();
        CreatedAtUtc = createdAtUtc;
        VerifyUntilUtc = verifyUntilUtc;
        DemotedAtUtc = demotedAtUtc;
        Compromised = compromised;
    }

    /// <summary>The key's id. Key 1 is the key every deployment started with.</summary>
    public int Id { get; }

    /// <summary>The raw key material.</summary>
    public ReadOnlySpan<byte> Material => _material;

    /// <summary>When the key was staged; null for key 1 migrated from the single key, whose age is unknown.</summary>
    public DateTime? CreatedAtUtc { get; }

    /// <summary>The latest instant a verify-only key is accepted for any bounded purpose, when set.</summary>
    public DateTime? VerifyUntilUtc { get; }

    /// <summary>When the key stopped issuing; null while it is current, next, or never issued.</summary>
    public DateTime? DemotedAtUtc { get; }

    /// <summary>
    /// The key leaked. It is accepted for nothing except webhook keys, which keep
    /// their grace (a key alone cannot forge a random webhook key).
    /// </summary>
    public bool Compromised { get; }
}
