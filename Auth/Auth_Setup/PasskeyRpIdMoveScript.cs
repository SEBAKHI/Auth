using Auth.Application.Features.Passkeys.Common;
using Auth.Domain.Constants;

namespace Auth_Setup;

/// <summary>
/// Builds the owner's statement that retires the passkeys of an RP ID the deployment is
/// leaving ([S20-O10], FA11 (11-3)): when the platform moves to another registrable domain,
/// every passkey made under the old one stops working anyway — a browser offers it only on
/// its own domain — so it is revoked on purpose, with one audit row each, instead of lying
/// dormant.
/// </summary>
/// <remarks>
/// <para>
/// The owner announces the move first and asks people to add another way in (a password,
/// the authenticator app, a passkey under the new domain). Then: this statement against the
/// database, then <c>Passkeys:RpId</c> set to the new domain, then a recycle of the API's
/// application pool. No event or notice follows a script: the audit row (reason
/// <c>rp-id-move</c>) is the record.
/// </para>
/// <para>
/// One transaction, and one builder with a golden file, like the break-glass script. It
/// refuses, and changes nothing, on a database without the passkey table.
/// </para>
/// </remarks>
public static class PasskeyRpIdMoveScript
{
    /// <summary>
    /// The statement for <paramref name="rpId"/>, the RP ID being left; null when it is not a
    /// host name an RP ID can be (localhost, or a domain such as example.com).
    /// </summary>
    public static string? Build(string rpId)
    {
        var normalized = rpId.Trim().ToLowerInvariant();
        if (!PasskeyConfiguration.IsHostName(normalized))
        {
            return null;
        }

        // The host-name rule leaves nothing but a-z, 0-9, dots and hyphens: safe in a comment.
        var literal = SqlLiteral.Unicode(normalized);
        return
            $"-- Retires every live passkey bound to the RP ID {normalized}.\n" +
            "-- Before you run this: announce the move, and ask people to add another way to sign in.\n" +
            "-- After it: set Passkeys:RpId to the new domain, then recycle the API's application pool.\n" +
            "-- The count it prints is the number of passkeys retired; their audit rows say rp-id-move.\n" +
            "SET XACT_ABORT ON; BEGIN TRANSACTION;\n" +
            "IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NULL THROW 50002, N'No passkey table: nothing to retire', 1;\n" +
            BreakGlassScript.PasskeyRevocation($"[RpId] = {literal}", PasskeyRemovalReasons.RpIdMove) +
            "COMMIT;";
    }
}
