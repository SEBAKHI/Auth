using Auth_API.Common.FirstParty;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Auth_API.Tests.Authentication.FirstParty;

/// <summary>
/// The boot line Program.cs writes about the first-party list: a Warning either way,
/// so it survives a production log level of Warning.
/// </summary>
public class FirstPartyOriginsStartupReportTests
{
    private sealed class Collect : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static List<LogEvent> Report(params (string Key, string? Value)[] values)
    {
        var sink = new Collect();
        using var logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Sink(sink).CreateLogger();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

        FirstPartyOriginsStartupReport.Write(logger, configuration);
        return sink.Events;
    }

    [Fact]
    public void AnEmptyList_WritesOneWarning_ThatNamesTheGap()
    {
        var events = Report();

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Warning);
        events[0].RenderMessage().Should().StartWith("boot.first-party-origins").And.Contain("is empty");
    }

    [Fact]
    public void AnOriginOnAnotherSiteThanTheApi_IsWarnedAbout()
    {
        var events = Report(
            ("IdentityProvider:PublicBaseUrl", "https://auth-sandbox.sebakhi.com"),
            ("IdentityProvider:FirstPartySpaOrigins:0", "https://console-sandbox.sebakhi.com"),
            ("IdentityProvider:FirstPartySpaOrigins:1", "https://accounts.other-site.net"));

        var messages = events.Select(e => e.RenderMessage()).ToList();
        messages.Should().ContainSingle(message => message.Contains("does not look same-site", StringComparison.Ordinal))
            .Which.Should().Contain("accounts.other-site.net");
    }

    [Fact]
    public void AFilledList_WritesOneWarning_ListingTheOrigins()
    {
        var events = Report(
            ("IdentityProvider:FirstPartySpaOrigins:0", "https://console.example.com"),
            ("IdentityProvider:FirstPartySpaOrigins:1", ""), // a shrink tombstone
            ("IdentityProvider:FirstPartySpaOrigins:2", "https://accounts.example.com"),
            ("IdentityProvider:SpaRefreshCookieEnabled", "true"));

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Warning);
        events[0].RenderMessage().Should()
            .Contain("https://console.example.com, https://accounts.example.com")
            .And.Contain("True");
    }
}
