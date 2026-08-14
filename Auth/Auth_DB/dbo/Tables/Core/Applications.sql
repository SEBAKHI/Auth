CREATE TABLE [dbo].[Applications]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Applications_Id] DEFAULT NEWID(),
    [Code] NVARCHAR(50) NOT NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    [BaseUrl] NVARCHAR(500) NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [ContactEmail] NVARCHAR(255) NULL,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_Applications_IsActive] DEFAULT 1,
    [AllowSelfRegistration] BIT NOT NULL CONSTRAINT [DF_Applications_AllowSelfRegistration] DEFAULT 0,
    [RequireEmailVerification] BIT NOT NULL CONSTRAINT [DF_Applications_RequireEmailVerification] DEFAULT 0,
    [RequireTwoFactor] BIT NOT NULL CONSTRAINT [DF_Applications_RequireTwoFactor] DEFAULT 0,
    [SessionTimeoutMinutes] INT NOT NULL CONSTRAINT [DF_Applications_SessionTimeoutMinutes] DEFAULT 60,
    [MaxConcurrentSessions] INT NOT NULL CONSTRAINT [DF_Applications_MaxConcurrentSessions] DEFAULT 5,
    [ReauthenticationMaxAgeMinutes] INT NULL,   -- step-up: require re-auth if the SSO session is older than this (NULL = disabled)
    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_Applications_CreatedAt] DEFAULT GETUTCDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt] DATETIME2 NULL,
    [ModifiedBy] UNIQUEIDENTIFIER NULL,
    [IsDeleted] BIT NOT NULL CONSTRAINT [DF_Applications_IsDeleted] DEFAULT 0,
    [DeletedAt] DATETIME2 NULL,
    [DeletedBy] UNIQUEIDENTIFIER NULL,
    [AccessMode] TINYINT NOT NULL CONSTRAINT [DF_Applications_AccessMode] DEFAULT 2,

    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UQ_Applications_Code] UNIQUE ([Code])
);
GO

-- IsActive and AccessMode are two independent switches, and IsActive wins:
--   IsActive  = is the application switched on at all? Off means nobody signs
--               in and no token refreshes, not even for invited users.
--   AccessMode= when it IS on, who may sign in?
--                 1 = Everyone   - any authenticated platform user
--                 2 = Restricted - only users with an active row in
--                                  ApplicationUserAccess (the default for new
--                                  applications; also implies the application
--                                  has no enabled organizations)

-- Indexes
CREATE NONCLUSTERED INDEX [IX_Applications_Code]
ON [dbo].[Applications] ([Code])
WHERE [IsDeleted] = 0;
GO

CREATE NONCLUSTERED INDEX [IX_Applications_IsActive]
ON [dbo].[Applications] ([IsActive]);
GO

CREATE NONCLUSTERED INDEX [IX_Applications_AccessMode]
ON [dbo].[Applications] ([AccessMode])
WHERE [IsDeleted] = 0;
GO
