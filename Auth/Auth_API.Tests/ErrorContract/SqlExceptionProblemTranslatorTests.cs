using Auth.Domain.Errors;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Errors;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Http;

namespace Auth_API.Tests.ErrorContract;

/// <summary>
/// Which SQL Server errors are answered other than 500 (ADR 0001, section 5).
/// </summary>
public class SqlExceptionProblemTranslatorTests
{
    private readonly SqlExceptionProblemTranslator _translator = new();

    [Fact]
    public void Translate_WithForeignKeyViolation_Returns409WithReferenceConflict()
    {
        var problem = _translator.Translate(SqlExceptions.WithNumber(547));

        Assert.Equal(new ExceptionProblem(StatusCodes.Status409Conflict, PersistenceErrors.ReferenceConflict.Code), problem);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(53)]
    [InlineData(233)]
    [InlineData(4060)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    [InlineData(40613)]
    public void Translate_WithUnreachableDatabase_ReturnsOutage(int number)
    {
        Assert.Equal(ExceptionProblem.Outage, _translator.Translate(SqlExceptions.WithNumber(number)));
    }

    [Theory]
    [InlineData(2601)] // unique index: an expected race is translated in its repository
    [InlineData(2627)] // unique constraint: likewise
    [InlineData(1205)] // deadlock victim
    [InlineData(8152)] // truncation: a validator limit that does not match its column
    public void Translate_WithAnyOtherNumber_ReturnsNull(int number)
    {
        Assert.Null(_translator.Translate(SqlExceptions.WithNumber(number)));
    }
}
