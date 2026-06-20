using System.Security.Cryptography;
using System.Text;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Breached-password checker backed by the Have I Been Pwned (HIBP) Pwned Passwords range API.
/// Uses k-anonymity: only the first 5 characters of the password's SHA-1 hash are sent; the API
/// returns all hash suffixes sharing that prefix and the match is performed locally, so the
/// plaintext password (and its full hash) never leave this process.
/// <para>
/// The API is free, keyless and unthrottled. The <c>Add-Padding</c> header pads the response to
/// defeat response-size traffic analysis. The base address, timeout and user-agent are configured
/// on the injected typed <see cref="HttpClient"/>.
/// </para>
/// </summary>
public sealed class HibpBreachedPasswordChecker : IBreachedPasswordChecker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HibpBreachedPasswordChecker> _logger;

    public HibpBreachedPasswordChecker(HttpClient httpClient, ILogger<HibpBreachedPasswordChecker> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken)
    {
        // HIBP indexes passwords by SHA-1. This is a lookup key for k-anonymity, NOT password storage
        // (passwords are stored with Argon2id elsewhere), so SHA-1 is appropriate here.
        var sha1Hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = sha1Hash[..5];
        var suffix = sha1Hash[5..];

        using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
        request.Headers.Add("Add-Padding", "true");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Each line is "SUFFIX:COUNT". Padding entries have COUNT 0 and never match a real suffix.
        foreach (var line in body.Split('\n'))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var lineSuffix = line.AsSpan(0, separatorIndex).Trim();
            if (!lineSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var countText = line.AsSpan(separatorIndex + 1).Trim();
            return int.TryParse(countText, out var count) ? count : 1;
        }

        return 0;
    }
}
