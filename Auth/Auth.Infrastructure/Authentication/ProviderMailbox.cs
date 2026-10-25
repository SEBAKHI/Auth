namespace Auth.Infrastructure.Authentication;

/// <summary>
/// Reads whether an identity provider is the authority of the mailbox it names
/// (D-243, OI-243 T4): the one input that lets a provider sign-in stand in for an
/// emailed code. Each provider keeps its own list of the domains it hosts.
/// </summary>
internal static class ProviderMailbox
{
    /// <summary>
    /// The domain of <paramref name="email"/>, normalized as every address is (trimmed,
    /// lower case); null when there is none.
    /// </summary>
    public static string? DomainOf(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (email is null || at < 0)
        {
            return null;
        }

        var domain = email[(at + 1)..].Trim().ToLowerInvariant();
        return domain.Length == 0 ? null : domain;
    }

    /// <summary>
    /// Whether a verified <paramref name="email"/> lies in one of
    /// <paramref name="hostedDomains"/>, the domains the provider runs the mailboxes of.
    /// </summary>
    public static bool IsHosted(bool emailVerified, string? email, IReadOnlyCollection<string> hostedDomains) =>
        emailVerified && DomainOf(email) is { } domain && hostedDomains.Contains(domain);
}
