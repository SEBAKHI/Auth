---
name: failure-mode-design
description: Load this skill when designing integrations with external services, databases, caches, message queues, or file storage, or deciding where failures are caught, retried, or surfaced. Use it to define failure responses, retries, timeouts, circuit breakers, exception-handling boundaries and graceful degradation. No silent failures allowed.
user-invocable: true
---

# Failure Mode Design

**Every external dependency has a defined failure response. No silent failures.**

The rules here are stack-neutral. .NET-specific mechanics appear only as "(C#/.NET: …)" parentheticals; the skills they name own the implementation.

## Classify Every Dependency Use

- Classify every dependency use on two axes. The repository ADR records the classification of non-HTTP dependencies.
  - Security control or not.
  - Outage or not: whether the dependency's unavailability is reported as an outage (503) or as an unexpected failure (500). HTTP dependencies are outages by default (C#/.NET: the outage classifier of `/backend-development` → Error Pipeline).
- In this skill, "the outage status" means 503 for a dependency classified as an outage, and 500 otherwise.
- Security controls fail closed. These are authentication, authorization, credential revocation and reuse detection, rate limiting and lockout, idempotency keys and locks.
- When a security control's store is unavailable, the request is rejected: the store's failure propagates, and the response is the outage status, or 429 from the limiter. It is never served from stale data, and the check is never skipped.
- Only non-security data may be served from a cache, or skipped.

## Failure Responses

