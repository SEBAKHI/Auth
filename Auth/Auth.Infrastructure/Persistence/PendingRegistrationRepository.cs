using System.Data;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Dapper implementation of <see cref="IPendingRegistrationRepository"/>.
/// </summary>
/// <remarks>
/// The code generator and hasher are injected here, not in the handlers,
/// because a code can only be minted once the row's id is known and only
/// while the row is locked: the scope the code is hashed under is the id, and
/// the decision to issue one is a decision about the locked row. Every
/// statement inside a transaction carries that transaction explicitly —
/// SqlClient throws for a command issued on a connection with an open
/// transaction it was not handed, and that throw surfaces to the client as a
/// 400 with nothing pointing at the cause.
/// </remarks>
public sealed class PendingRegistrationRepository : IPendingRegistrationRepository
{
    private const string Columns = @"
                [Id], [Handle], [Email], [NormalizedEmail], [OtpHash], [ExpiresAt],
                [AttemptCount], [MailedCount], [MailWindowStartUtc], [MailedAt],
                [VerifiedAt], [ConsumedAt], [PreferredLanguage], [CreatedAt]";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IOtpHasher _otpHasher;

    public PendingRegistrationRepository(
        IDbConnectionFactory connectionFactory,
        IOtpGenerator otpGenerator,
        IOtpHasher otpHasher)
    {
        _connectionFactory = connectionFactory;
        _otpGenerator = otpGenerator;
        _otpHasher = otpHasher;
    }

    /// <inheritdoc />
    public Task<PendingRegistrationStart> StartAsync(
        PendingRegistrationStartRequest request, CancellationToken cancellationToken)
        => StartAsync(request, retryAfterCollision: true, cancellationToken);

