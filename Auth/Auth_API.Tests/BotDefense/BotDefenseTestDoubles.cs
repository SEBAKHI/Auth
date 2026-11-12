using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Infrastructure.BotDefense;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace Auth_API.Tests.BotDefense;

/// <summary>Stand-ins for the bot check's collaborators (S13). No network: CI never calls Cloudflare.</summary>
internal static class BotDefenseTestDoubles
{
    /// <summary>A real-looking (not testing) pair of keys.</summary>
    public const string SiteKey = "0x4AAAAAAAsitekeyForTests";
    public const string Secret = "0x4AAAAAAAsecretForTestsOnly";

    public static BotDefenseSettings Settings(
        BotDefenseMode mode = BotDefenseMode.Enforce,
        BotDefenseFailMode failMode = BotDefenseFailMode.PerSurface,
        string siteKey = SiteKey,
        string secret = Secret) =>
        new() { Mode = mode, FailMode = failMode, SiteKey = siteKey, TurnstileSecret = secret };

    public static IdentityProviderSettings FirstParty(params string[] origins) =>
        new() { FirstPartySpaOrigins = origins };

    public static IConfiguration Cors(params string[] origins) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(origins.Select((origin, index) =>
                new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{index}", origin)))
            .Build();

    public static TurnstileBotDefenseVerifier Verifier(
        BotDefenseSettings settings,
        ITurnstileSiteverifyClient client,
        bool production = false,
        IdentityProviderSettings? firstParty = null,
        IConfiguration? cors = null,
        TimeProvider? timeProvider = null) =>
        new(
            TestHelpers.CreateOptions(settings),
            TestHelpers.CreateOptions(firstParty ?? FirstParty("https://accounts.example.com", "https://console.example.com")),
            cors ?? Cors("https://accounts.example.com", "https://console.example.com"),
            client,
            new FakeEnvironment(production),
            timeProvider ?? TimeProvider.System);

    /// <summary>Cloudflare's answer accepting a token made for <paramref name="action"/> on <paramref name="hostname"/>.</summary>
    public static SiteverifyResult Accepted(
        string action,
        string hostname = "accounts.example.com",
        DateTimeOffset? solvedAt = null,
        DateTimeOffset? respondedAt = null) =>
        new(new SiteverifyAnswer(true, [], hostname, action, solvedAt ?? DateTimeOffset.UtcNow.AddSeconds(-5), respondedAt), null, 1, 40);

    public static SiteverifyResult Refused(params string[] codes) =>
        new(new SiteverifyAnswer(false, codes, null, null, null), null, 1, 40);

    public static SiteverifyResult NoAnswer(string failure = "timeout") => new(null, failure, 1, 2000);
}

internal sealed class FakeEnvironment(bool production, bool development = false) : IEnvironmentInfo
{
    public bool IsDevelopment => development;

    public bool IsProduction => production;
}

/// <summary>Answers each verification with the next queued result; records what it was asked.</summary>
internal sealed class FakeSiteverifyClient : ITurnstileSiteverifyClient
{
    private readonly Queue<SiteverifyResult> _results = new();

    public List<(string Secret, string Token)> Calls { get; } = [];

    public FakeSiteverifyClient Answers(params SiteverifyResult[] results)
    {
        foreach (var result in results)
        {
            _results.Enqueue(result);
        }

        return this;
    }

    public Task<SiteverifyResult> VerifyAsync(string secret, string token, CancellationToken cancellationToken)
    {
        Calls.Add((secret, token));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : BotDefenseTestDoubles.NoAnswer("no-answer-queued"));
    }
}

internal sealed class RecordingVerdicts : IBotDefenseVerdictRecorder
{
    public List<(string Action, BotDefenseMode Mode, BotDefenseVerdict Verdict)> Recorded { get; } = [];

    public void Record(string action, BotDefenseMode mode, BotDefenseVerdict verdict) => Recorded.Add((action, mode, verdict));
}

/// <summary>An allowance with a fixed number of permits, whatever the address.</summary>
internal sealed class CountingAllowance(int permits) : IBotFallbackLimiter
{
    private int _left = permits;

    public List<string?> Asked { get; } = [];

    public bool TryAcquire(string? clientIp)
    {
        Asked.Add(clientIp);
        return _left-- > 0;
    }
}

/// <summary>A clock that moves only when told to.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