| Component | Scenario | Expected behavior |
|---|---|---|
| Database | Transient connection fault | The provider's bounded, retrying execution strategy (C#/.NET: EF Core `EnableRetryOnFailure`; a user-initiated transaction runs inside `Database.CreateExecutionStrategy().ExecuteAsync`). A commit can have an unknown outcome, so inserts with store-generated keys use client-generated keys or commit verification. |
| Database | Persistent outage | Writes fail, and the exception propagates to the central handler, which returns the outage status. A write is never acknowledged before it commits. Writes are never queued in memory or on local disk. |
| Database | Query timeout | Pass the request's cancellation signal to every query and set a command timeout. The timeout propagates to the central handler, which logs it once with context and returns the outage status. The call site neither logs nor converts it. |
| Database | Reads during an outage | A query may serve a cached DTO or read model only where the use case documents staleness as acceptable and the data is not security-relevant. |
| Cache | Unavailable | Cache calls have a short timeout and their own circuit breaker; while it is open, the cache is skipped at once. Log circuit state changes, not every request. Protect the database from the fallback surge: a local in-process tier, request coalescing, or load shedding. Security-state caches fail closed. |
| External API | Timeout, 5xx, 408, 429 | Retry per Retries, Timeouts and Circuit Breakers, with one circuit breaker per dependency. Queries may fall back to a documented substitute (a cached or default value that the response marks as stale). Commands never fall back: the outage propagates, and the response is 503. |
| Message broker | Unavailable or full | Messages that must not be lost are written to a transactional outbox in the same transaction as the state change (`/event-driven-architecture` §8, §9). A dispatcher publishes them, retrying, parking and alerting per `/event-driven-architecture` §8. Consumers are idempotent. Messages are never buffered in memory or on local disk. When the outbox backlog passes a threshold, producers are throttled with 503 and `Retry-After` (deliberate shedding; C#/.NET: the shedding gate of `/backend-development` → Error Pipeline). |
| File storage | Upload fails | Use the storage client's built-in retry (one layer), with no application retry around it. Buffer uploads only to bounded temporary storage under the enforced size limit, and delete the buffer afterwards. Storage is an outage dependency: the repository ADR adds the storage client's exception types to the outage classification (C#/.NET: the outage classifier of `/backend-development` → Error Pipeline), so a final failure is rejected with 503 and `Retry-After`: the client keeps its file and retries. Never report success before the object is committed, and clean up partially written objects. |

## Retries, Timeouts and Circuit Breakers

- Retry only transient faults: transport errors, timeouts, 408, 429 and 5xx. Never other 4xx.
- Retry only idempotent requests (RFC 9110), or requests that carry an idempotency key the receiver honors.
- Use exponential backoff with jitter, honor `Retry-After`, and bound the retries with a total timeout.
- One resilience layer per client: no retry stacked on another retry. A storage or database client's built-in retry counts as that layer.
- Timeouts:
  - every call has an attempt timeout inside the breaker, so a timeout counts as a failure;
  - a total timeout sits outside the retries;
  - the platform client's own timeout is set above the total timeout, so the pipeline decides, not the client.
- Order, outermost first: rate limiter → total timeout → retry → circuit breaker → attempt timeout. Retries stop while the circuit is open.
- One circuit breaker per dependency.
  - It opens when the failure ratio over a sampling window, with a minimum throughput, reaches its threshold. It stays open for the break duration, and one successful probe closes it.
  - It counts only the transient faults above, never other 4xx. Its settings come from configuration.
  - Log its state changes: opened, with the cause; closed.
- A breaker is built once per client, never per request.

C#/.NET: the client registration in `/backend-development` → Outbound HTTP implements these rules with one resilience handler (Microsoft.Extensions.Http.Resilience). Microsoft.Extensions.Http.Polly (`AddPolicyHandler`) is deprecated and never used.

## Exception-Handling Boundaries

- Unexpected exceptions are handled once, centrally, at each host entry point, logged with context, and (for HTTP) mapped to the error contract. For HTTP, that is one handler at the outermost middleware, which also covers middleware, model binding, authentication and endpoint filters (C#/.NET: `UseExceptionHandler` with one `IExceptionHandler`, `/backend-development` → Error Pipeline).
- Background workers: one catch-all per loop iteration, inside the loop. It logs once with context and continues after a delay, so one failed item neither ends the worker nor stops the host (C#/.NET: inside the `while (!stoppingToken.IsCancellationRequested)` loop of `BackgroundService.ExecuteAsync`). Never one catch around the whole loop.
- Message consumers run by a broker or messaging framework have no catch-all. The failure reaches the framework, whose retry and dead-letter handling apply (`/event-driven-architecture` §7).
- The only other catch-all is an isolation boundary that keeps a side-effect failure from failing its caller (C#/.NET: an in-process notification handler, and the retry recorder its catch calls, `/event-driven-architecture` §4, §7). It logs once with context and, for a critical side effect, records the failure for retry; the recorder logs a failure to record at Critical and never rethrows.
- A pipeline behavior may add request context and rethrow. It never logs a second time, maps or swallows.
- There is no catch-all in request handlers, services or endpoints.
- Business-rule failures follow the project's error convention (C#/.NET: returned as `ErrorOr` errors and never thrown, P7).
- Cancellation is neither swallowed nor logged as an error, isolation boundaries included. Cancellation means that the token the boundary received was cancelled (the handler's token, or the worker's stopping token). An `OperationCanceledException` raised while that token is not cancelled, such as a timeout's `TaskCanceledException`, is a failure and is handled like any other (C#/.NET: `catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`, with the boundary's own token).

## Graceful Degradation

```
┌──────────────────────────────────────────────────────────────────────┐
│                     GRACEFUL DEGRADATION LEVELS                      │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Level 0: FULL FUNCTIONALITY                                         │
│  └─ All systems operational, optimal experience                      │
│                                                                      │
│  Level 1: DEGRADED BUT FUNCTIONAL                                    │
│  └─ Non-critical features disabled                                   │
│  └─ Real-time features fall back to polling                          │
│  └─ Caching more aggressively                                        │
│                                                                      │
│  Level 2: CORE FEATURES ONLY                                         │
│  └─ Only essential features available                                │
│  └─ Read-only mode for some features                                 │
│  └─ Writes to unavailable stores rejected with the outage status     │
│                                                                      │
│  Level 3: MAINTENANCE MODE                                           │
│  └─ Static content only                                              │
│  └─ Clear messaging to users                                         │
│  └─ API returns 503 with Retry-After (the estimated recovery time)   │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

- A level is entered through an operator-controlled flag, or through an automatic policy recorded in an ADR. The ADR lists the disabled features for each level.
- Disabled endpoints return 503 with `Retry-After` and the standard error body, never 200. This is deliberate shedding (C#/.NET: the shedding gate of `/backend-development` → Error Pipeline).
- At Level 2, writes to unavailable stores are rejected with the outage status, not queued.
- At Level 3, the API serves 503 with `Retry-After` through the same gate.
