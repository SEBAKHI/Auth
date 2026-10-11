using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Primitives;
using ErrorOr;

namespace Auth.Domain.Entities;

/// <summary>
/// An application's machine key (OI-128): the public half of an ES256 key pair with which the
/// application's own server signs the client assertion of the <c>client_credentials</c> grant.
/// </summary>
/// <remarks>
/// An independent entity with its own repository, the way <see cref="ApiKey"/> is: the
/// application does not hold its keys. The private key is never part of it. <see cref="Kid"/> is
/// the key's RFC 7638 thumbprint, so the same public key cannot belong to two applications.
/// </remarks>
public class ApplicationMachineKey : EntityBase
{
    /// <summary>The application this key authenticates.</summary>
    public Guid ApplicationId { get; private set; }

    /// <summary>The key's identifier, which the assertion's <c>kid</c> header names.</summary>
    public string Kid { get; private set; } = string.Empty;

    /// <summary>An optional label, for the people who manage the key.</summary>
    public string? Name { get; private set; }

    /// <summary>The signing algorithm; <see cref="MachineClients.SigningAlgorithm"/> today.</summary>
    public string Algorithm { get; private set; } = MachineClients.SigningAlgorithm;

    /// <summary>The public key as a JWK: <c>kty</c>, <c>crv</c>, <c>x</c> and <c>y</c> only.</summary>
    public string PublicKeyJwk { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    /// <summary>When the key last obtained a machine token.</summary>
    public DateTime? LastUsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public string? RevokeReason { get; private set; }

    /// <summary>A revoked key signs nothing that is accepted.</summary>
    public bool IsRevoked => RevokedAt.HasValue;

    private ApplicationMachineKey() : base()
    {
    }

    /// <summary>Rebuilds a stored key (persistence only).</summary>
    public ApplicationMachineKey(
        Guid id,
        Guid applicationId,
        string kid,
        string? name,
        string algorithm,
        string publicKeyJwk,
        DateTime createdAt,
        Guid createdBy,
        DateTime? lastUsedAt,
        DateTime? revokedAt,
        Guid? revokedBy,
        string? revokeReason) : base(id)
    {
        ApplicationId = applicationId;
        Kid = kid;
        Name = name;
        Algorithm = algorithm;
        PublicKeyJwk = publicKeyJwk;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        LastUsedAt = lastUsedAt;
        RevokedAt = revokedAt;
        RevokedBy = revokedBy;
        RevokeReason = revokeReason;
    }

    /// <summary>
    /// A new active key. <paramref name="kid"/> and <paramref name="publicKeyJwk"/> come from the
    /// key-material service, which has already checked the key; this refuses only what no caller
    /// may ever store: an empty identifier or key.
    /// </summary>
    public static ErrorOr<ApplicationMachineKey> Create(
        Guid applicationId,
        string kid,
        string publicKeyJwk,
        string? name,
        Guid createdBy,
        DateTime createdAtUtc)
    {
        if (applicationId == Guid.Empty
            || string.IsNullOrWhiteSpace(kid)
            || string.IsNullOrWhiteSpace(publicKeyJwk))
        {
            return MachineKeyErrors.InvalidPublicKey;
        }

        return new ApplicationMachineKey
        {
            ApplicationId = applicationId,
            Kid = kid,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Algorithm = MachineClients.SigningAlgorithm,
            PublicKeyJwk = publicKeyJwk,
            CreatedAt = createdAtUtc,
            CreatedBy = createdBy,
        };
    }

    /// <summary>Revokes the key. A key is revoked once; the second attempt is a conflict.</summary>
    public ErrorOr<Success> Revoke(Guid revokedBy, string? reason, DateTime revokedAtUtc)
    {
        if (IsRevoked)
        {
            return MachineKeyErrors.AlreadyRevoked;
        }

        RevokedAt = revokedAtUtc;
        RevokedBy = revokedBy;
        RevokeReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return Result.Success;
    }
}
