namespace Auth.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of an account deletion request.
/// Values match the CK_AccountDeletionRequests_Status check constraint.
/// </summary>
public enum AccountDeletionStatus
{
    /// <summary>
    /// The request is inside its grace window; the account is deactivated but
    /// recoverable via re-authentication.
    /// </summary>
    PendingGrace = 1,

    /// <summary>
    /// The user recovered the account during grace; the request is terminal.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// The background worker has claimed the request and staged destruction is
    /// executing; recovery is no longer possible.
    /// </summary>
    Processing = 3,

    /// <summary>
    /// Staged destruction finished; the row is destruction evidence.
    /// </summary>
    Completed = 4,

    /// <summary>
    /// Execution failed repeatedly and was dead-lettered for operator attention.
    /// </summary>
    Failed = 5
}
