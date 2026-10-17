using System.Threading.RateLimiting;

namespace Auth_API.Common.RateLimiting;

/// <summary>A fixed-window allowance: so many requests per so many seconds.</summary>
/// <param name="PermitLimit">The requests allowed in one window.</param>
/// <param name="WindowSeconds">The window's length.</param>
public readonly record struct FixedWindowBudget(int PermitLimit, int WindowSeconds)
{
    /// <summary>The limiter options for this allowance: no queue, a refusal is immediate.</summary>
    public FixedWindowRateLimiterOptions Options() => new()
    {
        PermitLimit = PermitLimit,
        Window = TimeSpan.FromSeconds(WindowSeconds),
        QueueLimit = 0
    };
}

/// <summary>
/// The budget a token request counts in (OI-178), decided by
/// <see cref="Middleware.TokenClientClassificationMiddleware"/> before the "token" policy
/// partitions the request.
/// </summary>
/// <remarks>
/// <para>
/// A request whose client assertion verified counts on its application's budget: a server
/// renewing its people's tokens from one address is no longer held to that address's budget,
/// and nobody without the application's key can spend its budget, because only a verified
/// assertion is counted there. Every other request counts on its address, as before: no
/// assertion, an assertion that did not verify, or one whose jti was already spent.
/// </para>
/// <para>
/// A request is refused at once when its address has spent its allowance of assertions that
/// did not verify: verification reads the database, and this bounds how much of it one address
/// can cause.
/// </para>
/// </remarks>
public abstract class TokenRequestClass
{
    private const string ItemKey = "Auth_API.TokenRequestClass";

    /// <summary>Counted on its client address, as every token request was before OI-178.</summary>
    public static TokenRequestClass ByAddress { get; } = new Address();

    /// <summary>Refused at once, without verifying anything.</summary>
    public static TokenRequestClass Refused { get; } = new RefusedAtOnce();

    /// <summary>Counted on the budget of the application whose key verified the request.</summary>
    public static TokenRequestClass ForApplication(Guid applicationId) => new Application(applicationId);

    /// <summary>The class marked on this request; <see cref="ByAddress"/> when none was.</summary>
    public static TokenRequestClass Of(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var marked) && marked is TokenRequestClass known ? known : ByAddress;

    /// <summary>Marks this class on the request, for the rate limiter that runs next.</summary>
    public void MarkOn(HttpContext context) => context.Items[ItemKey] = this;

    /// <summary>
    /// The partition this request counts in. Keys carry the settings version, like every other
    /// policy's, so a saved limit applies to fresh partitions.
    /// </summary>
    public abstract RateLimitPartition<string> Partition(
        int settingsVersion,
        string clientAddress,
        Func<FixedWindowBudget> perAddress,
        Func<FixedWindowBudget> perApplication);

    private sealed class Address : TokenRequestClass
    {
        public override RateLimitPartition<string> Partition(
            int settingsVersion, string clientAddress, Func<FixedWindowBudget> perAddress, Func<FixedWindowBudget> perApplication) =>
            RateLimitPartition.GetFixedWindowLimiter($"v{settingsVersion}:{clientAddress}", _ => perAddress().Options());
    }

    private sealed class Application(Guid applicationId) : TokenRequestClass
    {
        public override RateLimitPartition<string> Partition(
            int settingsVersion, string clientAddress, Func<FixedWindowBudget> perAddress, Func<FixedWindowBudget> perApplication) =>
            RateLimitPartition.GetFixedWindowLimiter($"v{settingsVersion}:app:{applicationId:N}", _ => perApplication().Options());
    }

    private sealed class RefusedAtOnce : TokenRequestClass
    {
        public override RateLimitPartition<string> Partition(
            int settingsVersion, string clientAddress, Func<FixedWindowBudget> perAddress, Func<FixedWindowBudget> perApplication) =>
            RateLimitPartition.Get("refused", _ => new RefusingLimiter());
    }

    /// <summary>
    /// Refuses every request (the built-in limiters accept no permit limit of zero). It holds
    /// nothing, so it never idles out and never needs replenishing.
    /// </summary>
    private sealed class RefusingLimiter : RateLimiter
    {
        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => new() { CurrentAvailablePermits = 0 };

        protected override RateLimitLease AttemptAcquireCore(int permitCount) => new Refusal();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
            ValueTask.FromResult<RateLimitLease>(new Refusal());

        private sealed class Refusal : RateLimitLease
        {
            public override bool IsAcquired => false;

            public override IEnumerable<string> MetadataNames => [];

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }
        }
    }
}