    private async Task<PendingRegistrationStart> StartAsync(
        PendingRegistrationStartRequest request, bool retryAfterCollision, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // The predicate names only the columns of the filtered unique index,
        // and deliberately NOT the expiry: an expired row is still the
        // address's one unconsumed row and must be found here and rotated,
        // not collided with. The seek therefore lands on
        // UX_PendingRegistrations_NormalizedEmail_Live, and UPDLOCK with
        // HOLDLOCK takes a key-range lock there even when no row exists yet,
        // so two starts for one new address serialize instead of both
        // inserting.
        var dto = await connection.QuerySingleOrDefaultAsync<PendingRegistrationDto>($@"
            SELECT {Columns}
            FROM [dbo].[PendingRegistrations] WITH (UPDLOCK, HOLDLOCK)
            WHERE [NormalizedEmail] = @NormalizedEmail AND [ConsumedAt] IS NULL",
            new { NormalizedEmail = PendingRegistration.NormalizeKey(request.Email) },
            transaction);

        if (dto is null)
        {
            var created = PendingRegistration.Create(request.Handle, request.Email, now);
            created.TryChargeMailWindow(request.MailWindow, request.MaxMailsPerWindow, now);
            var code = IssueCode(created, request, now);

            try
            {
                await connection.ExecuteAsync($@"
                    INSERT INTO [dbo].[PendingRegistrations] ({Columns}
                    ) VALUES (
                        @Id, @Handle, @Email, @NormalizedEmail, @OtpHash, @ExpiresAt,
                        @AttemptCount, @MailedCount, @MailWindowStartUtc, @MailedAt,
                        @VerifiedAt, @ConsumedAt, @PreferredLanguage, @CreatedAt
                    )",
                    Parameters(created),
                    transaction);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627 && retryAfterCollision)
            {
                // The one race the key-range lock does not close on every
                // isolation level: another start inserted this address between
                // our read and our insert. Its row is the address's row now;
                // start over against it, once.
                transaction.Rollback();
                return await StartAsync(request, retryAfterCollision: false, cancellationToken);
            }
            catch (SqlException ex) when (ex.Number is 1205 && retryAfterCollision)
            {
                // Chosen as the deadlock victim against a completion holding
                // the row through its clustered key (see CheckCodeUnderLockAsync).
                // The transaction is already gone; start over, once.
                return await StartAsync(request, retryAfterCollision: false, cancellationToken);
            }

            transaction.Commit();
            return new PendingRegistrationStart(created, PendingRegistrationStartAction.Minted, code);
        }

        var row = dto.ToEntity();

        // In someone's inbox: neither rotated nor re-mailed nor invalidated.
        if (row.IsCodeLive(now) && row.WasMailed)
        {
            transaction.Commit();
            return new PendingRegistrationStart(row, PendingRegistrationStartAction.Unchanged, Code: null);
        }

        // Dead, or never handed to the outbox. A fresh code, if the address's
        // window still allows one; the counters are the guessing bound and
        // survive the rotation because the row does.
        if (!row.TryChargeMailWindow(request.MailWindow, request.MaxMailsPerWindow, now))
        {
            transaction.Commit();
            return new PendingRegistrationStart(row, PendingRegistrationStartAction.Unchanged, Code: null);
        }

        // The handle is re-keyed with every code: the platform's HMAC key can
        // be rotated, and a row that keeps being re-started is never swept.
        row.Rebind(request.Handle);
        var rotated = IssueCode(row, request, now);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PendingRegistrations]
            SET [Handle] = @Handle,
                [OtpHash] = @OtpHash,
                [ExpiresAt] = @ExpiresAt,
                [AttemptCount] = 0,
                [MailedCount] = @MailedCount,
                [MailWindowStartUtc] = @MailWindowStartUtc,
                [MailedAt] = NULL,
                [VerifiedAt] = NULL,
                [PreferredLanguage] = @PreferredLanguage
            WHERE [Id] = @Id AND [ConsumedAt] IS NULL",
            Parameters(row),
            transaction);

        transaction.Commit();
        return new PendingRegistrationStart(row, PendingRegistrationStartAction.Minted, rotated);
    }

    /// <inheritdoc />
    public Task<PendingRegistrationCodeCheck> CheckCodeUnderLockAsync(
        string handle, string code, CancellationToken cancellationToken)
        => CheckCodeUnderLockAsync(handle, code, retryAfterDeadlock: true, cancellationToken);

    private async Task<PendingRegistrationCodeCheck> CheckCodeUnderLockAsync(
        string handle, string code, bool retryAfterDeadlock, CancellationToken cancellationToken)
    {
        try
        {
            return await CheckCodeUnderLockOnceAsync(handle, code, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 1205 && retryAfterDeadlock)
        {
            // This read reaches the row through the Handle index and then its
            // clustered key; the completion step's transaction holds the
            // clustered key and, when it stamps ConsumedAt, needs this index
            // too. A check landing on the row in the same few milliseconds as
            // its completion — a double submit — can deadlock, and SQL Server
            // kills one side. The victim's transaction is already rolled back
            // and nothing of ours was written, so it is run once more on a
            // fresh connection; if the completion won, the re-run finds no
            // live row and answers NotFound, which is the right answer.
            return await CheckCodeUnderLockAsync(handle, code, retryAfterDeadlock: false, cancellationToken);
        }
    }

    private async Task<PendingRegistrationCodeCheck> CheckCodeUnderLockOnceAsync(
        string handle, string code, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // Expiry enforced here, on the read, for every caller — against the
        // application clock that minted ExpiresAt and that StartAsync judges
        // liveness by, so the two never disagree by a clock offset and leave a
        // code that one call says is dead and the other says is live.
        var dto = await connection.QuerySingleOrDefaultAsync<PendingRegistrationDto>($@"
            SELECT {Columns}
            FROM [dbo].[PendingRegistrations] WITH (UPDLOCK, HOLDLOCK)
            WHERE [Handle] = @Handle AND [ConsumedAt] IS NULL AND [ExpiresAt] > @Now",
            new { Handle = handle, Now = DateTime.UtcNow },
            transaction);

        if (dto is null)
        {
            transaction.Rollback();
            return new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.NotFound, Row: null);
        }

        var row = dto.ToEntity();

        // The gate and the comparison sit under the same lock, so concurrent
        // guesses at one row see each other's increments.
        if (row.IsExhausted)
        {
            transaction.Rollback();
            return new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Exhausted, row);
        }

        if (!_otpHasher.Verify(row.OtpScope, code, row.OtpHash))
        {
            // The failure is what must commit: a rolled-back increment would
            // hand the guess back.
            await connection.ExecuteAsync(@"
                UPDATE [dbo].[PendingRegistrations]
                SET [AttemptCount] = [AttemptCount] + 1
                WHERE [Id] = @Id",
                new { row.Id },
                transaction);
            transaction.Commit();

            row.RecordFailedAttempt();
            return new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Wrong, row);
        }

        // A right code costs nothing: the stamp is telemetry, never read by
        // the completion step.
        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PendingRegistrations]
            SET [VerifiedAt] = COALESCE([VerifiedAt], GETUTCDATE())
            WHERE [Id] = @Id",
            new { row.Id },
            transaction);
        transaction.Commit();

        row.MarkVerified(DateTime.UtcNow);
        return new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Match, row);
    }

    /// <inheritdoc />
    public async Task MarkMailedAsync(Guid id, string otpHash, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(@"
            UPDATE [dbo].[PendingRegistrations]
            SET [MailedAt] = GETUTCDATE()
            WHERE [Id] = @Id AND [OtpHash] = @OtpHash AND [MailedAt] IS NULL",
            new { Id = id, OtpHash = otpHash });
    }

    /// <inheritdoc />
    public async Task<int> ConsumeByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(@"
            UPDATE [dbo].[PendingRegistrations]
            SET [ConsumedAt] = GETUTCDATE()
            WHERE [NormalizedEmail] = @NormalizedEmail AND [ConsumedAt] IS NULL",
            new { NormalizedEmail = normalizedEmail });
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

        return await connection.ExecuteAsync(@"
            DELETE TOP (@BatchSize) FROM [dbo].[PendingRegistrations]
            WHERE [ExpiresAt] < @OlderThan",
            new { OlderThan = olderThanUtc, BatchSize = batchSize });
    }

    private string IssueCode(PendingRegistration row, PendingRegistrationStartRequest request, DateTime now)
    {
        var code = _otpGenerator.GenerateNumericOtp(6);
        row.IssueCode(_otpHasher.Hash(row.OtpScope, code), request.ExpirationMinutes, request.PreferredLanguage, now);
        return code;
    }

    private static object Parameters(PendingRegistration row) => new
    {
        row.Id,
        row.Handle,
        Email = row.Email.Value,
        row.NormalizedEmail,
        row.OtpHash,
        row.ExpiresAt,
        row.AttemptCount,
        row.MailedCount,
        row.MailWindowStartUtc,
        row.MailedAt,
        row.VerifiedAt,
        row.ConsumedAt,
        row.PreferredLanguage,
        row.CreatedAt
    };

    private sealed record PendingRegistrationDto
    {
        public Guid Id { get; init; }
        public string Handle { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string NormalizedEmail { get; init; } = string.Empty;
        public string OtpHash { get; init; } = string.Empty;
        public DateTime ExpiresAt { get; init; }
        public int AttemptCount { get; init; }
        public int MailedCount { get; init; }
        public DateTime MailWindowStartUtc { get; init; }
        public DateTime? MailedAt { get; init; }
        public DateTime? VerifiedAt { get; init; }
        public DateTime? ConsumedAt { get; init; }
        public string? PreferredLanguage { get; init; }
        public DateTime CreatedAt { get; init; }

        public PendingRegistration ToEntity() => new(
            Id, Handle, Email, NormalizedEmail, OtpHash, ExpiresAt, AttemptCount, MailedCount,
            MailWindowStartUtc, MailedAt, VerifiedAt, ConsumedAt, PreferredLanguage, CreatedAt);
    }
}
