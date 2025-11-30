// This is a one-time setup 
using AuthShared_Lib.Services;

Console.WriteLine("=== Secret Setup for UnitedAuth ===");

// Get connection string
Console.WriteLine("Enter your Connection String:");
var connectionString = Console.ReadLine();

// Get JWT Secret
Console.WriteLine("Enter your JWT Secret Key:");
var jwtSecret = Console.ReadLine();

// Store encrypted secrets
SecretManager.StoreSecret("ConnectionString", connectionString!);
SecretManager.StoreSecret("JwtSecretKey", jwtSecret!);

Console.WriteLine($"Secrets encrypted and saved to: {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\UnitedAuth\\secrets.dat");
Console.WriteLine("You can now delete this setup file!");