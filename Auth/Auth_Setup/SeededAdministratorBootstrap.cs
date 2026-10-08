using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth_Setup;

/// <summary>
/// Builds the statement that bootstraps the seeded administrator on a new
/// deployment: the operator's own address, marked confirmed on their word, and the
/// password hash, in one UPDATE keyed on the seeded account's Id.
/// </summary>
/// <remarks>
/// <para>
/// The seed creates the only super-administrator with a placeholder address at a
/// domain that belongs to someone else, and no password. Keyed on the Id, never on
/// the address: the statement is what sets the address, and a deployment where it
/// already ran no longer has the placeholder.
/// </para>
/// <para>
/// The address is confirmed on the operator's word because a password sign-in
/// refuses an unconfirmed address, and on a new server email may not work yet: the
/// only administrator could not sign in at all. What proves the mailbox instead is
/// the emailed code that binding a first second factor needs — so a wrong address
/// fails there, before go-live, and the operator runs the tool again.
/// </para>
/// </remarks>
public static class SeededAdministratorBootstrap
{
    /// <summary>The Id the post-deployment script seeds the administrator with.</summary>
    public static readonly Guid SeededAdministratorId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    /// <summary>The seed's placeholder address — the one address never accepted.</summary>
    public const string PlaceholderAddress = "admin@company.com";

    /// <summary>
    /// Validates the operator's address with the domain's own rules: trimmed, at
    /// most 254 characters, the address pattern, lower-cased. The placeholder is
    /// refused in any letter case.
    /// </summary>
    public static ErrorOr<Email> ValidateAddress(string address)
    {
        var email = Email.Create(address);
        if (email.IsError)
        {
            return email.Errors;
        }

        if (string.Equals(email.Value.Value, PlaceholderAddress, StringComparison.Ordinal))
        {
            return Error.Validation(
                code: "Setup.PlaceholderAddress",
                description: $"{PlaceholderAddress} is the seed's placeholder at someone else's domain. Give an address whose mailbox you read.");
        }

        return email.Value;
    }

    /// <summary>
    /// The statement: one UPDATE of the five columns, keyed on the seeded Id and on
    /// the row not being deleted, followed by a guard that fails loudly when it
    /// changed anything but exactly one row.
    /// </summary>
    /// <param name="email">The validated address.</param>
    /// <param name="passwordHash">The Argon2id hash of the chosen password.</param>
    public static string Build(Email email, string passwordHash)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        // ToNormalized is the upper-casing the sign-in lookup compares against —
        // never SQL UPPER(), which can differ outside ASCII.
        return
            "UPDATE [dbo].[Users]\n" +
            $"SET [Email] = {SqlLiteral.Unicode(email.Value)},\n" +
            $"    [NormalizedEmail] = {SqlLiteral.Unicode(email.ToNormalized())},\n" +
            "    [IsEmailConfirmed] = 1,\n" +
            $"    [PasswordHash] = {SqlLiteral.Unicode(passwordHash)},\n" +
            "    [MustChangePassword] = 0\n" +
            $"WHERE [Id] = '{SeededAdministratorId:D}'\n" +
            "  AND [IsDeleted] = 0;\n" +
            "IF @@ROWCOUNT <> 1 THROW 50000, N'The seeded administrator is missing or deleted; nothing was changed.', 1;";
    }
}
