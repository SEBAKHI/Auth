namespace Auth.Application.Common;

/// <summary>
/// Masks email addresses for safe display in responses and logs.
/// </summary>
public static class EmailMasking
{
    /// <summary>
    /// Masks an email address for safe display (e.g., a****n@example.com).
    /// </summary>
    public static string Mask(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex < 1) return email;

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        // A one-character local part used to pass through in the clear, so
        // a@example.com reached every log line and audit row unmasked.
        if (localPart.Length == 1)
            return $"*{domain}";

        if (localPart.Length <= 2)
            return $"{localPart[0]}***{domain}";

        return $"{localPart[0]}{new string('*', Math.Min(localPart.Length - 2, 4))}{localPart[^1]}{domain}";
    }
}
