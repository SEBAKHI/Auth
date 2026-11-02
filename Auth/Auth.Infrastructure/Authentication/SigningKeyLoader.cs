using System.Security.Cryptography;
using Auth.Application.Configuration;
using Auth.Application.Features.Secrets.Common;
using Auth.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Loads the RSA key that signs every access token from the configured source that wins, and
/// refuses one that cannot sign (X03).
/// </summary>
/// <remarks>
/// The sources are tried in their long-standing order: <see cref="PathKey"/>, then
/// <see cref="EncryptedKey"/>, then <see cref="PemKey"/> (the secrets file carries this one in
/// Certificate/Dpapi mode). Until X03 the winner was imported unchecked, so a public-only or
/// 1024-bit PEM loaded and the first sign-in failed, and no source at all produced an unsaved
/// random key that lived until the next restart. The winner now passes the same check the import
/// endpoint applies (<see cref="SecretKeyMaterial.ValidateRsaPrivateKey"/>: a private part, at
/// least 2048 bits), and no source throws.
/// </remarks>
public static class SigningKeyLoader
{
    /// <summary>A PEM file on disk.</summary>
    public const string PathKey = "Jwt:PrivateKeyPath";

    /// <summary>A PEM protected by the Data Protection key ring.</summary>
    public const string EncryptedKey = "Jwt:PrivateKeyEncrypted";

    /// <summary>A PEM in configuration, which the secrets file supplies in Certificate/Dpapi mode.</summary>
    public const string PemKey = "Jwt:PrivateKeyPem";

    /// <summary>Loads the winning source's key; throws, naming the source, when it cannot sign.</summary>
    /// <exception cref="InvalidOperationException">No source, or a key that is not a usable RSA private key.</exception>
    public static RSA Load(JwtSettings settings, IDataProtectionProvider? dataProtectionProvider)
    {
        var (source, pem) = ReadWinningSource(settings, dataProtectionProvider);

        var validated = SecretKeyMaterial.ValidateRsaPrivateKey(pem);
        if (validated.IsError)
        {
            throw new InvalidOperationException(
                $"Refusing to start: the JWT signing key from {source} cannot sign tokens. " +
                $"{validated.FirstError.Description} Correct {source}, or restore the secrets file (or its " +
                ".bak) that held the key, then restart.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    private static (string Source, string Pem) ReadWinningSource(
        JwtSettings settings,
        IDataProtectionProvider? dataProtectionProvider)
    {
        // A path whose file is missing falls through to the next source, as it always has.
        // Outside Development the startup check refuses that state before this runs
        // (KeyMaterialValidator), so the fall-through is a Development convenience only.
        if (!string.IsNullOrEmpty(settings.PrivateKeyPath) && File.Exists(settings.PrivateKeyPath))
        {
            return (PathKey, File.ReadAllText(settings.PrivateKeyPath));
        }

        if (!string.IsNullOrEmpty(settings.PrivateKeyEncrypted) && dataProtectionProvider != null)
        {
            try
            {
                return (EncryptedKey, RsaKeyService.DecryptPrivateKey(dataProtectionProvider, settings.PrivateKeyEncrypted));
            }
            catch (CryptographicException ex)
            {
                throw new InvalidOperationException(
                    $"Refusing to start: {EncryptedKey} could not be decrypted. It decrypts only with the Data " +
                    "Protection key ring (and, in Certificate mode, the certificate) it was encrypted under: restore " +
                    "them, or replace the value, then restart.",
                    ex);
            }
        }

        if (!string.IsNullOrEmpty(settings.PrivateKeyPem))
        {
            return (PemKey, settings.PrivateKeyPem);
        }

        throw new InvalidOperationException(
            $"Refusing to start: no JWT signing key is configured. Set {PemKey} (the secrets file carries it in " +
            $"Certificate/Dpapi mode), {EncryptedKey} or {PathKey}.");
    }
}
