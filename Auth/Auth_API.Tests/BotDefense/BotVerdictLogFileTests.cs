using System.Text.RegularExpressions;
using Auth.Infrastructure.BotDefense;
using Auth_API.Common.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Auth_API.Tests.BotDefense;

/// <summary>
/// S13 (REVIEW-FIXES 167 F11): the owner's gates read a file only the verdict recorder writes. A
/// request path that carries a percent-encoded line break writes a whole forged verdict line into
/// the main log (the request log writes paths decoded); into this file it cannot.
/// </summary>
/// <remarks>Deliberate break, verified red: the source filter removed.</remarks>
public class BotVerdictLogFileTests
{
    private const string ForgedPath =
        "/x\n2026-11-13 10:00:00.000 +03:00 [INF] Bot defense verdict: action login verdict Passed mode Monitor origin https://accounts.example.com hostname x codes x attempts 1 elapsedMs 1\ntail";

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public void TheVerdictFile_TakesTheRecordersLines_AndNoOtherSource()
    {
        var verdictFile = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteBotVerdictFile(sink => sink.Sink(verdictFile))
            .CreateLogger();

        logger.ForContext<BotDefenseVerdictRecorder>().Information(
            BotDefenseVerdictRecorder.Template, "login", "Passed", "Monitor", "https://accounts.example.com", "accounts.example.com", "-", 1, 140);
        // The request log, with a path that carries a whole verdict line.
        logger.ForContext(Constants.SourceContextPropertyName, "Serilog.AspNetCore.RequestLoggingMiddleware").Information(
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms", "GET", ForgedPath, 404, 0.1);
        // Any other class, whatever its message says.
        logger.ForContext<BotVerdictLogFileTests>().Information(
            BotDefenseVerdictRecorder.Template, "login", "Passed", "Monitor", "https://accounts.example.com", "x", "x", 1, 1);

        var only = verdictFile.Events.Should().ContainSingle("only the recorder writes the verdict file").Which;
        only.Properties[Constants.SourceContextPropertyName].ToString()
            .Should().Be($"\"{typeof(BotDefenseVerdictRecorder).FullName}\"");
        only.RenderMessage().Should().Be(
            "Bot defense verdict: action \"login\" verdict \"Passed\" mode \"Monitor\" origin \"https://accounts.example.com\" hostname \"accounts.example.com\" codes \"-\" attempts 1 elapsedMs 140");
    }

    [Fact]
    public void ThePathIs_TheVerdictFile_KeptAsLongAsTheMainLog()
    {
        BotVerdictLogFile.PathFormat.Should().Be("Logs/bot-verdicts-.log");
        BotVerdictLogFile.RetainedFiles.Should().Be(30);
    }

    [Fact]
    public void Program_AddsTheVerdictFile_InCode_AfterTheConfiguredSinks()
    {
        // After ReadFrom.Configuration, so no deployment's Serilog section can drop it; as a
        // statement line, so a commented-out call reads as missing.
        var program = File.ReadAllText(Path.Combine(SolutionDirectory(), "Auth_API", "Program.cs"));

        var configuration = Regex.Match(program, @"^[ \t]*\.ReadFrom\.Configuration\(builder\.Configuration\)", RegexOptions.Multiline);
        var verdictFile = Regex.Match(program, @"^[ \t]*\.WriteBotVerdictFile\(\)", RegexOptions.Multiline);

        configuration.Success.Should().BeTrue();
        verdictFile.Success.Should().BeTrue("the API's logger writes the verdict file");
        verdictFile.Index.Should().BeGreaterThan(configuration.Index);
    }

    private static string SolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
