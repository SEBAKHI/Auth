using System.Collections.Frozen;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Microsoft.Data.SqlClient;

namespace Auth_API.Common.Errors;

/// <summary>
/// The SQL Server errors that are not faults of this code (ADR 0001, section 5): the database
/// being unreachable or busy, and a reference blocking a hard delete. Every other number,
/// unique-key violations included, stays a 500.
/// </summary>
/// <remarks>
/// A deadlock victim (1205) and the account-authority lock not granted in time (51000, thrown
/// by <c>UserRepository.AccountAuthorityLockSql</c>) are contention, not faults. SQL Server
/// rolls the deadlock victim's transaction back; the lock is taken before any write of the
/// account-authority change, whose transaction is then rolled back. Either way the losing
/// work was undone, so the request can be sent again: they answer as an outage (503 with
/// <c>Retry-After</c>), never as a 500.
/// </remarks>
public sealed class SqlExceptionProblemTranslator : IExceptionProblemTranslator
{
    /// <summary>A foreign key blocks the statement: a referenced row cannot be deleted.</summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>
    /// Timeout (-2), network and instance errors (2, 53, 233, 10053, 10054, 10060), the
    /// database being unavailable (4060, 40613), and contention that rolled the work back:
    /// a deadlock victim (1205) and the account-authority lock not granted (51000).
    /// </summary>
    private static readonly FrozenSet<int> Outages =
        new[] { -2, 2, 53, 233, 1205, 4060, 10053, 10054, 10060, 40613, 51000 }.ToFrozenSet();

    private static readonly ExceptionProblem ReferenceConflict = new(
        ErrorStatusMap.ToStatusCode(PersistenceErrors.ReferenceConflict),
        PersistenceErrors.ReferenceConflict.Code);

    public Type ExceptionType => typeof(SqlException);

    public ExceptionProblem? Translate(Exception exception)
    {
        var number = ((SqlException)exception).Number;

        return number == ForeignKeyViolation ? ReferenceConflict
            : Outages.Contains(number) ? ExceptionProblem.Outage
            : null;
    }
}
