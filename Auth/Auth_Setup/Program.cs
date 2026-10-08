using Auth.Infrastructure.Authentication;
using Auth_Setup;

// The operator's tool for the two things only the database owner can do. It prints
// SQL; it never connects to a database.
//
//   dotnet run --project Auth/Auth_Setup -- --email <your address> ["<password>"]
//       Bootstraps the seeded administrator. The post-deployment script seeds that
//       account with a placeholder address and a NULL PasswordHash on purpose, so no
//       deployment of this system ships a credential anyone can look up. With no
//       password argument it prompts, so the password never has to appear in shell
//       history.
//
//   dotnet run --project Auth/Auth_Setup -- --reset-two-factor <address>
//       The emergency way back for an account whose authenticator and recovery codes
//       are both lost when no administrator can reset them through the console.

Console.WriteLine("=== Auth System Setup ===");
Console.WriteLine();

var request = SetupCommandLine.Parse(args);
if (request.IsRefused)
{
    Console.Error.WriteLine(request.Error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(SetupCommandLine.Usage);
    return 1;
}

return request.Mode == SetupMode.ResetTwoFactor
    ? PrintBreakGlass(request.Address!)
    : BootstrapAdministrator(request.Address!, request.Password);

static int BootstrapAdministrator(string address, string? givenPassword)
{
    var email = SeededAdministratorBootstrap.ValidateAddress(address);
    if (email.IsError)
    {
        Console.Error.WriteLine($"That address cannot be used: {email.FirstError.Description}");
        return 1;
    }

    var password = givenPassword ?? Prompt();

    if (string.IsNullOrWhiteSpace(password))
    {
        Console.Error.WriteLine("No password given. Nothing to do.");
        return 1;
    }

    if (!SetupCommandLine.IsAcceptablePassword(password))
    {
        Console.Error.WriteLine("A password may not start with \"--\": that is how a mistyped option arrives in its place. Choose another.");
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

    Console.WriteLine($"This statement makes {email.Value.Value} the seeded administrator's address and sets its password.");
    Console.WriteLine("The address is marked confirmed on your word, so it must be a mailbox you read: the code that");
    Console.WriteLine("turns on two-step verification for this account, and its security notices, will be sent there.");
    Console.WriteLine("If another account already holds the address, the statement fails on UQ_Users_NormalizedEmail");
    Console.WriteLine("(deleted accounts included) and changes nothing: choose another address.");
    Console.WriteLine();
    Console.WriteLine("Run this against the target database, then delete it from your shell history:");
    Console.WriteLine();
    Console.WriteLine(SeededAdministratorBootstrap.Build(email.Value, hash));
    Console.WriteLine();
    Console.WriteLine("It must report one row. Until it runs, the account exists and holds super-admin but cannot");
    Console.WriteLine("authenticate: a null PasswordHash is rejected by the server, not by the browser.");
    return 0;
}

static int PrintBreakGlass(string address)
{
    var script = BreakGlassScript.Build(address);
    if (script.IsError)
    {
        Console.Error.WriteLine($"That address cannot be used: {script.FirstError.Description}");
        return 1;
    }

    Console.WriteLine("EMERGENCY USE ONLY: when no administrator can reset this account's two-step verification in the");
    Console.WriteLine("console. Back up the database first. Run this against the target database in one batch:");
    Console.WriteLine();
    Console.WriteLine(script.Value);
    Console.WriteLine();
    Console.WriteLine("Then recycle the API's application pool. The statement revokes every token and session of the");
    Console.WriteLine("account, but the API learns of the revocation only when it starts; until the recycle an access");
    Console.WriteLine("token issued before it keeps working, up to its lifetime. The recycle overlaps: the new worker");
    Console.WriteLine("takes over when the old one exits.");
    Console.WriteLine();
    Console.WriteLine("Know before you run it:");
    Console.WriteLine("  - No audit row is written: keep your own record of when and why you ran it.");
    Console.WriteLine("  - The next person to sign in to the account and set up two-step verification owns it. While");
    Console.WriteLine("    email is on, setting it up first needs a code sent to the account's address.");
    Console.WriteLine("  - \"No such user\" means no live account has that address, and nothing changed.");
    return 0;
}

static string Prompt()
{
    Console.Write("Password for the administrator account: ");
    return Console.ReadLine() ?? string.Empty;
}
