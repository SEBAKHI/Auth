# Domain Errors: Examples

Worked examples for `../SKILL.md` §5 Domain Errors. This file adds no rule; where it and SKILL.md differ, SKILL.md wins. The HTTP side (body, status map, transport codes, contract tests) belongs to `/backend-development` → Response Format.

---

## 1. A catalog class in the default code format

```csharp
// Domain/Errors/UserErrors.cs
public static class UserErrors
{
    // No parameters and no metadata: static readonly fields.
    // Administrative creation by an authenticated caller. Public self-registration never reveals
    // an existing account (/security-mindset → Authentication).
    public static readonly Error DuplicateEmail =
        Error.Conflict("User.DuplicateEmail", "A user with this email already exists.");

    // Raised by an Application handler (sign-in), declared here like every other code of the context.
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("User.InvalidCredentials", "Invalid email or password.");

    // A fault of the system itself, reported as a value: a catalog member in the same format, with a fixed message.
    public static readonly Error StateInconsistent =
        Error.Unexpected("User.StateInconsistent", "The stored user state is inconsistent.");

    // Takes a parameter: a static method, so every call builds a new Error.
    public static Error NotFound(Guid id) =>
        Error.NotFound("User.NotFound", $"User with ID '{id}' was not found.");

    // Carries metadata: a static method that builds a new Error and a new dictionary on every call.
    // A wrong current password inside an authenticated request is Validation, never Unauthorized.
    public static Error CurrentPasswordIncorrect() =>
        Error.Validation(
            code: "User.CurrentPasswordIncorrect",
            description: "The current password is incorrect.",
            metadata: new Dictionary<string, object> { ["property"] = "CurrentPassword" });
}
```

Never write `public static readonly Error X = Error.Validation(..., metadata: ...)`: the dictionary would be shared, mutable static state.

## 2. Choosing the ErrorType

| Situation | Factory | Why |
|---|---|---|
| An order line has quantity 0 | `Error.Validation` | The input violates a rule |
| A reservation ID that does not exist | `Error.NotFound` | The addressed resource does not exist |
| The slot was booked by someone else first; an email already used by another user (administrative creation) | `Error.Conflict` | The current state or a uniqueness rule forbids it |
| Sign-in with a wrong password; a refresh token presented to the refresh endpoint that is expired, revoked, already used or unknown | `Error.Unauthorized` | The caller is not authenticated |
| A request to a protected endpoint without a valid access token | No error value: the framework's 401 with a transport code, or the reason code the challenge records | The authentication middleware rejects it before any handler runs (`/backend-development` → Error Codes on the Wire) |
| The current password is wrong in a password change | `Error.Validation` with the property path in metadata | Re-entered secret inside an authenticated request |
| The caller is signed in but lacks the permission | `Error.Forbidden` | Authenticated, not permitted |
| Stored state that breaks an invariant, found while handling a request | `Error.Unexpected` or `Error.Failure` | A fault of the system itself, reported as a value |
| The payment provider times out or the circuit is open | No error value: the exception propagates | A dependency outage is never `Failure` or `Unexpected` (`/backend-development` → Error Pipeline) |
| The same outage, when the client contract gives it a catalog code | `Error.Custom` with the custom unavailable type (section 4) | Recorded in the repository ADR |

## 3. Validation-pipeline codes

Validator rules take their codes from the catalog, so the code is declared once. How the pipeline turns failures into errors: `/backend-development` → Validation Pipeline.

```csharp
// Domain/Errors/ReservationErrors.cs
public static class ReservationErrors
{
    public static readonly Error PartySizeOutOfRange =
        Error.Validation("Reservation.PartySizeOutOfRange", "The party size is out of range.");
}

// Application/Features/Reservations/CreateReservation/CreateReservationCommandValidator.cs
RuleFor(c => c.PartySize)
    .InclusiveBetween(ReservationConstants.MinPartySize, ReservationConstants.MaxPartySize)
    .WithErrorCode(ReservationErrors.PartySizeOutOfRange.Code);
```

### Shared technical request-rule codes

Paging and sorting codes are declared once for every context, and the shared validator rules use them. Where a list query's parameters and errors are defined: `/backend-development` → List Queries.

```csharp
// Layout (a): Domain/Errors/. Layout (b): MyApp.SharedKernel/Errors/.
public static class PagingErrors
{
    public static readonly Error PageSizeOutOfRange =
        Error.Validation("Paging.PageSizeOutOfRange", "The page size is out of range.");
}

public static class SortingErrors
{
    public static readonly Error SortByNotAllowed =
        Error.Validation("Sorting.SortByNotAllowed", "The sort field is not allowed for this endpoint.");
}

// Layout (a): the Application project. Layout (b): MyApp.BuildingBlocks.Application.
// Every list query's validator calls these rules; no module declares its own paging or sorting code.
public static class ListQueryRules
{
    public static IRuleBuilderOptions<T, int> ValidPageSize<T>(this IRuleBuilder<T, int> rule, int maxPageSize) =>
        rule.InclusiveBetween(1, maxPageSize).WithErrorCode(PagingErrors.PageSizeOutOfRange.Code);

    public static IRuleBuilderOptions<T, string?> AllowedSortBy<T>(
        this IRuleBuilder<T, string?> rule, IReadOnlySet<string> allowed) =>
        rule.Must(sortBy => sortBy is null || allowed.Contains(sortBy))
            .WithErrorCode(SortingErrors.SortByNotAllowed.Code);
}
```

## 4. A custom ErrorType: the custom unavailable type

```csharp
// Layout (a): Domain/Errors/CustomErrorTypes.cs. Layout (b): MyApp.SharedKernel.
public static class CustomErrorTypes
{
    // A value outside ErrorOr's built-in ErrorType values. The repository ADR records it with its status,
    // and the single map sends it to 503 (/backend-development → Status Map).
    public const int Unavailable = 100;
}

// Domain/Errors/PaymentErrors.cs
public static class PaymentErrors
{
    // Returned only when the client contract names this outage with a catalog code (/backend-development → Error Pipeline).
    public static readonly Error ProviderUnavailable =
        Error.Custom(CustomErrorTypes.Unavailable, "Payment.ProviderUnavailable", "The payment provider is unavailable.");
}
```

## 5. A client contract that prescribes the codes

When the consuming client publishes a contract with its own code strings, the catalog uses those exact strings. The examples below belong to a different API from sections 1 to 4: its client contract prescribes lowercase codes scoped per error domain, so every member of that API uses them.

```csharp
public static class ReservationErrors
{
    // One rule, one member. Every endpoint that can raise it (create, reschedule) reuses this member.
    public static readonly Error SlotTaken =
        Error.Conflict("slot_taken", "The slot was taken by another reservation.");

    public static readonly Error LimitExceeded =
        Error.Conflict("limit_exceeded", "The user already holds the maximum number of reservations.");
}

public static class WaitlistErrors
{
    // The same string in another error domain, but a different rule: a separate member.
    public static readonly Error LimitExceeded =
        Error.Conflict("limit_exceeded", "The waitlist for this slot is full.");
}
```

Mapping a contract's code list onto the catalog:

| In the contract | In the catalog |
|---|---|
| One code that means the same rule in several error domains | One member, reused by every endpoint that surfaces the rule |
| One string that names different rules in different error domains | One member per rule, each in its own `{Concept}Errors` class |
| Two look-alike rules with their own strings | Two members |
| A code for a framework-produced error (unauthenticated, rate limited) | No catalog member: it is a transport code (`/backend-development` → Error Codes on the Wire) |
