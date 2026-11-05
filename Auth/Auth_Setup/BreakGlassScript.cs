using Auth.Application.Configuration;
using Auth.Domain.Constants;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth_Setup;

/// <summary>
/// Builds the owner's emergency statement that removes an account's second factor
/// when no administrator can do it through the product — the only administrator
/// lost both the authenticator and the recovery codes. It works with one
/// administrator, no factor and no API: the owner runs it against the database.
/// </summary>
/// <remarks>
/// <para>
/// One transaction: the factor row goes, the account flag is cleared, and every
/// way the account is signed in is revoked — refresh tokens, SSO sessions, session
/// rows — plus a user-level entry in the revocation list that rejects every access
/// token issued until now, kept for the revocation horizon
/// (<see cref="RevocationHorizon.User"/>), the longest any of them can live. The
/// script throws, and changes nothing, when no live account has the address.
/// </para>
/// <para>
/// The API re-reads the revocation list every five minutes (X04), so the entry
/// reaches every running API process within five minutes, with no recycle. It is
/// the only writer of a revocation outside the API's credential revocation service.
/// The owner's own record of running it is the record of the reset itself.
/// </para>
/// <para>
/// One builder and one golden file. S20 (PK12): every live passkey of the account is
/// revoked in the same transaction, with one audit row each (reason <c>break-glass</c>):
/// no event or notice follows a script, so the audit row is what the account's history
/// keeps. Behind the table's existence, so the script still runs on a database whose
/// passkey schema is not deployed: the IF never runs the statements that name it.
/// </para>
/// </remarks>
public static class BreakGlassScript
{
    /// <summary>The reason recorded on the revoked tokens and ended sessions.</summary>
    public const string Reason = "Break-glass two-factor reset";

    /// <summary>
    /// The statement for the account with this address, matched on the normalized
    /// address the sign-in lookup uses, among accounts that are not deleted.
    /// </summary>
    public static ErrorOr<string> Build(string address)
    {
        var email = Email.Create(address);
        if (email.IsError)
        {
            return email.Errors;
        }

        var normalized = SqlLiteral.Unicode(email.Value.ToNormalized());
        var reason = SqlLiteral.Unicode(Reason);
        var horizonMinutes = (int)RevocationHorizon.User.TotalMinutes;

        // The rule the administrator's reset enforces (X12, LK4 (4)) and this script
        // cannot: comments for the owner who runs it, before the transaction. The
        // statement itself does not change.
        return
            "-- Before you run this: is the second factor hard-locked (too many wrong codes)?\n" +
            "-- If FailedAttempts below is at or above Password:ConsecutiveFailureLimit (100\n" +
            "-- by default), whoever drove it there knew the password. Run this only after\n" +
            "-- the account's owner completed a password reset by email LATER than\n" +
            "-- ModifiedAt (LastPasswordChangeUtc > ModifiedAt), and you confirmed it is the\n" +
            "-- owner outside email. The check reads and changes nothing:\n" +
            $"-- SELECT t.[FailedAttempts], t.[ModifiedAt], u.[LastPasswordChangeUtc] FROM [dbo].[Users] u LEFT JOIN [dbo].[TwoFactorAuth] t ON t.[UserId] = u.[Id] WHERE u.[NormalizedEmail] = {normalized} AND u.[IsDeleted] = 0;\n" +
            "SET XACT_ABORT ON; BEGIN TRANSACTION;\n" +
            $"DECLARE @U UNIQUEIDENTIFIER = (SELECT [Id] FROM [dbo].[Users] WHERE [NormalizedEmail] = {normalized} AND [IsDeleted] = 0);\n" +
            "IF @U IS NULL THROW 50001, N'No such user', 1;\n" +
            "DELETE FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @U;\n" +
            "UPDATE [dbo].[Users] SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @U;\n" +
            PasskeyRevocation("[UserId] = @U", PasskeyRemovalReasons.BreakGlass) +
            $"UPDATE [dbo].[RefreshTokens] SET [RevokedAt] = SYSUTCDATETIME(), [ReasonRevoked] = {reason}\n" +
            " WHERE [UserId] = @U AND [RevokedAt] IS NULL;\n" +
            "UPDATE [dbo].[IdpSessions] SET [RevokedAt] = SYSUTCDATETIME() WHERE [UserId] = @U AND [RevokedAt] IS NULL;\n" +
            $"UPDATE [dbo].[UserSessions] SET [EndedAt] = SYSUTCDATETIME(), [EndReason] = {reason}\n" +
            " WHERE [UserId] = @U AND [EndedAt] IS NULL;\n" +
            "INSERT INTO [dbo].[RevokedTokens] ([RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt])\n" +
            $"VALUES (3, LOWER(CONVERT(NVARCHAR(36), @U)), SYSUTCDATETIME(), DATEADD(MINUTE, {horizonMinutes}, SYSUTCDATETIME()));\n" +
            "COMMIT;";
    }

    /// <summary>
    /// The statements that revoke the live passkeys a predicate selects, with one audit row
    /// each, as the API's audit handler writes them (action <c>passkey.removed</c>, details
    /// built by <c>FOR JSON</c>, never by concatenation). Behind the table's existence. Shared
    /// with <see cref="PasskeyRpIdMoveScript"/>.
    /// </summary>
    /// <param name="where">The predicate over <c>UserPasskeys</c>, besides "live".</param>
    /// <param name="reason">The reason recorded on the rows and in the audit details.</param>
    internal static string PasskeyRevocation(string where, string reason)
    {
        var reasonLiteral = SqlLiteral.Unicode(reason);
        return
            "IF OBJECT_ID(N'[dbo].[UserPasskeys]', N'U') IS NOT NULL\n" +
            "BEGIN\n" +
            "    DECLARE @RevokedPasskeys TABLE ([Id] UNIQUEIDENTIFIER, [UserId] UNIQUEIDENTIFIER, [Name] NVARCHAR(128));\n" +
            $"    UPDATE [dbo].[UserPasskeys] SET [RevokedAt] = SYSUTCDATETIME(), [RevokedReason] = {reasonLiteral}, [RevokedBy] = NULL\n" +
            "     OUTPUT inserted.[Id], inserted.[UserId], inserted.[Name] INTO @RevokedPasskeys\n" +
            $"     WHERE {where} AND [RevokedAt] IS NULL;\n" +
            "    INSERT INTO [dbo].[AuditLogs] ([UserId], [Action], [EntityType], [EntityId], [Details], [ActionType], [IsSuccess])\n" +
            $"    SELECT r.[UserId], {SqlLiteral.Unicode(AuditActions.PasskeyRemoved)}, N'UserPasskey', r.[Id],\n" +
            $"           (SELECT r.[Name] AS [name], {reasonLiteral} AS [reason] FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),\n" +
            $"           {SqlLiteral.Unicode(AuditActionTypes.Security)}, 1\n" +
            "    FROM @RevokedPasskeys r;\n" +
            "    SELECT COUNT(*) AS [PasskeysRevoked] FROM @RevokedPasskeys;\n" +
            "END;\n";
    }
}
