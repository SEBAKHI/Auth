using Auth.Infrastructure.Authentication;

// Bootstraps the seeded administrator. The post-deployment script seeds that account with a NULL
// PasswordHash on purpose, so no deployment of this system ships a credential anyone can look up.
// This tool turns a password the operator chooses into the hash and the UPDATE that sets it.
//
//   dotnet run --project Auth/Auth_Setup -- "<password>" ["<email>"]
//
// With no argument it prompts, so the password never has to appear in shell history.

Console.WriteLine("=== Auth System Setup ===");
Console.WriteLine();

var password = args.Length > 0 ? args[0] : Prompt();
var email = args.Length > 1 ? args[1] : "admin@company.com";

if (string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("No password given. Nothing to do.");
    return 1;
}

// The hash that used to be seeded is public in this repository's history; refuse to restore it.
if (password == "Admin@123!")
{
    Console.Error.WriteLine("That password is published in this repository's history. Choose another.");
    return 1;
}

var hasher = Argon2PasswordHasher.CreateDefault();
var hash = hasher.HashPassword(password);

if (!hasher.VerifyPassword(password, hash))
{
    Console.Error.WriteLine("The hash did not verify against its own input. Refusing to print it.");
    return 1;
}

Console.WriteLine("Run this against the target database, then delete it from your shell history:");
Console.WriteLine();
Console.WriteLine($"UPDATE [dbo].[Users] SET [PasswordHash] = N'{hash}', [MustChangePassword] = 0 WHERE [Email] = '{email}';");
Console.WriteLine();
Console.WriteLine("Until that runs, the account exists and holds super-admin but cannot authenticate:");
Console.WriteLine("a null PasswordHash is rejected by the server, not by the browser.");
return 0;

static string Prompt()
{
    Console.Write("Password for the administrator account: ");
    return Console.ReadLine() ?? string.Empty;
}
