CREATE TABLE [dbo].[AuthorizationRequests]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AuthorizationRequests_Id] DEFAULT NEWID(),
    [HandleHash] NVARCHAR(500) NOT NULL,      -- HMAC-SHA256 hash of the handle; the plain handle is never stored
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    -- The authorize parameters as the handler validated them, one column each.
    -- Never the raw query string: every value here already passed the checks a
    -- resume re-runs, and each column is bounded.
    [ClientId] NVARCHAR(100) NOT NULL,        -- client_id as sent (an application code)
    [ResponseType] NVARCHAR(20) NOT NULL,     -- response_type ("code")
    [RedirectUri] NVARCHAR(500) NOT NULL,     -- redirect_uri, an exact registered URI
    [CodeChallenge] NVARCHAR(128) NOT NULL,   -- code_challenge (PKCE S256)
    [CodeChallengeMethod] NVARCHAR(10) NOT NULL, -- code_challenge_method ("S256")
    [State] NVARCHAR(512) NULL,               -- state, verbatim
    [Prompt] NVARCHAR(50) NULL,               -- prompt, as the recognised values in canonical order
    [MaxAge] INT NULL,                        -- max_age, parsed; NULL when absent or malformed
    [Scope] NVARCHAR(200) NOT NULL,           -- scope, as the canonical requested set
    [CreateOrganization] BIT NOT NULL,        -- create_organization=true
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_AuthorizationRequests_CreatedAt] DEFAULT GETUTCDATE(),
    [ExpiresAt] DATETIME2 NOT NULL,

    CONSTRAINT [PK_AuthorizationRequests] PRIMARY KEY CLUSTERED ([Id]),
    -- Pending requests are throwaway artifacts scoped to the client, like the
    -- codes they lead to, so they cascade away with the application.
    CONSTRAINT [FK_AuthorizationRequests_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id]) ON DELETE CASCADE
);
GO

-- A pending OAuth authorize request, kept while the browser passes through an
-- accounts page (sign-in, registration, organization creation). The page URL
-- carries only client_id and a 256-bit handle (64 lowercase hex), not the whole
-- authorize URL a web application firewall reads as a remote file inclusion
-- (OI-145). Expiry is fixed at creation
-- (IdentityProvider:AuthorizationRequestLifetimeMinutes) and never slides. A row
-- is not deleted when used: the authorize URL it replaces could always be
-- replayed, and the daily sweep removes it once expired.

CREATE UNIQUE NONCLUSTERED INDEX [UX_AuthorizationRequests_HandleHash]
ON [dbo].[AuthorizationRequests] ([HandleHash]);
GO

CREATE NONCLUSTERED INDEX [IX_AuthorizationRequests_ExpiresAt]
ON [dbo].[AuthorizationRequests] ([ExpiresAt]);
GO
