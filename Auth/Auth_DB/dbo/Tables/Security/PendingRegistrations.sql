CREATE TABLE [dbo].[PendingRegistrations]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_PendingRegistrations_Id] DEFAULT NEWID(),
    [Handle] NVARCHAR(100) NOT NULL,           -- Keyed digest of the address; what the client holds
    [Email] NVARCHAR(255) NOT NULL,            -- As it will be written to Users (lower-case)
    [NormalizedEmail] NVARCHAR(255) NOT NULL,  -- As Users.NormalizedEmail (upper-case)
    [OtpHash] NVARCHAR(500) NOT NULL,          -- Keyed hash of the 6-digit code, scope pending-registration:{Id}
    [ExpiresAt] DATETIME2 NOT NULL,            -- When the current code dies
    [AttemptCount] INT NOT NULL CONSTRAINT [DF_PendingRegistrations_AttemptCount] DEFAULT 0,
    [MailedCount] INT NOT NULL CONSTRAINT [DF_PendingRegistrations_MailedCount] DEFAULT 0,
    [MailWindowStartUtc] DATETIME2 NOT NULL,
    [MailedAt] DATETIME2 NULL,                 -- When the current code's message reached the outbox
    [VerifiedAt] DATETIME2 NULL,               -- Telemetry only; never read by the completion step
    [ConsumedAt] DATETIME2 NULL,               -- A Users row now exists for this address
    [PreferredLanguage] NVARCHAR(10) NULL,
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_PendingRegistrations_CreatedAt] DEFAULT GETUTCDATE(),

    CONSTRAINT [PK_PendingRegistrations] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- A self-registration that has not yet earned its account: the address someone
-- typed, the hash of the code mailed to it, and the counters that bound what
-- the code can be spent on. Nothing is written to Users, no password is
-- hashed and no organization is created until the code comes back. Add no
-- foreign key to Users: the row exists BEFORE the account does, and after the
-- account exists the row is only history. Precedent: AccountDeletionVerifications.
--
-- One row per address at a time: UX_PendingRegistrations_NormalizedEmail_Live
-- below allows a single row whose ConsumedAt is NULL. ConsumedAt has exactly
-- one meaning - a Users row was created for this address, by the completion
-- step or by any other door that creates accounts - and is never stamped for
-- expiry or exhausted attempts. A dead code (expired, or five wrong guesses)
-- is rotated IN PLACE by the next start for the address: new OtpHash, new
-- ExpiresAt, AttemptCount back to zero, same Id. Never delete a row to make
-- room for a new one.
--
-- MailedCount / MailWindowStartUtc live on this single row so the cap on how
-- many codes an address is issued per window survives rotation. That cap is
-- the guessing bound (codes per window x five attempts per code), not a
-- courtesy; keep it on the row, never on the code.
--
-- There is deliberately no IsDecoy column. A row is written for a free
-- address, for one that already has an account and for one reserved by a
-- deletion tombstone alike, and only which message goes out differs. A stored
-- flag saying which is which would be an oracle for whoever can read the
-- table or the delivery log; the row must carry no trace of it.
--
-- Expiry is enforced on every read (ExpiresAt > GETUTCDATE()), never by the
-- sweep. The sweep is hygiene: it removes rows whose code expired more than
-- DataRetention:PendingRegistrationDays ago, consumed or not.

-- One unconsumed row per address, enforced here rather than in code.
CREATE UNIQUE NONCLUSTERED INDEX [UX_PendingRegistrations_NormalizedEmail_Live]
ON [dbo].[PendingRegistrations] ([NormalizedEmail])
WHERE [ConsumedAt] IS NULL;
GO

-- The client-facing lookup: verify and complete find the live row by handle.
CREATE NONCLUSTERED INDEX [IX_PendingRegistrations_Handle_Live]
ON [dbo].[PendingRegistrations] ([Handle])
WHERE [ConsumedAt] IS NULL;
GO

-- Deliberately NOT filtered: serves the hard-delete purge, which removes
-- every row for the address, consumed or not.
CREATE NONCLUSTERED INDEX [IX_PendingRegistrations_Email]
ON [dbo].[PendingRegistrations] ([Email]);
GO

-- Serves the retention sweep, which deletes consumed and unconsumed rows alike.
CREATE NONCLUSTERED INDEX [IX_PendingRegistrations_Cleanup]
ON [dbo].[PendingRegistrations] ([ExpiresAt]);
GO
