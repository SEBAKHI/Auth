using System.Security.Cryptography;
using System.Text;

namespace AuthShared_Lib.Services
{
    public class SecretManager
    {
        private static readonly string SecretFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UnitedAuth",
            "secrets.dat"
        );

        public static void StoreSecret(string key, string value)
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                null,
                DataProtectionScope.CurrentUser
            );
            
            var directory = Path.GetDirectoryName(SecretFilePath);
            Directory.CreateDirectory(directory!);
            
            var lines = File.Exists(SecretFilePath) ? File.ReadAllLines(SecretFilePath).ToList() : new List<string>();
            lines.RemoveAll(l => l.StartsWith($"{key}="));
            lines.Add($"{key}={Convert.ToBase64String(encrypted)}");
            File.WriteAllLines(SecretFilePath, lines);
        }

        public static string? GetSecret(string key)
        {
            if (!File.Exists(SecretFilePath)) return null;
            
            var line = File.ReadAllLines(SecretFilePath).FirstOrDefault(l => l.StartsWith($"{key}="));
            if (line == null) return null;
            
            var encryptedBase64 = line.Substring(key.Length + 1);
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}