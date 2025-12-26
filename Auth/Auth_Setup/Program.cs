using Auth_Lib.Infrastructure.Authentication;

Console.WriteLine("=== Auth System Setup ===");
Console.WriteLine();

// Use the same settings as appsettings.json
var hasher = Argon2PasswordHasher.CreateDefault();

// Generate hash for Admin@123!
var password = "Admin@123!";
var hash = hasher.HashPassword(password);

Console.WriteLine($"Password: {password}");
Console.WriteLine($"Hash:     {hash}");
Console.WriteLine();
Console.WriteLine("Run this SQL to update the admin user:");
Console.WriteLine();
Console.WriteLine($"UPDATE [dbo].[Users] SET [PasswordHash] = N'{hash}' WHERE [Email] = 'admin@company.com';");
Console.WriteLine();

// Verify it works
var isValid = hasher.VerifyPassword(password, hash);
Console.WriteLine($"Verification: {(isValid ? "SUCCESS" : "FAILED")}");
