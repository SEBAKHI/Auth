-- The jti of every client assertion the token endpoint accepted (OI-128), so that each assertion is
-- accepted once (RFC 7523 §3, item 7). A row is written only after the assertion's signature has
-- been verified, by one conditional INSERT, and it is needed only until the assertion expires;
-- ExpiredDataCleanupWorker removes it after that.
--
-- Jti is compared byte for byte (BIN2): "abc" and "ABC" are two identifiers.
CREATE TABLE [dbo].[ClientAssertionJtis]
(
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Jti] NVARCHAR(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [ExpiresAt] DATETIME2 NOT NULL,

    CONSTRAINT [PK_ClientAssertionJtis] PRIMARY KEY CLUSTERED ([ApplicationId], [Jti]),
    CONSTRAINT [FK_ClientAssertionJtis_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications]([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_ClientAssertionJtis_ExpiresAt] ON [dbo].[ClientAssertionJtis] ([ExpiresAt]);
GO
