---
name: failure-mode-design
description: Load this skill when designing integrations with external services, databases, caches, message queues, or file storage. Use it to define failure responses, implement circuit breakers, and plan graceful degradation strategies. No silent failures allowed.
user-invocable: true
---

# Failure Mode Design

## For Every Integration Point

**Every external dependency must have a defined failure response. No silent failures.**

| Component | Failure Scenario | Expected Behavior |
|-----------|-----------------|-------------------|
| **Database** | Connection failed | Return cached data if available; queue writes for retry; show degraded UI |
| **Database** | Query timeout | Cancel operation; log with context; return error to user |
| **Cache** | Redis unavailable | Fall back to database; log warning; continue without cache |
| **External API** | Timeout / 5xx | Retry with exponential backoff; circuit breaker; fallback response |
| **Message Queue** | Full / unavailable | Apply backpressure; store locally; retry with limits |
| **File Storage** | Upload failed | Retry; notify user; don't lose the file |

## Circuit Breaker Pattern

```csharp
// Circuit breaker states:
// CLOSED  → Normal operation, requests flow through
// OPEN    → Failures exceeded threshold, requests fail fast
// HALF-OPEN → Testing if service recovered

public class CircuitBreakerSettings
{
    public int FailureThreshold { get; set; } = 5;        // Failures before opening
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int SuccessThreshold { get; set; } = 3;        // Successes to close
}

// Usage with Polly
services.AddHttpClient<IExternalService, ExternalService>()
    .AddPolicyHandler(Policy
        .Handle<HttpRequestException>()
        .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (result, duration) =>
                logger.LogWarning("Circuit opened for {Duration}", duration),
            onReset: () =>
                logger.LogInformation("Circuit closed"),
            onHalfOpen: () =>
                logger.LogInformation("Circuit half-open, testing...")));
```

## Graceful Degradation Strategy

```
┌─────────────────────────────────────────────────────────────┐
│              GRACEFUL DEGRADATION LEVELS                    │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  Level 0: FULL FUNCTIONALITY                                │
│  └─ All systems operational, optimal experience             │
│                                                             │
│  Level 1: DEGRADED BUT FUNCTIONAL                           │
│  └─ Non-critical features disabled                          │
│  └─ Real-time features fall back to polling                 │
│  └─ Caching more aggressively                               │
│                                                             │
│  Level 2: CORE FEATURES ONLY                                │
│  └─ Only essential features available                       │
│  └─ Read-only mode for some features                        │
│  └─ Queuing writes for later processing                     │
│                                                             │
│  Level 3: MAINTENANCE MODE                                  │
│  └─ Static content only                                     │
│  └─ Clear messaging to users                                │
│  └─ Estimated recovery time displayed                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```
