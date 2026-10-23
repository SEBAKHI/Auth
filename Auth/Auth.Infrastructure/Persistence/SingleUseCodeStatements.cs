using Dapper;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// The two statements every single-use code table shares: a reservation taken
/// before a code is checked, and the consumption of a code that matched. One
/// definition, so the conditions that make each of them atomic cannot drift
/// apart between the tables that rely on them.
/// </summary>
/// <remarks>
/// <para>
/// The reservation is the whole point. A count read first and raised after the
/// check only records attempts; every request that read the row before the first
/// raise sees the same count, so a burst of concurrent guesses all pass the cap.
/// Here the cap, the expiry and single use are conditions of the very statement
/// that counts, and a request that is not let through checks nothing.
/// </para>
/// <para>
/// The consumption gives the matched attempt back, so a row at rest counts only
/// the codes that were rejected.
/// </para>
/// <para>
/// The table name is spliced into the text, so the constructor is private: the
/// instances below are the only tables these statements can ever address.
/// </para>
/// <para>
/// A table that holds rows for several purposes names one more condition, which
/// every statement on it carries: the two-factor challenges (S24), whose rows serve a
/// sign-in, an emailed-code enrollment, a recovery or switching the emailed code off,
/// and whose id alone must never let one answer another. Its callers pass
/// <c>@Purpose</c>.
/// </para>
/// </remarks>
internal sealed class SingleUseCodeStatements
{
    internal static readonly SingleUseCodeStatements TwoFactorChallenges = new("TwoFactorChallenges", "[Purpose] = @Purpose");
    internal static readonly SingleUseCodeStatements EmailVerificationTokens = new("EmailVerificationTokens");
    internal static readonly SingleUseCodeStatements OwnershipTransferCodes = new("OwnershipTransferCodes");
    internal static readonly SingleUseCodeStatements TwoFactorBindCodes = new("TwoFactorBindCodes");

    private SingleUseCodeStatements(string table, string? scopeCondition = null)
    {
        var scope = scopeCondition is null
            ? string.Empty
            : $@"
              AND {scopeCondition}";

        Reserve = $@"
            UPDATE [dbo].[{table}] SET
                [AttemptCount] = [AttemptCount] + 1
            OUTPUT inserted.[AttemptCount]
            WHERE [Id] = @Id
              AND [UsedAt] IS NULL
              AND [ExpiresAt] > SYSUTCDATETIME()
              AND [AttemptCount] < @MaxAttempts{scope}";

        Consume = $@"
            UPDATE [dbo].[{table}] SET
                [UsedAt] = SYSUTCDATETIME(),
                [AttemptCount] = [AttemptCount] - 1
            WHERE [Id] = @Id
              AND [UsedAt] IS NULL{scope}";
    }

    /// <summary>
    /// Counts one attempt on a live row and returns the new count. Parameters:
    /// <c>@Id</c>, <c>@MaxAttempts</c>.
    /// </summary>
    internal string Reserve { get; }

    /// <summary>
    /// Marks an unused row used and gives back the attempt that matched.
    /// Parameter: <c>@Id</c>.
    /// </summary>
    internal string Consume { get; }

    /// <summary>
    /// Runs <see cref="Reserve"/> on its own connection.
    /// </summary>
    /// <returns>The count including this attempt, or null when nothing may be checked.</returns>
    internal Task<int?> TryReserveAsync(
        IDbConnectionFactory connectionFactory,
        Guid id,
        int maxAttempts,
        CancellationToken cancellationToken) =>
        TryReserveAsync(connectionFactory, id, maxAttempts, scopeParameters: null, cancellationToken);

    /// <summary>
    /// Runs <see cref="Reserve"/> on its own connection, with the parameters of the
    /// table's scope condition (<c>@Purpose</c> for the two-factor challenges).
    /// </summary>
    /// <returns>The count including this attempt, or null when nothing may be checked.</returns>
    internal async Task<int?> TryReserveAsync(
        IDbConnectionFactory connectionFactory,
        Guid id,
        int maxAttempts,
        object? scopeParameters,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);

        var parameters = new DynamicParameters(new { Id = id, MaxAttempts = maxAttempts });
        if (scopeParameters is not null)
        {
            parameters.AddDynamicParams(scopeParameters);
        }

        return await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
            Reserve,
            parameters,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Runs <see cref="Consume"/> on its own connection.
    /// </summary>
    /// <returns>True only for the one caller that consumed the row.</returns>
    internal async Task<bool> TryConsumeAsync(
        IDbConnectionFactory connectionFactory,
        Guid id,
        CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);

        var consumed = await connection.ExecuteAsync(new CommandDefinition(
            Consume,
            new { Id = id },
            cancellationToken: cancellationToken));

        return consumed == 1;
    }
}
