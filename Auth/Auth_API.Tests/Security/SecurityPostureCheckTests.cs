using Auth.Application.Interfaces;
using Auth.Application.Security;
using Auth.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.Security;

/// <summary>
/// S09 (T10): the posture check is a background service that may never cost startup or the host
/// anything: a slow contributor does not hold the host's start, and a throwing one is an Error
/// line, not a stopped host (the default BackgroundServiceExceptionBehavior stops the host).
/// </summary>
public class SecurityPostureCheckTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    private sealed class StubContributor(string name, Func<CancellationToken, Task<SecurityPosture>> evaluate)
        : ISecurityPostureContributor
    {
        public string Name => name;

        public Task<SecurityPosture> EvaluateAsync(CancellationToken cancellationToken) => evaluate(cancellationToken);
    }

    private static SecurityPosture Posture(string message) => new(
        SecurityPostureLevel.Ok,
        [],
        [new SecurityPostureLine(LogLevel.Information, "{Message}", [message])],
        message);

    private static SecurityPostureCheck Create(CapturingLogger<SecurityPostureCheck> logger, params ISecurityPostureContributor[] contributors)
        => new(contributors, EmptyConfiguration, TimeProvider.System, logger, TimeSpan.FromMilliseconds(20));

    [Fact]
    public async Task StartAsync_ReturnsBeforeASlowContributor()
    {
        var release = new TaskCompletionSource<SecurityPosture>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new CapturingLogger<SecurityPostureCheck>();
        using var check = Create(logger, new StubContributor("slow", _ => release.Task));

        await check.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        logger.Entries.Should().BeEmpty("the contributor has not answered yet");
        release.SetResult(Posture("slow answered"));
        await WaitUntil(() => logger.Entries.Count == 1);
        logger.Entries[0].Message.Should().Be("slow answered");

        await check.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContributorException_IsOneErrorLine_AndNeverReachesTheHost()
    {
        var logger = new CapturingLogger<SecurityPostureCheck>();
        using var check = Create(
            logger,
            new StubContributor("broken", _ => throw new InvalidOperationException("contributor bug")),
            new StubContributor("healthy", _ => Task.FromResult(Posture("healthy answered"))));

        await check.StartAsync(CancellationToken.None);
        await WaitUntil(() => logger.Entries.Count == 2);

        var error = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which;
        error.Message.Should().Be("Security posture contributor broken failed");
        error.Exception.Should().BeOfType<InvalidOperationException>();
        logger.Entries.Should().Contain(e => e.Message == "healthy answered");

        check.ExecuteTask.Should().NotBeNull();
        check.ExecuteTask!.IsFaulted.Should().BeFalse("an exception must never reach the host");
        check.ExecuteTask.IsCompleted.Should().BeFalse("the check keeps waiting for the next reload");

        var stop = () => check.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();
    }

    /// <summary>
    /// A contributor that failed writes its lines again once it recovers, even when its state is
    /// the one written before the failure: the Error line came in between.
    /// </summary>
    [Fact]
    public async Task ContributorRecovery_WritesItsLinesAgain()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var calls = 0;
        var logger = new CapturingLogger<SecurityPostureCheck>();
        using var check = new SecurityPostureCheck(
            [
                new StubContributor("flaky", _ => Interlocked.Increment(ref calls) == 2
                    ? throw new InvalidOperationException("transient")
                    : Task.FromResult(Posture("flaky answered"))),
            ],
            configuration, TimeProvider.System, logger, TimeSpan.FromMilliseconds(20));

        await check.StartAsync(CancellationToken.None);
        await WaitUntil(() => logger.Entries.Count == 1);
        configuration.Reload();
        await WaitUntil(() => logger.Entries.Count == 2);
        configuration.Reload();
        await WaitUntil(() => logger.Entries.Count == 3);

        logger.Entries.Select(e => e.Level).Should().Equal(LogLevel.Information, LogLevel.Error, LogLevel.Information);

        await check.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the posture check should have evaluated by now");
            await Task.Delay(10);
        }
    }
}
