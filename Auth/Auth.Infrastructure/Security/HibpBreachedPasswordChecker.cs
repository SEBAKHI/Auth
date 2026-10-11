using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Breached-password checker backed by the Have I Been Pwned (HIBP) Pwned Passwords range API.
/// Uses k-anonymity: only the first 5 characters of the password's SHA-1 hash are sent; the API
/// returns all hash suffixes sharing that prefix and the match is performed locally, so the
/// plaintext password (and its full hash) never leave this process.
/// <para>
/// The API is free, keyless and unthrottled. The <c>Add-Padding</c> header pads the response to
/// defeat response-size traffic analysis. The base address and user-agent are configured on the
/// injected typed <see cref="HttpClient"/>, whose own timeout is only the ceiling
/// (<see cref="BreachedPasswordCheckSettings.MaxTimeoutMs"/>): each call reads
/// <see cref="BreachedPasswordCheckSettings.TimeoutMs"/> and runs under a deadline of its own, so a
/// value saved in the console applies to the next call.
/// </para>
/// </summary>
public sealed class HibpBreachedPasswordChecker : IBreachedPasswordChecker
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<PasswordSettings> _passwordSettings;

    public HibpBreachedPasswordChecker(HttpClient httpClient, IOptionsMonitor<PasswordSettings> passwordSettings)
    {
        _httpClient = httpClient;
        _passwordSettings = passwordSettings;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The deadline cancels with a plain <see cref="OperationCanceledException"/> while the caller's
    /// token is still live; the evaluator reads that as a timeout. The caller's own cancellation
    /// reaches the request through the linked token and propagates as itself.
    /// </remarks>
    public async Task<int> GetBreachCountAsync(string password, CancellationToken cancellationToken)
    {
        // HIBP indexes passwords by SHA-1. This is a lookup key for k-anonymity, NOT password storage
        // (passwords are stored with Argon2id elsewhere), so SHA-1 is appropriate here.
        var sha1Hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = sha1Hash[..5];
        var suffix = sha1Hash[5..];

        var timeoutMs = Math.Clamp(
            _passwordSettings.CurrentValue.BreachedPasswordCheck.TimeoutMs,
            BreachedPasswordCheckSettings.MinTimeoutMs,
            BreachedPasswordCheckSettings.MaxTimeoutMs);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
        request.Headers.Add("Add-Padding", "true");

        using var response = await _httpClient.SendAsync(request, deadline.Token);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(deadline.Token);

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
