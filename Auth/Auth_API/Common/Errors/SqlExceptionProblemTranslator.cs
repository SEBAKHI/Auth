using System.Collections.Frozen;
using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Microsoft.Data.SqlClient;

namespace Auth_API.Common.Errors;

/// <summary>
/// The SQL Server errors that are not faults of this code (ADR 0001, section 5): the database
/// being unreachable, and a reference blocking a hard delete. Every other number, unique-key
/// violations included, stays a 500.
/// </summary>
public sealed class SqlExceptionProblemTranslator : IExceptionProblemTranslator
{
    /// <summary>A foreign key blocks the statement: a referenced row cannot be deleted.</summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>
    /// Timeout (-2), network and instance errors (2, 53, 233, 10053, 10054, 10060), and the
    /// database being unavailable (4060, 40613).
    /// </summary>
    private static readonly FrozenSet<int> Outages =
        new[] { -2, 2, 53, 233, 4060, 10053, 10054, 10060, 40613 }.ToFrozenSet();

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
