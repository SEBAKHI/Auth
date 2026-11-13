using Auth.Infrastructure.BotDefense;
using Serilog;
using Serilog.Configuration;
using Serilog.Filters;

namespace Auth_API.Common.Logging;

/// <summary>
/// The bot check's verdict file (S13, REVIEW-FIXES 167 F11): <c>Logs/bot-verdicts-.log</c>, daily,
/// 30 files, written by <see cref="BotDefenseVerdictRecorder"/> and by nothing else. The owner's
/// gate counter (<c>Tools/probes/verdict-window.mjs</c>) reads this file.
/// </summary>
/// <remarks>
/// <para>
/// The main log cannot serve the gate: the request log writes every request path there decoded, so
/// an anonymous request whose path carries a percent-encoded line break writes a whole forged
/// verdict line, with a timestamp its sender chose (seen with Kestrel; IIS's http.sys refuses such
/// a URL by default). Here only the recorder's events arrive, so a line is the recorder's or it is
/// not a verdict, and the counter reads any other line as INVALID.
/// </para>
/// <para>
/// Configured in code, after <c>ReadFrom.Configuration</c>, so that no deployment's Serilog
/// section can drop or redirect it. The main log keeps its copy of each line. The minimum levels
/// are the logger's: keep <c>Serilog:MinimumLevel:Default</c> at Information while the gates run.
/// </para>
/// </remarks>
public static class BotVerdictLogFile
{
    /// <summary>The file's path pattern; the sink adds the day (<c>bot-verdicts-YYYYMMDD.log</c>).</summary>
    public const string PathFormat = "Logs/bot-verdicts-.log";

    /// <summary>How many daily files are kept: as the main log.</summary>
    public const int RetainedFiles = 30;

    /// <summary>
    /// Adds the verdict file to <paramref name="configuration"/>. <paramref name="sink"/> replaces
    /// the file, for a test that reads what reaches it.
    /// </summary>
    public static LoggerConfiguration WriteBotVerdictFile(
        this LoggerConfiguration configuration,
        Func<LoggerSinkConfiguration, LoggerConfiguration>? sink = null) =>
        configuration.WriteTo.Logger(verdicts =>
        {
            verdicts.Filter.ByIncludingOnly(Matching.FromSource<BotDefenseVerdictRecorder>());
            if (sink is null)
            {
                verdicts.WriteTo.File(PathFormat, rollingInterval: RollingInterval.Day, retainedFileCountLimit: RetainedFiles);
            }
            else
            {
                sink(verdicts.WriteTo);
            }
        });
}
