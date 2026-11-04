using System.Collections.Concurrent;
using System.Threading.Channels;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Revocation;

/// <summary>
/// X04, RV2 (e) and (f): the revocation list is loaded before the host goes on to start the
/// server, within a budget; a failed load is Critical and retried; once loaded the whole list
/// is read again periodically; and what is queued at shutdown is written under a budget of its
/// own. Before X04 the load ran inside ExecuteAsync (the server could answer first from an
/// empty list), a failure was one Error line, and the queue was dropped at shutdown.
/// </summary>
/// <remarks>
/// Deliberate breaks, each seen red: the load moved back into ExecuteAsync; the stopping token
/// used for the drain; the rehydrated line written only when the count is above zero.
/// </remarks>
public sealed class TokenRevocationBackgroundServiceLifecycleTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Channel<TokenRevocation> _queue = Channel.CreateUnbounded<TokenRevocation>();
    private readonly TokenBlacklistService _blacklist;
    private readonly RevocationRehydrationStatus _status = new();
    private readonly FakeStore _store = new();
    private readonly CapturingLogger<TokenRevocationBackgroundService> _logger = new();
    private readonly ServiceProvider _services;

    public TokenRevocationBackgroundServiceLifecycleTests()
    {
        _blacklist = new TokenBlacklistService(_queue.Writer, TimeProvider.System, NullLogger<TokenBlacklistService>.Instance);
        _services = new ServiceCollection().AddSingleton<IRevokedTokenStore>(_store).BuildServiceProvider();
    }

    public void Dispose()
    {
        _blacklist.Dispose();
        _services.Dispose();
    }

    private TokenRevocationBackgroundService NewService(
        TimeSpan? loadBudget = null, TimeSpan? retry = null, TimeSpan? reread = null, TimeSpan? drain = null) =>
        new(
            _queue.Reader,
            _blacklist,
            _status,
            _services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            _logger,
            new TokenRevocationBackgroundService.Schedule(
                LoadBudget: loadBudget ?? TimeSpan.FromSeconds(5),
                RetryInterval: retry ?? TimeSpan.FromHours(1),
                RereadInterval: reread ?? TimeSpan.FromHours(1),
                PurgeInterval: TimeSpan.FromHours(1),
                DrainBudget: drain ?? TimeSpan.FromSeconds(5)));

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, because);
            await Task.Delay(20);
        }
    }

    private static TokenRevocation UserRevocation(Guid userId, DateTime at) =>
        new(RevocationType.User, userId.ToString(), at, at.AddHours(1));

    private static TokenRevocation Session(string key) =>
        new(RevocationType.Session, key, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

    private IEnumerable<CapturedLogEntry> Lines(string eventCode) =>
        _logger.Entries.Where(entry => entry.Values.Contains(eventCode));

    [Fact]
    public async Task Start_DoesNotReturnUntilTheListIsLoaded()
    {
        var userId = Guid.NewGuid();
        var revokedAt = DateTime.UtcNow.AddMinutes(-1);
        var gate = new TaskCompletionSource<IReadOnlyList<TokenRevocation>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.OnGetActive = _ => gate.Task;
        using var service = NewService();

        var start = service.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        start.IsCompleted.Should().BeFalse("the host must not go on to start the server before the load");
        _status.IsLoaded.Should().BeFalse();

        gate.SetResult([UserRevocation(userId, revokedAt)]);
        await start.WaitAsync(Wait);

        _status.IsLoaded.Should().BeTrue();
        _blacklist.AreUserTokensBlacklisted(userId, revokedAt.AddSeconds(-1)).Should().BeTrue();
        var line = Lines(RevocationSignalCodes.Rehydrated).Should().ContainSingle().Subject;
        line.Level.Should().Be(LogLevel.Information);
        line.Message.Should().StartWith("revocation.rehydrated: loaded 1 token revocations");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AnEmptyList_StillWritesTheRehydratedLine_WithItsCount()
    {
        using var service = NewService();

        await service.StartAsync(CancellationToken.None);

        _status.IsLoaded.Should().BeTrue();
        Lines(RevocationSignalCodes.Rehydrated).Should().ContainSingle()
            .Which.Message.Should().StartWith("revocation.rehydrated: loaded 0 token revocations",
                "the deployment's check after a restart reads this line, a zero included");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AFailedStart_IsCritical_IsRetried_AndOnceLoadedTheListIsReadAgain()
    {
        var userId = Guid.NewGuid();
        var calls = 0;
        _store.OnGetActive = _ => Interlocked.Increment(ref calls) == 1
            ? Task.FromException<IReadOnlyList<TokenRevocation>>(new InvalidOperationException("database unreachable"))
            : Task.FromResult<IReadOnlyList<TokenRevocation>>([UserRevocation(userId, DateTime.UtcNow)]);
        using var service = NewService(retry: TimeSpan.FromMilliseconds(100), reread: TimeSpan.FromMilliseconds(100));

        await service.StartAsync(CancellationToken.None);

        _status.IsLoaded.Should().BeFalse("the server starts, but the list is not loaded");
        _status.FailedAttempts.Should().Be(1);
        var failure = Lines(RevocationSignalCodes.RehydrationFailed).Should().ContainSingle().Subject;
        failure.Level.Should().Be(LogLevel.Critical);
        failure.Exception.Should().BeOfType<InvalidOperationException>();

        await Eventually(() => _status.IsLoaded, "the retry loads the list");
        Lines(RevocationSignalCodes.Rehydrated).Should().ContainSingle()
            .Which.Message.Should().Contain("(attempt 2)");
        await Eventually(() => Volatile.Read(ref calls) >= 3, "the loaded list is read again periodically");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ALoadThatNeverAnswers_FailsWithinTheBudget_EvenIfTheStoreIgnoresTheToken()
    {
        _store.OnGetActive = _ => new TaskCompletionSource<IReadOnlyList<TokenRevocation>>().Task;
        using var service = NewService(loadBudget: TimeSpan.FromMilliseconds(300));

        await service.StartAsync(CancellationToken.None).WaitAsync(Wait);

        _status.IsLoaded.Should().BeFalse();
        Lines(RevocationSignalCodes.RehydrationFailed).Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Critical);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AtShutdown_WhatIsQueued_IsWrittenUnderABudgetOfItsOwn()
    {
        // The first write hangs until the stop cancels it; two more wait behind it. The store
        // refuses any write under a cancelled token, as SqlClient does.
        _store.HangFirstWriteUntilCancelled = true;
        using var service = NewService();
        await service.StartAsync(CancellationToken.None);

        _queue.Writer.TryWrite(Session("sid-1"));
        await _store.FirstWriteStarted.Task.WaitAsync(Wait);
        _queue.Writer.TryWrite(Session("sid-2"));
        _queue.Writer.TryWrite(Session("sid-3"));

        await service.StopAsync(CancellationToken.None).WaitAsync(Wait);

        _store.Added.Select(r => r.Key).Should().BeEquivalentTo(["sid-1", "sid-2", "sid-3"],
            "the write the stop interrupted and the two queued behind it reach durable storage");
        _logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Critical);
    }

    [Fact]
    public async Task AtShutdown_WhatTheBudgetCannotWrite_IsCountedInOneCriticalLine()
    {
        _store.HangEveryWriteUntilCancelled = true;
        using var service = NewService(drain: TimeSpan.FromMilliseconds(200));
        await service.StartAsync(CancellationToken.None);

        _queue.Writer.TryWrite(Session("sid-1"));
        await _store.FirstWriteStarted.Task.WaitAsync(Wait);
        _queue.Writer.TryWrite(Session("sid-2"));
        _queue.Writer.TryWrite(Session("sid-3"));

        await service.StopAsync(CancellationToken.None).WaitAsync(Wait);

        _store.Added.Should().BeEmpty();
        var critical = _logger.Entries.Where(entry => entry.Level == LogLevel.Critical).Should().ContainSingle().Subject;
        critical.Message.Should().StartWith("3 token revocations queued at shutdown were not written");
    }

    private sealed class FakeStore : IRevokedTokenStore
    {
        private int _writes;

        public Func<CancellationToken, Task<IReadOnlyList<TokenRevocation>>> OnGetActive { get; set; } =
            _ => Task.FromResult<IReadOnlyList<TokenRevocation>>([]);

        public bool HangFirstWriteUntilCancelled { get; set; }

        public bool HangEveryWriteUntilCancelled { get; set; }

        public TaskCompletionSource FirstWriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<TokenRevocation> Added { get; } = new();

        public async Task AddAsync(TokenRevocation revocation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = Interlocked.Increment(ref _writes) == 1;
            if (first)
            {
                FirstWriteStarted.TrySetResult();
            }

            if (HangEveryWriteUntilCancelled || (first && HangFirstWriteUntilCancelled))
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Added.Enqueue(revocation);
        }

        public Task<IReadOnlyList<TokenRevocation>> GetActiveAsync(DateTime now, CancellationToken cancellationToken) =>
            OnGetActive(cancellationToken);

        public Task PurgeExpiredAsync(DateTime olderThan, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
