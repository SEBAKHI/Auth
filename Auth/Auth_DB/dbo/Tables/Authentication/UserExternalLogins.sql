CREATE TABLE [dbo].[UserExternalLogins]
(
    [Id]              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    [UserId]          UNIQUEIDENTIFIER NOT NULL,
    [Provider]        NVARCHAR(50)     NOT NULL,  -- 'google', 'apple', 'facebook', etc.
    [ProviderUserId]  NVARCHAR(255)    NOT NULL,  -- Provider's unique user ID (e.g., Google 'sub')
    [Email]           NVARCHAR(255)    NULL,       -- Email from provider
    [Name]            NVARCHAR(200)    NULL,       -- Display name from provider
    [PictureUrl]      NVARCHAR(500)    NULL,       -- Profile picture URL
    [CreatedAt]       DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    [ModifiedAt]      DATETIME2        NULL,

    CONSTRAINT [PK_UserExternalLogins] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserExternalLogins_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_UserExternalLogins_Provider_ProviderUserId] UNIQUE ([Provider], [ProviderUserId]),
    CONSTRAINT [UQ_UserExternalLogins_UserId_Provider] UNIQUE ([UserId], [Provider])
);
GO

CREATE NONCLUSTERED INDEX [IX_UserExternalLogins_UserId]
ON [dbo].[UserExternalLogins] ([UserId]);
GO

CREATE NONCLUSTERED INDEX [IX_UserExternalLogins_Provider_ProviderUserId]
ON [dbo].[UserExternalLogins] ([Provider], [ProviderUserId]);
GO
