using Auth.Application.Common.BotDefense;
using Auth.Application.Configuration;

namespace Auth.Application.Interfaces;

/// <summary>
/// Records each bot-check verdict (S13, BD12): one log line per verdict, which the owner's
/// counting script reads, and the in-process counts the posture reports.
/// </summary>
public interface IBotDefenseVerdictRecorder
{
    /// <param name="action">The surface's action.</param>
    /// <param name="mode">The mode the verdict was reached under.</param>
    /// <param name="verdict">The verdict. Never carries the token or the secret.</param>
    void Record(string action, BotDefenseMode mode, BotDefenseVerdict verdict);
}
