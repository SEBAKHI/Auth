using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Auth_API.Common;

/// <summary>
/// Verifies that a freshly minted account-deletion identifier HMAC key is not
/// being layered over a deletion registry that already holds rows.
///
/// <para>
/// The key is permanent by design: every <c>AccountDeletionTombstones</c> digest
/// is derived from it. Mint a second key over a populated registry and nothing
/// fails — reservations simply stop matching, so identifiers the privacy policy
/// promises are never recycled become registrable again, and the orphaned rows
/// are retained forever serving no purpose. There is no error to observe, which
/// is exactly why this has to be checked rather than trusted.
/// </para>
///
/// <para>
/// Fail-closed: an unreachable database or a missing table means the claim
/// cannot be verified, and a permanent key that may already be wrong is not
/// something to serve traffic on.
/// </para>
/// </summary>
public static class IdentifierKeyRegenerationGuard
{
    /// <summary>
    /// Short, non-reversible identifier for a key, safe to log. Lets an operator
    /// tell "the same key as last boot" from "a new one" without ever writing
    /// key material to a log file.
    /// </summary>
    public static string Fingerprint(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return "none";
        }

        var digest = SHA256.HashData(Convert.FromBase64String(base64Key));
        return Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant();
    }

    /// <summary>
    /// Throws when the deletion registry already holds rows, i.e. the new key
    /// cannot be the one those rows were written under.
    /// </summary>
    public static void EnsureRegistryIsEmpty(string connectionString)
    {
        int existingRows;

        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM [dbo].[AccountDeletionTombstones]";
            command.CommandTimeout = 10;

            existingRows = Convert.ToInt32(command.ExecuteScalar());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Refusing to start: a new account-deletion identifier HMAC key was just generated, but the " +
                "existing deletion registry could not be read to confirm the key is not replacing an earlier " +
                "one. Restore the original secrets file if one exists, or verify that " +
                "[dbo].[AccountDeletionTombstones] is present and the database is reachable. " +
                "A permanent key must never be adopted unverified.",
                ex);
        }

        if (existingRows == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Refusing to start: a NEW account-deletion identifier HMAC key was generated, but " +
            $"[dbo].[AccountDeletionTombstones] already holds {existingRows} row(s) written under a " +
            "different key. Adopting this key would silently orphan every existing identifier " +
            "reservation — deleted identifiers would become registrable again, contradicting the " +
            "published policy, with no error at any point. Restore the original secrets file. " +
            "This key is permanent and is not recoverable by regeneration.");
    }
}
