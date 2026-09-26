using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Storage outcomes the API reports as a result (ADR 0001).
/// </summary>
public static class PersistenceErrors
{
    public static readonly Error ReferenceConflict = Error.Conflict(
        code: "Persistence.ReferenceConflict",
        description: "The operation could not be completed because related records reference this resource.");

}
