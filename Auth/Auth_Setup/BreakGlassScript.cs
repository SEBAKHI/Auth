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
/// token issued until now. The script throws, and changes nothing, when no live
/// account has the address.
/// </para>
/// <para>
/// The revocation list reaches the API's memory only when the API starts, so the
/// application pool is recycled after the script. No audit row is written: the
/// owner's own record of running it is the record.
/// </para>
/// <para>
/// One builder and one golden file, so a later factor (passkeys) is removed by
/// adding its statement here.
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

        return
            "SET XACT_ABORT ON; BEGIN TRANSACTION;\n" +
            $"DECLARE @U UNIQUEIDENTIFIER = (SELECT [Id] FROM [dbo].[Users] WHERE [NormalizedEmail] = {normalized} AND [IsDeleted] = 0);\n" +
            "IF @U IS NULL THROW 50001, N'No such user', 1;\n" +
            "DELETE FROM [dbo].[TwoFactorAuth] WHERE [UserId] = @U;\n" +
            "UPDATE [dbo].[Users] SET [IsTwoFactorEnabled] = 0 WHERE [Id] = @U;\n" +
            $"UPDATE [dbo].[RefreshTokens] SET [RevokedAt] = SYSUTCDATETIME(), [ReasonRevoked] = {reason}\n" +
            " WHERE [UserId] = @U AND [RevokedAt] IS NULL;\n" +
            "UPDATE [dbo].[IdpSessions] SET [RevokedAt] = SYSUTCDATETIME() WHERE [UserId] = @U AND [RevokedAt] IS NULL;\n" +
            $"UPDATE [dbo].[UserSessions] SET [EndedAt] = SYSUTCDATETIME(), [EndReason] = {reason}\n" +
            " WHERE [UserId] = @U AND [EndedAt] IS NULL;\n" +
            "INSERT INTO [dbo].[RevokedTokens] ([RevocationType], [RevocationKey], [EffectiveAt], [ExpiresAt])\n" +
            "VALUES (3, LOWER(CONVERT(NVARCHAR(36), @U)), SYSUTCDATETIME(), DATEADD(HOUR, 1, SYSUTCDATETIME()));\n" +
            "COMMIT;";
    }
}
