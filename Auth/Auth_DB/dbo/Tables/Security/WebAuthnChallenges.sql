CREATE TABLE [dbo].[WebAuthnChallenges]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_WebAuthnChallenges_Id] DEFAULT NEWID(),
    [HandleHash] NVARCHAR(100) NOT NULL,       -- keyed fingerprint of the handle the __Host-auth_webauthn cookie carries (CredentialPurpose.WebAuthnChallenge)
    [Purpose] TINYINT NOT NULL,                -- WebAuthnChallengePurpose: 1 = registration, 2 = assertion
    [UserId] UNIQUEIDENTIFIER NULL,            -- the account a registration challenge belongs to; NULL for an assertion
    [OptionsJson] NVARCHAR(MAX) NOT NULL,      -- the options offered, challenge included: not secret, they went to the browser
    [ExpiresAt] DATETIME2 NOT NULL,            -- five minutes after it was issued
    [UsedAt] DATETIME2 NULL,                   -- set once, by the statement that spends it, before the response is checked
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_WebAuthnChallenges_CreatedAt] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_WebAuthnChallenges] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_WebAuthnChallenges_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [CK_WebAuthnChallenges_Purpose] CHECK ([Purpose] = 1 OR [Purpose] = 2)
);
GO

-- The challenges of the passkey ceremonies (S20, PK6, D-T17 A): single-use, five minutes,
-- held server-side so an app-pool recycle neither loses nor duplicates one. The browser
-- holds a random handle; only its keyed fingerprint is stored.

-- Lookup by every accepted fingerprint of a presented handle.
CREATE UNIQUE NONCLUSTERED INDEX [UX_WebAuthnChallenges_HandleHash]
ON [dbo].[WebAuthnChallenges] ([HandleHash]);
GO

-- An account's live registration challenge (the emailed code is minted only while one exists).
CREATE NONCLUSTERED INDEX [IX_WebAuthnChallenges_UserId]
ON [dbo].[WebAuthnChallenges] ([UserId], [Purpose], [ExpiresAt])
WHERE [UsedAt] IS NULL;
GO

-- Serves the retention sweep, which deletes spent and unspent challenges alike.
CREATE NONCLUSTERED INDEX [IX_WebAuthnChallenges_Cleanup]
ON [dbo].[WebAuthnChallenges] ([ExpiresAt]);
GO
