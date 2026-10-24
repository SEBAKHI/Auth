using System.Collections.Frozen;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Microsoft.Data.SqlClient;

namespace Auth_API.Common.Errors;

/// <summary>
/// The SQL Server errors that are not faults of this code (ADR 0001, section 5): the database
/// being unreachable, the account-authority lock not granted, and a reference blocking a hard
/// delete. Every other number, unique-key violations and deadlocks included, stays a 500.
/// </summary>
/// <remarks>
/// <para>
/// The account-authority lock not granted in time (51000, thrown by
/// <c>UserRepository.AccountAuthorityLockSql</c>) is contention, not a fault: the lock is
/// taken before any write of the change, whose transaction is then rolled back, so the change
/// wrote nothing and can be asked again. It answers as an outage (503 with <c>Retry-After</c>).
/// The account's own deletion spends its single-use code before the lock, so asking again
/// there takes a new code; nothing is written twice.
/// </para>
/// <para>
/// A deadlock victim (1205) stays a 500 on purpose: SQL Server rolls back only the victim's
/// own transaction, and a command can meet one after its write committed (in an audit or
/// notification handler that runs after the commit), where a retry would repeat that write.
/// </para>
/// </remarks>
public sealed class SqlExceptionProblemTranslator : IExceptionProblemTranslator
{
    /// <summary>A foreign key blocks the statement: a referenced row cannot be deleted.</summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>
    /// Timeout (-2), network and instance errors (2, 53, 233, 10053, 10054, 10060), the
    /// database being unavailable (4060, 40613), and the account-authority lock not granted
    /// before any write (51000).
    /// </summary>
    private static readonly FrozenSet<int> Outages =
        new[] { -2, 2, 53, 233, 4060, 10053, 10054, 10060, 40613, 51000 }.ToFrozenSet();

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
