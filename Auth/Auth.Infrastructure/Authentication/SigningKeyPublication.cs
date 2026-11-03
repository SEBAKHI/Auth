using System.Security.Cryptography;
using Auth.Application.Interfaces;
using Auth.Infrastructure.Security;

namespace Auth.Infrastructure.Authentication;

/// <summary>
/// At start, records in the secrets file when the staged signing key was first published
/// (S12, KR3): a scheduled promote waits 24 hours from that moment, so every application has
/// fetched the key before a token signed with it reaches them.
/// </summary>
/// <remarks>
/// The first process start that loads the staged key writes the moment, under the file's lock
/// and only while the file records none for that same key (<see cref="DpapiSecretService.RecordStagedSigningKeyPublishedAsync"/>).
/// A failed write stops nothing: the key is published anyway, and a promote stays refused
/// until a later start records the moment.
/// </remarks>
public static class SigningKeyPublication
{
    /// <summary>What the start did about the staged key's publication.</summary>
    public enum Outcome
    {
        /// <summary>No staged key without a recorded moment, or no ring.</summary>
        NothingToRecord,

        /// <summary>This start recorded the moment, or found it recorded by another process.</summary>
        Recorded,

        /// <summary>The file no longer stages the key this process loaded (another process changed the ring).</summary>
        StagedKeyChanged,

        /// <summary>The file could not be written; a promote stays refused until a later start records it.</summary>
        Failed
    }

    /// <summary>Records the staged key's publication, if this process is the first to load it.</summary>
    /// <returns>The ring with the moment the file records, and what happened.</returns>
    public static async Task<(SigningKeyRing Ring, Outcome Outcome, Exception? Error)> RecordAsync(
        DpapiSecretService secrets,
        SigningKeyRing loaded,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (!loaded.Source.HoldsRing || loaded.Ring.Next is not { PublishedAtUtc: null } staged)
        {
            return (loaded, Outcome.NothingToRecord, null);
        }

        DateTime? recorded;
        try
        {
            recorded = await secrets.RecordStagedSigningKeyPublishedAsync(staged.Kid, staged.Thumbprint, nowUtc, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or SecretDecryptionException)
        {
            return (loaded, Outcome.Failed, ex);
        }

        if (recorded is not { } publishedAt)
        {
            return (loaded, Outcome.StagedKeyChanged, null);
        }

        return (loaded.With(loaded.Ring.WithNextPublishedAt(publishedAt)) ?? loaded, Outcome.Recorded, null);
    }
}
