using Auth.Domain.Enums;

namespace Auth.Application.DTOs;

/// <summary>
/// An application's machine key as the console lists it (OI-128). Never any key material: not the
/// private key, which is not stored, and not the public key either, which no reader needs.
/// </summary>
public class MachineKeyDto
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }

    /// <summary>The key's identifier, which the client assertion's <c>kid</c> header names.</summary>
    public string Kid { get; set; } = string.Empty;

    public string? Name { get; set; }
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>What the key authenticates (OI-171): <c>Machine</c> or <c>SignIn</c>, by name.</summary>
    public MachineKeyPurpose Purpose { get; set; } = MachineKeyPurpose.Machine;

    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }
}

/// <summary>
/// The answer to creating a machine key: what the application's server needs to sign its first
/// assertion. <see cref="PrivateKeyPem"/> is set only when the server generated the pair, and only
/// in this one response.
/// </summary>
public class CreateMachineKeyResponse
{
    public Guid Id { get; set; }

    /// <summary>The <c>kid</c> header every assertion signed with this key carries.</summary>
    public string Kid { get; set; } = string.Empty;

    /// <summary>The application code: the assertion's <c>iss</c> and <c>sub</c>, and the form's <c>client_id</c>.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The issuer identifier: the assertion's one <c>aud</c> value.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Where the assertion is posted, as the discovery document lists it.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    public string Algorithm { get; set; } = string.Empty;

    /// <summary>What the key authenticates (OI-171): <c>Machine</c> or <c>SignIn</c>, by name.</summary>
    public MachineKeyPurpose Purpose { get; set; } = MachineKeyPurpose.Machine;

    /// <summary>
    /// The generated private key, PKCS#8 PEM. Shown once: the server keeps the public half only.
    /// Null when the administrator pasted a public key of their own.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    public DateTime CreatedAt { get; set; }
}
