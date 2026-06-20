/*
=============================================================
  Shared Hosting Deployment Script for astoom_identity_prod
  
  Generated from Auth_DB project.
  This script contains ONLY object-level DDL (tables, indexes,
  defaults, foreign keys, stored procedures) and seed data.
  
  All database-level operations (DROP/CREATE/ALTER DATABASE)
  have been removed for shared hosting compatibility.
  
  HOW TO USE:
  1. Connect to astoom_identity_prod via SSMS, Plesk SQL Manager,
     or any SQL client using the etrack_astoom_identity_rw_prod credentials.
  2. Make sure you are in the correct database context.
  3. Execute this entire script.
=============================================================
*/

SET ANSI_NULLS, ANSI_PADDING, ANSI_WARNINGS, ARITHABORT, CONCAT_NULL_YIELDS_NULL, QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- ============================================
-- TABLES
-- ============================================

PRINT N'Creating Table [dbo].[AuditLogs]...';
GO
CREATE TABLE [dbo].[AuditLogs] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [UserId]        UNIQUEIDENTIFIER NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [SessionId]     UNIQUEIDENTIFIER NULL,
    [Action]        NVARCHAR (100)   NOT NULL,
    [EntityType]    NVARCHAR (100)   NULL,
    [EntityId]      UNIQUEIDENTIFIER NULL,
    [OldValues]     NVARCHAR (MAX)   NULL,
    [NewValues]     NVARCHAR (MAX)   NULL,
    [IpAddress]     NVARCHAR (45)    NULL,
    [UserAgent]     NVARCHAR (500)   NULL,
    [Details]       NVARCHAR (MAX)   NULL,
    [Timestamp]     DATETIME2 (7)    NOT NULL,
    [PerformedBy]   UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[LoginAttempts]...';
GO
CREATE TABLE [dbo].[LoginAttempts] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [UserId]        UNIQUEIDENTIFIER NULL,
    [Username]      NVARCHAR (255)   NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [IpAddress]     NVARCHAR (45)    NOT NULL,
    [UserAgent]     NVARCHAR (500)   NULL,
    [AttemptedAt]   DATETIME2 (7)    NOT NULL,
    [IsSuccessful]  BIT              NOT NULL,
    [FailureReason] NVARCHAR (100)   NULL,
    CONSTRAINT [PK_LoginAttempts] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[UserSessions]...';
GO
CREATE TABLE [dbo].[UserSessions] (
    [Id]             UNIQUEIDENTIFIER NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId]  UNIQUEIDENTIFIER NULL,
    [SessionToken]   NVARCHAR (500)   NOT NULL,
    [IpAddress]      NVARCHAR (45)    NOT NULL,
    [UserAgent]      NVARCHAR (500)   NULL,
    [DeviceType]     NVARCHAR (50)    NULL,
    [Location]       NVARCHAR (200)   NULL,
    [StartedAt]      DATETIME2 (7)    NOT NULL,
    [LastActivityAt] DATETIME2 (7)    NOT NULL,
    [ExpiresAt]      DATETIME2 (7)    NOT NULL,
    [EndedAt]        DATETIME2 (7)    NULL,
    [EndReason]      NVARCHAR (100)   NULL,
    CONSTRAINT [PK_UserSessions] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[PermissionImplications]...';
GO
CREATE TABLE [dbo].[PermissionImplications] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL,
    [PermissionId]        UNIQUEIDENTIFIER NOT NULL,
    [ImpliedPermissionId] UNIQUEIDENTIFIER NOT NULL,
    [CreatedAt]           DATETIME2 (7)    NOT NULL,
    [CreatedBy]           UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_PermissionImplications] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_PermissionImplication] UNIQUE NONCLUSTERED ([PermissionId] ASC, [ImpliedPermissionId] ASC)
);
GO

PRINT N'Creating Table [dbo].[UserPermissions]...';
GO
CREATE TABLE [dbo].[UserPermissions] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [UserId]        UNIQUEIDENTIFIER NOT NULL,
    [PermissionId]  UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [GrantedAt]     DATETIME2 (7)    NOT NULL,
    [GrantedBy]     UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]     DATETIME2 (7)    NULL,
    [IsActive]      BIT              NOT NULL,
    CONSTRAINT [PK_UserPermissions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_UserPermissions] UNIQUE NONCLUSTERED ([UserId] ASC, [PermissionId] ASC, [ApplicationId] ASC)
);
GO

PRINT N'Creating Table [dbo].[RolePermissions]...';
GO
CREATE TABLE [dbo].[RolePermissions] (
    [Id]           UNIQUEIDENTIFIER NOT NULL,
    [RoleId]       UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [GrantedAt]    DATETIME2 (7)    NOT NULL,
    [GrantedBy]    UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_RolePermissions] UNIQUE NONCLUSTERED ([RoleId] ASC, [PermissionId] ASC)
);
GO

PRINT N'Creating Table [dbo].[UserRoles]...';
GO
CREATE TABLE [dbo].[UserRoles] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [UserId]        UNIQUEIDENTIFIER NOT NULL,
    [RoleId]        UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [AssignedAt]    DATETIME2 (7)    NOT NULL,
    [AssignedBy]    UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]     DATETIME2 (7)    NULL,
    [IsActive]      BIT              NOT NULL,
    CONSTRAINT [PK_UserRoles] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_UserRoles] UNIQUE NONCLUSTERED ([UserId] ASC, [RoleId] ASC, [ApplicationId] ASC)
);
GO

PRINT N'Creating Table [dbo].[Permissions]...';
GO
CREATE TABLE [dbo].[Permissions] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [Code]          NVARCHAR (100)   NOT NULL,
    [Name]          NVARCHAR (200)   NOT NULL,
    [Description]   NVARCHAR (500)   NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [ParentId]      UNIQUEIDENTIFIER NULL,
    [Level]         TINYINT          NOT NULL,
    [IsWildcard]    BIT              NOT NULL,
    [IsActive]      BIT              NOT NULL,
    [CreatedAt]     DATETIME2 (7)    NOT NULL,
    [CreatedBy]     UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]    DATETIME2 (7)    NULL,
    [ModifiedBy]    UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Permissions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_Permissions_Code] UNIQUE NONCLUSTERED ([Code] ASC)
);
GO

PRINT N'Creating Table [dbo].[Roles]...';
GO
CREATE TABLE [dbo].[Roles] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [Code]          NVARCHAR (100)   NOT NULL,
    [Name]          NVARCHAR (200)   NOT NULL,
    [Description]   NVARCHAR (500)   NULL,
    [ApplicationId] UNIQUEIDENTIFIER NULL,
    [IsSystem]      BIT              NOT NULL,
    [IsActive]      BIT              NOT NULL,
    [CreatedAt]     DATETIME2 (7)    NOT NULL,
    [CreatedBy]     UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]    DATETIME2 (7)    NULL,
    [ModifiedBy]    UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Roles] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_Roles_Code_Application] UNIQUE NONCLUSTERED ([Code] ASC, [ApplicationId] ASC)
);
GO

PRINT N'Creating Table [dbo].[PasswordHistory]...';
GO
CREATE TABLE [dbo].[PasswordHistory] (
    [Id]           UNIQUEIDENTIFIER NOT NULL,
    [UserId]       UNIQUEIDENTIFIER NOT NULL,
    [PasswordHash] NVARCHAR (500)   NOT NULL,
    [CreatedAt]    DATETIME2 (7)    NOT NULL,
    CONSTRAINT [PK_PasswordHistory] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[RefreshTokens]...';
GO
CREATE TABLE [dbo].[RefreshTokens] (
    [Id]                  UNIQUEIDENTIFIER NOT NULL,
    [UserId]              UNIQUEIDENTIFIER NOT NULL,
    [TokenHash]           NVARCHAR (100)   NOT NULL,
    [JwtId]               NVARCHAR (100)   NOT NULL,
    [ApplicationId]       UNIQUEIDENTIFIER NULL,
    [DeviceInfo]          NVARCHAR (500)   NULL,
    [IpAddress]           NVARCHAR (45)    NULL,
    [CreatedAt]           DATETIME2 (7)    NOT NULL,
    [ExpiresAt]           DATETIME2 (7)    NOT NULL,
    [RevokedAt]           DATETIME2 (7)    NULL,
    [RevokedBy]           UNIQUEIDENTIFIER NULL,
    [ReplacedByTokenHash] NVARCHAR (100)   NULL,
    [ReasonRevoked]       NVARCHAR (200)   NULL,
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[OrganizationUserRoles]...';
GO
CREATE TABLE [dbo].[OrganizationUserRoles] (
    [Id]             UNIQUEIDENTIFIER NOT NULL,
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId]  UNIQUEIDENTIFIER NOT NULL,
    [RoleId]         UNIQUEIDENTIFIER NOT NULL,
    [IsActive]       BIT              NOT NULL,
    [AssignedAt]     DATETIME2 (7)    NOT NULL,
    [AssignedBy]     UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]      DATETIME2 (7)    NULL,
    [CreatedAt]      DATETIME2 (7)    NOT NULL,
    [CreatedBy]      UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]     DATETIME2 (7)    NULL,
    [ModifiedBy]     UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_OrganizationUserRoles] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_OrganizationUserRoles] UNIQUE NONCLUSTERED ([OrganizationId] ASC, [UserId] ASC, [ApplicationId] ASC, [RoleId] ASC)
);
GO

PRINT N'Creating Table [dbo].[OrganizationApplications]...';
GO
CREATE TABLE [dbo].[OrganizationApplications] (
    [Id]               UNIQUEIDENTIFIER NOT NULL,
    [OrganizationId]   UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId]    UNIQUEIDENTIFIER NOT NULL,
    [IsActive]         BIT              NOT NULL,
    [EnabledAt]        DATETIME2 (7)    NOT NULL,
    [EnabledBy]        UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]        DATETIME2 (7)    NULL,
    [SubscriptionTier] NVARCHAR (50)    NULL,
    [CreatedAt]        DATETIME2 (7)    NOT NULL,
    [CreatedBy]        UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]       DATETIME2 (7)    NULL,
    [ModifiedBy]       UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_OrganizationApplications] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_OrganizationApplications] UNIQUE NONCLUSTERED ([OrganizationId] ASC, [ApplicationId] ASC)
);
GO

PRINT N'Creating Table [dbo].[OrganizationUsers]...';
GO
CREATE TABLE [dbo].[OrganizationUsers] (
    [Id]             UNIQUEIDENTIFIER NOT NULL,
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [RoleId]         UNIQUEIDENTIFIER NOT NULL,
    [IsActive]       BIT              NOT NULL,
    [JoinedAt]       DATETIME2 (7)    NOT NULL,
    [InvitedBy]      UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]      DATETIME2 (7)    NULL,
    [CreatedAt]      DATETIME2 (7)    NOT NULL,
    [CreatedBy]      UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]     DATETIME2 (7)    NULL,
    [ModifiedBy]     UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_OrganizationUsers] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_OrganizationUsers] UNIQUE NONCLUSTERED ([OrganizationId] ASC, [UserId] ASC)
);
GO

PRINT N'Creating Table [dbo].[ApiKeyScopes]...';
GO
CREATE TABLE [dbo].[ApiKeyScopes] (
    [Id]           UNIQUEIDENTIFIER NOT NULL,
    [ApiKeyId]     UNIQUEIDENTIFIER NOT NULL,
    [PermissionId] UNIQUEIDENTIFIER NOT NULL,
    [GrantedAt]    DATETIME2 (7)    NOT NULL,
    [GrantedBy]    UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_ApiKeyScopes] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_ApiKeyScopes] UNIQUE NONCLUSTERED ([ApiKeyId] ASC, [PermissionId] ASC)
);
GO

PRINT N'Creating Table [dbo].[ApiKeys]...';
GO
CREATE TABLE [dbo].[ApiKeys] (
    [Id]                 UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId]      UNIQUEIDENTIFIER NOT NULL,
    [Name]               NVARCHAR (200)   NOT NULL,
    [Description]        NVARCHAR (500)   NULL,
    [KeyPrefix]          NVARCHAR (10)    NOT NULL,
    [KeyHash]            NVARCHAR (500)   NOT NULL,
    [Environment]        NVARCHAR (20)    NOT NULL,
    [RateLimitPerMinute] INT              NOT NULL,
    [RateLimitPerDay]    INT              NOT NULL,
    [AllowedIps]         NVARCHAR (MAX)   NULL,
    [AllowedOrigins]     NVARCHAR (MAX)   NULL,
    [CreatedAt]          DATETIME2 (7)    NOT NULL,
    [CreatedBy]          UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]          DATETIME2 (7)    NULL,
    [LastUsedAt]         DATETIME2 (7)    NULL,
    [RevokedAt]          DATETIME2 (7)    NULL,
    [RevokedBy]          UNIQUEIDENTIFIER NULL,
    [RevokeReason]       NVARCHAR (200)   NULL,
    CONSTRAINT [PK_ApiKeys] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[TwoFactorAuth]...';
GO
CREATE TABLE [dbo].[TwoFactorAuth] (
    [Id]             UNIQUEIDENTIFIER NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [SecretKey]      NVARCHAR (200)   NOT NULL,
    [RecoveryCodes]  NVARCHAR (MAX)   NULL,
    [IsEnabled]      BIT              NOT NULL,
    [EnabledAt]      DATETIME2 (7)    NULL,
    [LastUsedAt]     DATETIME2 (7)    NULL,
    [FailedAttempts] INT              NOT NULL,
    [LockedUntil]    DATETIME2 (7)    NULL,
    [CreatedAt]      DATETIME2 (7)    NOT NULL,
    [ModifiedAt]     DATETIME2 (7)    NULL,
    CONSTRAINT [PK_TwoFactorAuth] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_TwoFactorAuth_UserId] UNIQUE NONCLUSTERED ([UserId] ASC)
);
GO

PRINT N'Creating Table [dbo].[EmailVerificationTokens]...';
GO
CREATE TABLE [dbo].[EmailVerificationTokens] (
    [Id]           UNIQUEIDENTIFIER NOT NULL,
    [UserId]       UNIQUEIDENTIFIER NOT NULL,
    [OtpHash]      NVARCHAR (500)   NOT NULL,
    [Email]        NVARCHAR (255)   NOT NULL,
    [ExpiresAt]    DATETIME2 (7)    NOT NULL,
    [UsedAt]       DATETIME2 (7)    NULL,
    [AttemptCount] INT              NOT NULL,
    [CreatedAt]    DATETIME2 (7)    NOT NULL,
    CONSTRAINT [PK_EmailVerificationTokens] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[OrganizationInvitations]...';
GO
CREATE TABLE [dbo].[OrganizationInvitations] (
    [Id]               UNIQUEIDENTIFIER NOT NULL,
    [OrganizationId]   UNIQUEIDENTIFIER NOT NULL,
    [Email]            NVARCHAR (255)   NOT NULL,
    [RoleId]           UNIQUEIDENTIFIER NOT NULL,
    [Token]            NVARCHAR (500)   NOT NULL,
    [Status]           NVARCHAR (20)    NOT NULL,
    [ExpiresAt]        DATETIME2 (7)    NOT NULL,
    [InvitedBy]        UNIQUEIDENTIFIER NOT NULL,
    [AcceptedAt]       DATETIME2 (7)    NULL,
    [AcceptedByUserId] UNIQUEIDENTIFIER NULL,
    [CreatedAt]        DATETIME2 (7)    NOT NULL,
    CONSTRAINT [PK_OrganizationInvitations] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_OrganizationInvitations_Token] UNIQUE NONCLUSTERED ([Token] ASC)
);
GO

PRINT N'Creating Table [dbo].[OrganizationUserPermissions]...';
GO
CREATE TABLE [dbo].[OrganizationUserPermissions] (
    [Id]             UNIQUEIDENTIFIER NOT NULL,
    [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId]  UNIQUEIDENTIFIER NOT NULL,
    [PermissionId]   UNIQUEIDENTIFIER NOT NULL,
    [IsActive]       BIT              NOT NULL,
    [GrantedAt]      DATETIME2 (7)    NOT NULL,
    [GrantedBy]      UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]      DATETIME2 (7)    NULL,
    [CreatedAt]      DATETIME2 (7)    NOT NULL,
    [CreatedBy]      UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]     DATETIME2 (7)    NULL,
    [ModifiedBy]     UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_OrganizationUserPermissions] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_OrganizationUserPermissions] UNIQUE NONCLUSTERED ([OrganizationId] ASC, [UserId] ASC, [ApplicationId] ASC, [PermissionId] ASC)
);
GO

PRINT N'Creating Table [dbo].[PasswordResetTokens]...';
GO
CREATE TABLE [dbo].[PasswordResetTokens] (
    [Id]        UNIQUEIDENTIFIER NOT NULL,
    [UserId]    UNIQUEIDENTIFIER NOT NULL,
    [TokenHash] NVARCHAR (500)   NOT NULL,
    [ExpiresAt] DATETIME2 (7)    NOT NULL,
    [UsedAt]    DATETIME2 (7)    NULL,
    [CreatedAt] DATETIME2 (7)    NOT NULL,
    CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

PRINT N'Creating Table [dbo].[Applications]...';
GO
CREATE TABLE [dbo].[Applications] (
    [Id]                       UNIQUEIDENTIFIER NOT NULL,
    [Code]                     NVARCHAR (50)    NOT NULL,
    [Name]                     NVARCHAR (200)   NOT NULL,
    [Description]              NVARCHAR (500)   NULL,
    [BaseUrl]                  NVARCHAR (500)   NULL,
    [LogoUrl]                  NVARCHAR (500)   NULL,
    [ContactEmail]             NVARCHAR (255)   NULL,
    [IsActive]                 BIT              NOT NULL,
    [AllowSelfRegistration]    BIT              NOT NULL,
    [RequireEmailVerification] BIT              NOT NULL,
    [RequireTwoFactor]         BIT              NOT NULL,
    [SessionTimeoutMinutes]    INT              NOT NULL,
    [MaxConcurrentSessions]    INT              NOT NULL,
    [CreatedAt]                DATETIME2 (7)    NOT NULL,
    [CreatedBy]                UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]               DATETIME2 (7)    NULL,
    [ModifiedBy]               UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_Applications_Code] UNIQUE NONCLUSTERED ([Code] ASC)
);
GO

PRINT N'Creating Table [dbo].[Organizations]...';
GO
CREATE TABLE [dbo].[Organizations] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [Code]          NVARCHAR (100)   NOT NULL,
    [Name]          NVARCHAR (255)   NOT NULL,
    [Description]   NVARCHAR (1000)  NULL,
    [LogoUrl]       NVARCHAR (500)   NULL,
    [Website]       NVARCHAR (500)   NULL,
    [ContactEmail]  NVARCHAR (255)   NOT NULL,
    [OwnerId]       UNIQUEIDENTIFIER NOT NULL,
    [IsActive]      BIT              NOT NULL,
    [IsAutoCreated] BIT              NOT NULL,
    [CreatedAt]     DATETIME2 (7)    NOT NULL,
    [CreatedBy]     UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]    DATETIME2 (7)    NULL,
    [ModifiedBy]    UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Organizations] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_Organizations_Code] UNIQUE NONCLUSTERED ([Code] ASC)
);
GO

PRINT N'Creating Table [dbo].[Users]...';
GO
CREATE TABLE [dbo].[Users] (
    [Id]                    UNIQUEIDENTIFIER NOT NULL,
    [Username]              NVARCHAR (50)    NOT NULL,
    [Email]                 NVARCHAR (255)   NOT NULL,
    [NormalizedEmail]       NVARCHAR (255)   NOT NULL,
    [PasswordHash]          NVARCHAR (500)   NULL,
    [FirstName]             NVARCHAR (100)   NULL,
    [LastName]              NVARCHAR (100)   NULL,
    [FullName]              AS               (ISNULL([FirstName], N'') + N' ' + ISNULL([LastName], N'')) PERSISTED,
    [PhoneNumber]           NVARCHAR (20)    NULL,
    [ProfileImageUrl]       NVARCHAR (500)   NULL,
    [PreferredLanguage]     NVARCHAR (10)    NOT NULL,
    [TimeZone]              NVARCHAR (50)    NOT NULL,
    [IsEmailConfirmed]      BIT              NOT NULL,
    [IsPhoneConfirmed]      BIT              NOT NULL,
    [IsTwoFactorEnabled]    BIT              NOT NULL,
    [Status]                TINYINT          NOT NULL,
    [FailedLoginAttempts]   INT              NOT NULL,
    [LockoutEndUtc]         DATETIME2 (7)    NULL,
    [LastLoginUtc]          DATETIME2 (7)    NULL,
    [LastLoginIp]           NVARCHAR (45)    NULL,
    [LastPasswordChangeUtc] DATETIME2 (7)    NULL,
    [MustChangePassword]    BIT              NOT NULL,
    [PasswordExpiresUtc]    DATETIME2 (7)    NULL,
    [SecurityStamp]         NVARCHAR (100)   NOT NULL,
    [ConcurrencyStamp]      NVARCHAR (100)   NOT NULL,
    [CreatedAt]             DATETIME2 (7)    NOT NULL,
    [CreatedBy]             UNIQUEIDENTIFIER NOT NULL,
    [ModifiedAt]            DATETIME2 (7)    NULL,
    [ModifiedBy]            UNIQUEIDENTIFIER NULL,
    [IsDeleted]             BIT              NOT NULL,
    [DeletedAt]             DATETIME2 (7)    NULL,
    [DeletedBy]             UNIQUEIDENTIFIER NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_Users_NormalizedEmail] UNIQUE NONCLUSTERED ([NormalizedEmail] ASC),
    CONSTRAINT [UQ_Users_Username] UNIQUE NONCLUSTERED ([Username] ASC)
);
GO

PRINT N'Creating Table [dbo].[UserExternalLogins]...';
GO
CREATE TABLE [dbo].[UserExternalLogins] (
    [Id]             UNIQUEIDENTIFIER NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [Provider]       NVARCHAR (50)    NOT NULL,
    [ProviderUserId] NVARCHAR (255)   NOT NULL,
    [Email]          NVARCHAR (255)   NULL,
    [Name]           NVARCHAR (200)   NULL,
    [PictureUrl]     NVARCHAR (500)   NULL,
    [CreatedAt]      DATETIME2 (7)    NOT NULL,
    [ModifiedAt]     DATETIME2 (7)    NULL,
    CONSTRAINT [PK_UserExternalLogins] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_UserExternalLogins_Provider_ProviderUserId] UNIQUE NONCLUSTERED ([Provider] ASC, [ProviderUserId] ASC),
    CONSTRAINT [UQ_UserExternalLogins_UserId_Provider] UNIQUE NONCLUSTERED ([UserId] ASC, [Provider] ASC)
);
GO

PRINT N'Creating Table [dbo].[ExternalAuthProviders]...';
GO
CREATE TABLE [dbo].[ExternalAuthProviders] (
    [Id]           UNIQUEIDENTIFIER NOT NULL,
    [Code]         NVARCHAR (50)    NOT NULL,
    [Name]         NVARCHAR (100)   NOT NULL,
    [IconUrl]      NVARCHAR (500)   NULL,
    [IsEnabled]    BIT              NOT NULL,
    [DisplayOrder] INT              NOT NULL,
    [CreatedAt]    DATETIME2 (7)    NOT NULL,
    [ModifiedAt]   DATETIME2 (7)    NULL,
    CONSTRAINT [PK_ExternalAuthProviders] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [UQ_ExternalAuthProviders_Code] UNIQUE NONCLUSTERED ([Code] ASC)
);
GO

PRINT N'Creating Table [dbo].[WebhookKeys]...';
GO
CREATE TABLE [dbo].[WebhookKeys] (
    [Id]            UNIQUEIDENTIFIER NOT NULL,
    [ApplicationId] UNIQUEIDENTIFIER NOT NULL,
    [Name]          NVARCHAR (200)   NOT NULL,
    [Description]   NVARCHAR (500)   NULL,
    [KeyPrefix]     NVARCHAR (10)    NOT NULL,
    [KeyHash]       NVARCHAR (500)   NOT NULL,
    [TargetUrl]     NVARCHAR (2000)  NOT NULL,
    [Environment]   NVARCHAR (20)    NOT NULL,
    [CreatedAt]     DATETIME2 (7)    NOT NULL,
    [CreatedBy]     UNIQUEIDENTIFIER NOT NULL,
    [ExpiresAt]     DATETIME2 (7)    NULL,
    [LastUsedAt]    DATETIME2 (7)    NULL,
    [RevokedAt]     DATETIME2 (7)    NULL,
    [RevokedBy]     UNIQUEIDENTIFIER NULL,
    [RevokeReason]  NVARCHAR (200)   NULL,
    CONSTRAINT [PK_WebhookKeys] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

-- ============================================
-- INDEXES
-- ============================================

PRINT N'Creating Indexes...';
GO

CREATE NONCLUSTERED INDEX [IX_AuditLogs_UserId] ON [dbo].[AuditLogs]([UserId] ASC, [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_Action] ON [dbo].[AuditLogs]([Action] ASC, [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_Timestamp] ON [dbo].[AuditLogs]([Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_ApplicationId] ON [dbo].[AuditLogs]([ApplicationId] ASC, [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_EntityType_EntityId] ON [dbo].[AuditLogs]([EntityType] ASC, [EntityId] ASC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_PerformedBy] ON [dbo].[AuditLogs]([PerformedBy] ASC, [Timestamp] DESC);
CREATE NONCLUSTERED INDEX [IX_AuditLogs_SessionId] ON [dbo].[AuditLogs]([SessionId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_LoginAttempts_UserId] ON [dbo].[LoginAttempts]([UserId] ASC, [AttemptedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_IpAddress] ON [dbo].[LoginAttempts]([IpAddress] ASC, [AttemptedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_AttemptedAt] ON [dbo].[LoginAttempts]([AttemptedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_Username] ON [dbo].[LoginAttempts]([Username] ASC, [AttemptedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_LoginAttempts_ApplicationId] ON [dbo].[LoginAttempts]([ApplicationId] ASC, [AttemptedAt] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_UserSessions_UserId] ON [dbo].[UserSessions]([UserId] ASC) WHERE [EndedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_UserSessions_SessionToken] ON [dbo].[UserSessions]([SessionToken] ASC);
CREATE NONCLUSTERED INDEX [IX_UserSessions_ExpiresAt] ON [dbo].[UserSessions]([ExpiresAt] ASC) WHERE [EndedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_UserSessions_ApplicationId] ON [dbo].[UserSessions]([ApplicationId] ASC) WHERE [EndedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_UserSessions_LastActivityAt] ON [dbo].[UserSessions]([LastActivityAt] DESC) WHERE [EndedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_PermImpl_PermissionId] ON [dbo].[PermissionImplications]([PermissionId] ASC);
CREATE NONCLUSTERED INDEX [IX_PermImpl_ImpliedPermissionId] ON [dbo].[PermissionImplications]([ImpliedPermissionId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_UserPermissions_UserId] ON [dbo].[UserPermissions]([UserId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_UserPermissions_PermissionId] ON [dbo].[UserPermissions]([PermissionId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_UserPermissions_ApplicationId] ON [dbo].[UserPermissions]([ApplicationId] ASC) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_RolePermissions_RoleId] ON [dbo].[RolePermissions]([RoleId] ASC);
CREATE NONCLUSTERED INDEX [IX_RolePermissions_PermissionId] ON [dbo].[RolePermissions]([PermissionId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_UserRoles_UserId] ON [dbo].[UserRoles]([UserId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_UserRoles_RoleId] ON [dbo].[UserRoles]([RoleId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_UserRoles_ApplicationId] ON [dbo].[UserRoles]([ApplicationId] ASC);
CREATE NONCLUSTERED INDEX [IX_UserRoles_ExpiresAt] ON [dbo].[UserRoles]([ExpiresAt] ASC) WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX [IX_Permissions_ParentId] ON [dbo].[Permissions]([ParentId] ASC);
CREATE NONCLUSTERED INDEX [IX_Permissions_ApplicationId] ON [dbo].[Permissions]([ApplicationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Permissions_Code] ON [dbo].[Permissions]([Code] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Permissions_Level] ON [dbo].[Permissions]([Level] ASC);
CREATE NONCLUSTERED INDEX [IX_Permissions_IsWildcard] ON [dbo].[Permissions]([IsWildcard] ASC) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_Roles_ApplicationId] ON [dbo].[Roles]([ApplicationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Roles_Code] ON [dbo].[Roles]([Code] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Roles_IsSystem] ON [dbo].[Roles]([IsSystem] ASC) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_PasswordHistory_UserId] ON [dbo].[PasswordHistory]([UserId] ASC, [CreatedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_PasswordHistory_CreatedAt] ON [dbo].[PasswordHistory]([CreatedAt] DESC);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_RefreshTokens_TokenHash] ON [dbo].[RefreshTokens]([TokenHash] ASC);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_UserId] ON [dbo].[RefreshTokens]([UserId] ASC);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_ExpiresAt] ON [dbo].[RefreshTokens]([ExpiresAt] ASC) WHERE [RevokedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_JwtId] ON [dbo].[RefreshTokens]([JwtId] ASC);
CREATE NONCLUSTERED INDEX [IX_RefreshTokens_ApplicationId] ON [dbo].[RefreshTokens]([ApplicationId] ASC) WHERE [RevokedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_OrganizationId] ON [dbo].[OrganizationUserRoles]([OrganizationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_UserId] ON [dbo].[OrganizationUserRoles]([UserId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_ApplicationId] ON [dbo].[OrganizationUserRoles]([ApplicationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_RoleId] ON [dbo].[OrganizationUserRoles]([RoleId] ASC);
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_ExpiresAt] ON [dbo].[OrganizationUserRoles]([ExpiresAt] ASC) WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserRoles_Lookup] ON [dbo].[OrganizationUserRoles]([UserId] ASC, [ApplicationId] ASC, [OrganizationId] ASC) INCLUDE([RoleId]) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationApplications_OrganizationId] ON [dbo].[OrganizationApplications]([OrganizationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationApplications_ApplicationId] ON [dbo].[OrganizationApplications]([ApplicationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationApplications_ExpiresAt] ON [dbo].[OrganizationApplications]([ExpiresAt] ASC) WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_OrganizationId] ON [dbo].[OrganizationUsers]([OrganizationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_UserId] ON [dbo].[OrganizationUsers]([UserId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_RoleId] ON [dbo].[OrganizationUsers]([RoleId] ASC);
CREATE NONCLUSTERED INDEX [IX_OrganizationUsers_ExpiresAt] ON [dbo].[OrganizationUsers]([ExpiresAt] ASC) WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
GO

CREATE NONCLUSTERED INDEX [IX_ApiKeyScopes_ApiKeyId] ON [dbo].[ApiKeyScopes]([ApiKeyId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_ApiKeys_KeyHash] ON [dbo].[ApiKeys]([KeyHash] ASC) WHERE [RevokedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_ApiKeys_ApplicationId] ON [dbo].[ApiKeys]([ApplicationId] ASC) WHERE [RevokedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_ApiKeys_KeyPrefix] ON [dbo].[ApiKeys]([KeyPrefix] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_UserId] ON [dbo].[EmailVerificationTokens]([UserId] ASC, [CreatedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_ExpiresAt] ON [dbo].[EmailVerificationTokens]([ExpiresAt] ASC) WHERE [UsedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_EmailVerificationTokens_Email_CreatedAt] ON [dbo].[EmailVerificationTokens]([Email] ASC, [CreatedAt] DESC) WHERE [UsedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_OrganizationId] ON [dbo].[OrganizationInvitations]([OrganizationId] ASC) WHERE [Status] = 'Pending';
CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_Email] ON [dbo].[OrganizationInvitations]([Email] ASC) WHERE [Status] = 'Pending';
CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_Token] ON [dbo].[OrganizationInvitations]([Token] ASC) WHERE [Status] = 'Pending';
CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_ExpiresAt] ON [dbo].[OrganizationInvitations]([ExpiresAt] ASC) WHERE [Status] = 'Pending';
CREATE NONCLUSTERED INDEX [IX_OrganizationInvitations_Status] ON [dbo].[OrganizationInvitations]([Status] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_OrganizationId] ON [dbo].[OrganizationUserPermissions]([OrganizationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_UserId] ON [dbo].[OrganizationUserPermissions]([UserId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_ApplicationId] ON [dbo].[OrganizationUserPermissions]([ApplicationId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_PermissionId] ON [dbo].[OrganizationUserPermissions]([PermissionId] ASC);
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_ExpiresAt] ON [dbo].[OrganizationUserPermissions]([ExpiresAt] ASC) WHERE [IsActive] = 1 AND [ExpiresAt] IS NOT NULL;
CREATE NONCLUSTERED INDEX [IX_OrganizationUserPermissions_Lookup] ON [dbo].[OrganizationUserPermissions]([UserId] ASC, [ApplicationId] ASC, [OrganizationId] ASC) INCLUDE([PermissionId]) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_UserId] ON [dbo].[PasswordResetTokens]([UserId] ASC, [CreatedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_TokenHash] ON [dbo].[PasswordResetTokens]([TokenHash] ASC) WHERE [UsedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_PasswordResetTokens_ExpiresAt] ON [dbo].[PasswordResetTokens]([ExpiresAt] ASC) WHERE [UsedAt] IS NULL;
GO

CREATE NONCLUSTERED INDEX [IX_Applications_Code] ON [dbo].[Applications]([Code] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Applications_IsActive] ON [dbo].[Applications]([IsActive] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Organizations_Code] ON [dbo].[Organizations]([Code] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Organizations_OwnerId] ON [dbo].[Organizations]([OwnerId] ASC) WHERE [IsActive] = 1;
CREATE NONCLUSTERED INDEX [IX_Organizations_IsActive] ON [dbo].[Organizations]([IsActive] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_Users_NormalizedEmail] ON [dbo].[Users]([NormalizedEmail] ASC) WHERE [IsDeleted] = 0;
CREATE NONCLUSTERED INDEX [IX_Users_Username] ON [dbo].[Users]([Username] ASC) WHERE [IsDeleted] = 0;
CREATE NONCLUSTERED INDEX [IX_Users_Status] ON [dbo].[Users]([Status] ASC) WHERE [IsDeleted] = 0;
CREATE NONCLUSTERED INDEX [IX_Users_CreatedAt] ON [dbo].[Users]([CreatedAt] DESC);
CREATE NONCLUSTERED INDEX [IX_Users_LastLoginUtc] ON [dbo].[Users]([LastLoginUtc] DESC) WHERE [IsDeleted] = 0;
GO

CREATE NONCLUSTERED INDEX [IX_UserExternalLogins_UserId] ON [dbo].[UserExternalLogins]([UserId] ASC);
CREATE NONCLUSTERED INDEX [IX_UserExternalLogins_Provider_ProviderUserId] ON [dbo].[UserExternalLogins]([Provider] ASC, [ProviderUserId] ASC);
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_WebhookKeys_KeyHash] ON [dbo].[WebhookKeys]([KeyHash] ASC) WHERE [RevokedAt] IS NULL;
CREATE NONCLUSTERED INDEX [IX_WebhookKeys_ApplicationId] ON [dbo].[WebhookKeys]([ApplicationId] ASC) WHERE [RevokedAt] IS NULL;
GO

-- ============================================
-- DEFAULT CONSTRAINTS
-- ============================================

PRINT N'Creating Default Constraints...';
GO

ALTER TABLE [dbo].[AuditLogs] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[AuditLogs] ADD DEFAULT GETUTCDATE() FOR [Timestamp];
ALTER TABLE [dbo].[LoginAttempts] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[LoginAttempts] ADD DEFAULT GETUTCDATE() FOR [AttemptedAt];
ALTER TABLE [dbo].[LoginAttempts] ADD DEFAULT 0 FOR [IsSuccessful];
ALTER TABLE [dbo].[UserSessions] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[UserSessions] ADD DEFAULT GETUTCDATE() FOR [StartedAt];
ALTER TABLE [dbo].[UserSessions] ADD DEFAULT GETUTCDATE() FOR [LastActivityAt];
ALTER TABLE [dbo].[PermissionImplications] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[PermissionImplications] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[UserPermissions] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[UserPermissions] ADD DEFAULT GETUTCDATE() FOR [GrantedAt];
ALTER TABLE [dbo].[UserPermissions] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[RolePermissions] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[RolePermissions] ADD DEFAULT GETUTCDATE() FOR [GrantedAt];
ALTER TABLE [dbo].[UserRoles] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[UserRoles] ADD DEFAULT GETUTCDATE() FOR [AssignedAt];
ALTER TABLE [dbo].[UserRoles] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[Permissions] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[Permissions] ADD DEFAULT 0 FOR [Level];
ALTER TABLE [dbo].[Permissions] ADD DEFAULT 0 FOR [IsWildcard];
ALTER TABLE [dbo].[Permissions] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[Permissions] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[Roles] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[Roles] ADD DEFAULT 0 FOR [IsSystem];
ALTER TABLE [dbo].[Roles] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[Roles] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[PasswordHistory] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[PasswordHistory] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[RefreshTokens] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[RefreshTokens] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[OrganizationUserRoles] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[OrganizationUserRoles] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[OrganizationUserRoles] ADD DEFAULT GETUTCDATE() FOR [AssignedAt];
ALTER TABLE [dbo].[OrganizationUserRoles] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[OrganizationApplications] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[OrganizationApplications] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[OrganizationApplications] ADD DEFAULT GETUTCDATE() FOR [EnabledAt];
ALTER TABLE [dbo].[OrganizationApplications] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[OrganizationUsers] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[OrganizationUsers] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[OrganizationUsers] ADD DEFAULT GETUTCDATE() FOR [JoinedAt];
ALTER TABLE [dbo].[OrganizationUsers] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[ApiKeyScopes] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[ApiKeyScopes] ADD DEFAULT GETUTCDATE() FOR [GrantedAt];
ALTER TABLE [dbo].[ApiKeys] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[ApiKeys] ADD DEFAULT 'production' FOR [Environment];
ALTER TABLE [dbo].[ApiKeys] ADD DEFAULT 60 FOR [RateLimitPerMinute];
ALTER TABLE [dbo].[ApiKeys] ADD DEFAULT 10000 FOR [RateLimitPerDay];
ALTER TABLE [dbo].[ApiKeys] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[TwoFactorAuth] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[TwoFactorAuth] ADD DEFAULT 0 FOR [IsEnabled];
ALTER TABLE [dbo].[TwoFactorAuth] ADD DEFAULT 0 FOR [FailedAttempts];
ALTER TABLE [dbo].[TwoFactorAuth] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[EmailVerificationTokens] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[EmailVerificationTokens] ADD DEFAULT 0 FOR [AttemptCount];
ALTER TABLE [dbo].[EmailVerificationTokens] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[OrganizationInvitations] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[OrganizationInvitations] ADD DEFAULT 'Pending' FOR [Status];
ALTER TABLE [dbo].[OrganizationInvitations] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD DEFAULT GETUTCDATE() FOR [GrantedAt];
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[PasswordResetTokens] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[PasswordResetTokens] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[Applications] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[Applications] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[Applications] ADD DEFAULT 0 FOR [AllowSelfRegistration];
ALTER TABLE [dbo].[Applications] ADD DEFAULT 0 FOR [RequireEmailVerification];
ALTER TABLE [dbo].[Applications] ADD DEFAULT 0 FOR [RequireTwoFactor];
ALTER TABLE [dbo].[Applications] ADD DEFAULT 60 FOR [SessionTimeoutMinutes];
ALTER TABLE [dbo].[Applications] ADD DEFAULT 5 FOR [MaxConcurrentSessions];
ALTER TABLE [dbo].[Applications] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[Organizations] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[Organizations] ADD DEFAULT 1 FOR [IsActive];
ALTER TABLE [dbo].[Organizations] ADD DEFAULT 0 FOR [IsAutoCreated];
ALTER TABLE [dbo].[Organizations] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[Users] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[Users] ADD DEFAULT N'en' FOR [PreferredLanguage];
ALTER TABLE [dbo].[Users] ADD DEFAULT N'UTC' FOR [TimeZone];
ALTER TABLE [dbo].[Users] ADD DEFAULT 0 FOR [IsEmailConfirmed];
ALTER TABLE [dbo].[Users] ADD DEFAULT 0 FOR [IsPhoneConfirmed];
ALTER TABLE [dbo].[Users] ADD DEFAULT 0 FOR [IsTwoFactorEnabled];
ALTER TABLE [dbo].[Users] ADD DEFAULT 1 FOR [Status];
ALTER TABLE [dbo].[Users] ADD DEFAULT 0 FOR [FailedLoginAttempts];
ALTER TABLE [dbo].[Users] ADD DEFAULT 0 FOR [MustChangePassword];
ALTER TABLE [dbo].[Users] ADD DEFAULT CAST(NEWID() AS NVARCHAR(100)) FOR [SecurityStamp];
ALTER TABLE [dbo].[Users] ADD DEFAULT CAST(NEWID() AS NVARCHAR(100)) FOR [ConcurrencyStamp];
ALTER TABLE [dbo].[Users] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[Users] ADD DEFAULT 0 FOR [IsDeleted];
ALTER TABLE [dbo].[UserExternalLogins] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[UserExternalLogins] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[ExternalAuthProviders] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[ExternalAuthProviders] ADD DEFAULT 1 FOR [IsEnabled];
ALTER TABLE [dbo].[ExternalAuthProviders] ADD DEFAULT 0 FOR [DisplayOrder];
ALTER TABLE [dbo].[ExternalAuthProviders] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
ALTER TABLE [dbo].[WebhookKeys] ADD DEFAULT NEWID() FOR [Id];
ALTER TABLE [dbo].[WebhookKeys] ADD DEFAULT 'production' FOR [Environment];
ALTER TABLE [dbo].[WebhookKeys] ADD DEFAULT GETUTCDATE() FOR [CreatedAt];
GO

-- ============================================
-- FOREIGN KEYS
-- ============================================

PRINT N'Creating Foreign Keys...';
GO

ALTER TABLE [dbo].[LoginAttempts] ADD CONSTRAINT [FK_LoginAttempts_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[LoginAttempts] ADD CONSTRAINT [FK_LoginAttempts_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[UserSessions] ADD CONSTRAINT [FK_UserSessions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[UserSessions] ADD CONSTRAINT [FK_UserSessions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[PermissionImplications] ADD CONSTRAINT [FK_PermImpl_Permission] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[PermissionImplications] ADD CONSTRAINT [FK_PermImpl_Implied] FOREIGN KEY ([ImpliedPermissionId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[UserPermissions] ADD CONSTRAINT [FK_UserPermissions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[UserPermissions] ADD CONSTRAINT [FK_UserPermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[UserPermissions] ADD CONSTRAINT [FK_UserPermissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]);
ALTER TABLE [dbo].[RolePermissions] ADD CONSTRAINT [FK_RolePermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[UserRoles] ADD CONSTRAINT [FK_UserRoles_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[UserRoles] ADD CONSTRAINT [FK_UserRoles_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]);
ALTER TABLE [dbo].[UserRoles] ADD CONSTRAINT [FK_UserRoles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[Permissions] ADD CONSTRAINT [FK_Permissions_Parent] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[Permissions] ADD CONSTRAINT [FK_Permissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[Roles] ADD CONSTRAINT [FK_Roles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[PasswordHistory] ADD CONSTRAINT [FK_PasswordHistory_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[RefreshTokens] ADD CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[RefreshTokens] ADD CONSTRAINT [FK_RefreshTokens_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[OrganizationUserRoles] ADD CONSTRAINT [FK_OrganizationUserRoles_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[OrganizationUserRoles] ADD CONSTRAINT [FK_OrganizationUserRoles_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationUserRoles] ADD CONSTRAINT [FK_OrganizationUserRoles_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[OrganizationUserRoles] ADD CONSTRAINT [FK_OrganizationUserRoles_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]);
ALTER TABLE [dbo].[OrganizationUserRoles] ADD CONSTRAINT [FK_OrganizationUserRoles_AssignedBy] FOREIGN KEY ([AssignedBy]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationApplications] ADD CONSTRAINT [FK_OrganizationApplications_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[OrganizationApplications] ADD CONSTRAINT [FK_OrganizationApplications_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[OrganizationApplications] ADD CONSTRAINT [FK_OrganizationApplications_EnabledBy] FOREIGN KEY ([EnabledBy]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationUsers] ADD CONSTRAINT [FK_OrganizationUsers_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[OrganizationUsers] ADD CONSTRAINT [FK_OrganizationUsers_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationUsers] ADD CONSTRAINT [FK_OrganizationUsers_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]);
ALTER TABLE [dbo].[OrganizationUsers] ADD CONSTRAINT [FK_OrganizationUsers_InvitedBy] FOREIGN KEY ([InvitedBy]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[ApiKeyScopes] ADD CONSTRAINT [FK_ApiKeyScopes_ApiKeys] FOREIGN KEY ([ApiKeyId]) REFERENCES [dbo].[ApiKeys] ([Id]);
ALTER TABLE [dbo].[ApiKeyScopes] ADD CONSTRAINT [FK_ApiKeyScopes_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[ApiKeys] ADD CONSTRAINT [FK_ApiKeys_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[TwoFactorAuth] ADD CONSTRAINT [FK_TwoFactorAuth_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[EmailVerificationTokens] ADD CONSTRAINT [FK_EmailVerificationTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationInvitations] ADD CONSTRAINT [FK_OrganizationInvitations_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[OrganizationInvitations] ADD CONSTRAINT [FK_OrganizationInvitations_Roles] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]);
ALTER TABLE [dbo].[OrganizationInvitations] ADD CONSTRAINT [FK_OrganizationInvitations_InvitedBy] FOREIGN KEY ([InvitedBy]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationInvitations] ADD CONSTRAINT [FK_OrganizationInvitations_AcceptedBy] FOREIGN KEY ([AcceptedByUserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD CONSTRAINT [FK_OrganizationUserPermissions_Organizations] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE CASCADE;
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD CONSTRAINT [FK_OrganizationUserPermissions_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD CONSTRAINT [FK_OrganizationUserPermissions_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD CONSTRAINT [FK_OrganizationUserPermissions_Permissions] FOREIGN KEY ([PermissionId]) REFERENCES [dbo].[Permissions] ([Id]);
ALTER TABLE [dbo].[OrganizationUserPermissions] ADD CONSTRAINT [FK_OrganizationUserPermissions_GrantedBy] FOREIGN KEY ([GrantedBy]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[PasswordResetTokens] ADD CONSTRAINT [FK_PasswordResetTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[Organizations] ADD CONSTRAINT [FK_Organizations_Users_Owner] FOREIGN KEY ([OwnerId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[UserExternalLogins] ADD CONSTRAINT [FK_UserExternalLogins_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]);
ALTER TABLE [dbo].[WebhookKeys] ADD CONSTRAINT [FK_WebhookKeys_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]);
GO

-- ============================================
-- CHECK CONSTRAINTS
-- ============================================

PRINT N'Creating Check Constraints...';
GO

ALTER TABLE [dbo].[OrganizationInvitations] ADD CONSTRAINT [CK_OrganizationInvitations_Status] CHECK ([Status] IN ('Pending', 'Accepted', 'Declined', 'Expired', 'Cancelled'));
ALTER TABLE [dbo].[Users] ADD CONSTRAINT [CK_Users_Status] CHECK ([Status] IN (1, 2, 3, 4));
GO

-- ============================================
-- STORED PROCEDURES
-- ============================================

PRINT N'Creating Stored Procedures...';
GO

CREATE PROCEDURE [dbo].[sp_RevokeAllUserTokens]
    @UserId UNIQUEIDENTIFIER,
    @RevokedBy UNIQUEIDENTIFIER = NULL,
    @ReasonRevoked NVARCHAR(200) = N'All tokens revoked'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RevokedCount INT;

    UPDATE [dbo].[RefreshTokens]
    SET [RevokedAt] = GETUTCDATE(),
        [RevokedBy] = @RevokedBy,
        [ReasonRevoked] = @ReasonRevoked
    WHERE [UserId] = @UserId
      AND [RevokedAt] IS NULL;

    SET @RevokedCount = @@ROWCOUNT;

    UPDATE [dbo].[UserSessions]
    SET [EndedAt] = GETUTCDATE(),
        [EndReason] = @ReasonRevoked
    WHERE [UserId] = @UserId
      AND [EndedAt] IS NULL;

    SELECT @RevokedCount AS [RevokedTokenCount];
END
GO

CREATE PROCEDURE [dbo].[sp_CheckAccountLockout]
    @UserId UNIQUEIDENTIFIER,
    @MaxFailedAttempts INT = 5,
    @LockoutDurationMinutes INT = 30
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @FailedLoginAttempts INT;
    DECLARE @LockoutEndUtc DATETIME2;
    DECLARE @Status TINYINT;
    DECLARE @IsLocked BIT = 0;
    DECLARE @ShouldLock BIT = 0;
    DECLARE @RemainingAttempts INT;
    DECLARE @LockoutRemainingMinutes INT = 0;

    SELECT
        @FailedLoginAttempts = [FailedLoginAttempts],
        @LockoutEndUtc = [LockoutEndUtc],
        @Status = [Status]
    FROM [dbo].[Users]
    WHERE [Id] = @UserId
      AND [IsDeleted] = 0;

    IF @Status = 3 OR (@LockoutEndUtc IS NOT NULL AND @LockoutEndUtc > GETUTCDATE())
    BEGIN
        SET @IsLocked = 1;
        IF @LockoutEndUtc IS NOT NULL
        BEGIN
            SET @LockoutRemainingMinutes = DATEDIFF(MINUTE, GETUTCDATE(), @LockoutEndUtc);
            IF @LockoutRemainingMinutes < 0
                SET @LockoutRemainingMinutes = 0;
        END
    END

    IF @LockoutEndUtc IS NOT NULL AND @LockoutEndUtc <= GETUTCDATE()
    BEGIN
        UPDATE [dbo].[Users]
        SET [LockoutEndUtc] = NULL,
            [FailedLoginAttempts] = 0,
            [Status] = CASE WHEN [Status] = 3 THEN 1 ELSE [Status] END,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;

        SET @IsLocked = 0;
        SET @FailedLoginAttempts = 0;
    END

    IF @FailedLoginAttempts >= @MaxFailedAttempts AND @IsLocked = 0
    BEGIN
        SET @ShouldLock = 1;
        SET @LockoutEndUtc = DATEADD(MINUTE, @LockoutDurationMinutes, GETUTCDATE());

        UPDATE [dbo].[Users]
        SET [LockoutEndUtc] = @LockoutEndUtc,
            [Status] = 3,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;

        SET @IsLocked = 1;
        SET @LockoutRemainingMinutes = @LockoutDurationMinutes;
    END

    SET @RemainingAttempts = @MaxFailedAttempts - @FailedLoginAttempts;
    IF @RemainingAttempts < 0
        SET @RemainingAttempts = 0;

    SELECT
        @IsLocked AS [IsLocked],
        @FailedLoginAttempts AS [FailedAttempts],
        @RemainingAttempts AS [RemainingAttempts],
        @LockoutEndUtc AS [LockoutEndUtc],
        @LockoutRemainingMinutes AS [LockoutRemainingMinutes],
        @ShouldLock AS [JustLocked];
END
GO

CREATE PROCEDURE [dbo].[sp_RecordLoginAttempt]
    @UserId UNIQUEIDENTIFIER = NULL,
    @Username NVARCHAR(255),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @IpAddress NVARCHAR(45),
    @UserAgent NVARCHAR(500) = NULL,
    @IsSuccessful BIT,
    @FailureReason NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[LoginAttempts]
    (
        [UserId],
        [Username],
        [ApplicationId],
        [IpAddress],
        [UserAgent],
        [AttemptedAt],
        [IsSuccessful],
        [FailureReason]
    )
    VALUES
    (
        @UserId,
        @Username,
        @ApplicationId,
        @IpAddress,
        @UserAgent,
        GETUTCDATE(),
        @IsSuccessful,
        @FailureReason
    );

    IF @IsSuccessful = 0 AND @UserId IS NOT NULL
    BEGIN
        UPDATE [dbo].[Users]
        SET [FailedLoginAttempts] = [FailedLoginAttempts] + 1,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;
    END

    IF @IsSuccessful = 1 AND @UserId IS NOT NULL
    BEGIN
        UPDATE [dbo].[Users]
        SET [FailedLoginAttempts] = 0,
            [LastLoginUtc] = GETUTCDATE(),
            [LastLoginIp] = @IpAddress,
            [LockoutEndUtc] = NULL,
            [ModifiedAt] = GETUTCDATE()
        WHERE [Id] = @UserId;
    END
END
GO

CREATE PROCEDURE [dbo].[sp_ValidateCredentials]
    @Email NVARCHAR(255),
    @IpAddress NVARCHAR(45) = NULL,
    @UserAgent NVARCHAR(500) = NULL,
    @ApplicationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NormalizedEmail NVARCHAR(255) = UPPER(TRIM(@Email));
    DECLARE @UserId UNIQUEIDENTIFIER;
    DECLARE @PasswordHash NVARCHAR(500);
    DECLARE @Status TINYINT;
    DECLARE @IsEmailConfirmed BIT;
    DECLARE @IsTwoFactorEnabled BIT;
    DECLARE @FailedLoginAttempts INT;
    DECLARE @LockoutEndUtc DATETIME2;

    SELECT
        @UserId = [Id],
        @PasswordHash = [PasswordHash],
        @Status = [Status],
        @IsEmailConfirmed = [IsEmailConfirmed],
        @IsTwoFactorEnabled = [IsTwoFactorEnabled],
        @FailedLoginAttempts = [FailedLoginAttempts],
        @LockoutEndUtc = [LockoutEndUtc]
    FROM [dbo].[Users]
    WHERE [NormalizedEmail] = @NormalizedEmail
      AND [IsDeleted] = 0;

    IF @UserId IS NOT NULL
    BEGIN
        SELECT
            @UserId AS [UserId],
            @PasswordHash AS [PasswordHash],
            @Status AS [Status],
            @IsEmailConfirmed AS [IsEmailConfirmed],
            @IsTwoFactorEnabled AS [IsTwoFactorEnabled],
            @FailedLoginAttempts AS [FailedLoginAttempts],
            @LockoutEndUtc AS [LockoutEndUtc],
            CASE
                WHEN @Status = 3 THEN 1
                WHEN @LockoutEndUtc IS NOT NULL AND @LockoutEndUtc > GETUTCDATE() THEN 1
                ELSE 0
            END AS [IsLocked],
            CASE
                WHEN @Status = 1 THEN 1
                ELSE 0
            END AS [IsActive];
    END
    ELSE
    BEGIN
        SELECT
            NULL AS [UserId],
            NULL AS [PasswordHash],
            NULL AS [Status],
            NULL AS [IsEmailConfirmed],
            NULL AS [IsTwoFactorEnabled],
            0 AS [FailedLoginAttempts],
            NULL AS [LockoutEndUtc],
            0 AS [IsLocked],
            0 AS [IsActive];
    END
END
GO

CREATE PROCEDURE [dbo].[sp_GetUserByEmail]
    @Email NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NormalizedEmail NVARCHAR(255) = UPPER(TRIM(@Email));

    SELECT
        [Id],
        [Username],
        [Email],
        [NormalizedEmail],
        [PasswordHash],
        [FirstName],
        [LastName],
        [FullName],
        [PhoneNumber],
        [ProfileImageUrl],
        [PreferredLanguage],
        [TimeZone],
        [IsEmailConfirmed] AS [EmailConfirmed],
        [IsPhoneConfirmed] AS [PhoneConfirmed],
        [IsTwoFactorEnabled] AS [TwoFactorEnabled],
        [Status],
        [FailedLoginAttempts],
        [LockoutEndUtc] AS [LockoutEnd],
        [LastLoginUtc] AS [LastLoginAt],
        [LastLoginIp],
        [LastPasswordChangeUtc] AS [PasswordChangedAt],
        [MustChangePassword],
        [PasswordExpiresUtc],
        [SecurityStamp],
        [ConcurrencyStamp],
        [CreatedAt],
        [CreatedBy],
        [ModifiedAt],
        [ModifiedBy],
        [IsDeleted],
        [DeletedAt],
        [DeletedBy]
    FROM [dbo].[Users]
    WHERE [NormalizedEmail] = @NormalizedEmail
      AND [IsDeleted] = 0;
END
GO

CREATE PROCEDURE [dbo].[sp_GetUserById]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [Username],
        [Email],
        [NormalizedEmail],
        [PasswordHash],
        [FirstName],
        [LastName],
        [FullName],
        [PhoneNumber],
        [ProfileImageUrl],
        [PreferredLanguage],
        [TimeZone],
        [IsEmailConfirmed] AS [EmailConfirmed],
        [IsPhoneConfirmed] AS [PhoneConfirmed],
        [IsTwoFactorEnabled] AS [TwoFactorEnabled],
        [Status],
        [FailedLoginAttempts],
        [LockoutEndUtc] AS [LockoutEnd],
        [LastLoginUtc] AS [LastLoginAt],
        [LastLoginIp],
        [LastPasswordChangeUtc] AS [PasswordChangedAt],
        [MustChangePassword],
        [PasswordExpiresUtc],
        [SecurityStamp],
        [ConcurrencyStamp],
        [CreatedAt],
        [CreatedBy],
        [ModifiedAt],
        [ModifiedBy],
        [IsDeleted],
        [DeletedAt],
        [DeletedBy]
    FROM [dbo].[Users]
    WHERE [Id] = @UserId
      AND [IsDeleted] = 0;
END
GO

CREATE PROCEDURE [dbo].[sp_CreateRefreshToken]
    @UserId UNIQUEIDENTIFIER,
    @TokenHash NVARCHAR(100),
    @JwtId NVARCHAR(100),
    @ApplicationId UNIQUEIDENTIFIER = NULL,
    @DeviceInfo NVARCHAR(500) = NULL,
    @IpAddress NVARCHAR(45) = NULL,
    @ExpiresAt DATETIME2
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TokenId UNIQUEIDENTIFIER = NEWID();

    INSERT INTO [dbo].[RefreshTokens]
    (
        [Id],
        [UserId],
        [TokenHash],
        [JwtId],
        [ApplicationId],
        [DeviceInfo],
        [IpAddress],
        [CreatedAt],
        [ExpiresAt]
    )
    VALUES
    (
        @TokenId,
        @UserId,
        @TokenHash,
        @JwtId,
        @ApplicationId,
        @DeviceInfo,
        @IpAddress,
        GETUTCDATE(),
        @ExpiresAt
    );

    SELECT @TokenId AS [TokenId];
END
GO

CREATE PROCEDURE [dbo].[sp_RevokeRefreshToken]
    @TokenHash NVARCHAR(100),
    @RevokedBy UNIQUEIDENTIFIER = NULL,
    @ReasonRevoked NVARCHAR(200) = NULL,
    @ReplacedByTokenHash NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TokenId UNIQUEIDENTIFIER;
    DECLARE @AlreadyRevoked BIT = 0;

    SELECT
        @TokenId = [Id],
        @AlreadyRevoked = CASE WHEN [RevokedAt] IS NOT NULL THEN 1 ELSE 0 END
    FROM [dbo].[RefreshTokens]
    WHERE [TokenHash] = @TokenHash;

    IF @TokenId IS NULL
    BEGIN
        SELECT 0 AS [Success], N'Token not found' AS [Message];
        RETURN;
    END

    IF @AlreadyRevoked = 1
    BEGIN
        SELECT 0 AS [Success], N'Token already revoked' AS [Message];
        RETURN;
    END

    UPDATE [dbo].[RefreshTokens]
    SET [RevokedAt] = GETUTCDATE(),
        [RevokedBy] = @RevokedBy,
        [ReasonRevoked] = @ReasonRevoked,
        [ReplacedByTokenHash] = @ReplacedByTokenHash
    WHERE [Id] = @TokenId;

    SELECT 1 AS [Success], N'Token revoked successfully' AS [Message];
END
GO

CREATE PROCEDURE [dbo].[sp_ValidateRefreshToken]
    @TokenHash NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        rt.[Id],
        rt.[UserId],
        rt.[TokenHash],
        rt.[JwtId],
        rt.[ApplicationId],
        rt.[DeviceInfo],
        rt.[IpAddress],
        rt.[CreatedAt],
        rt.[ExpiresAt],
        rt.[RevokedAt],
        rt.[RevokedBy],
        rt.[ReplacedByTokenHash],
        rt.[ReasonRevoked],
        CASE
            WHEN rt.[RevokedAt] IS NOT NULL THEN 0
            WHEN rt.[ExpiresAt] < GETUTCDATE() THEN 0
            ELSE 1
        END AS [IsValid],
        CASE
            WHEN rt.[ExpiresAt] < GETUTCDATE() THEN 1
            ELSE 0
        END AS [IsExpired],
        CASE
            WHEN rt.[RevokedAt] IS NOT NULL THEN 1
            ELSE 0
        END AS [IsRevoked],
        u.[Status] AS [UserStatus],
        u.[IsDeleted] AS [UserIsDeleted]
    FROM [dbo].[RefreshTokens] rt
    INNER JOIN [dbo].[Users] u ON rt.[UserId] = u.[Id]
    WHERE rt.[TokenHash] = @TokenHash;
END
GO

-- ============================================
-- SEED DATA
-- ============================================

PRINT 'Starting post-deployment seed data...';
PRINT '======================================';

-- STEP 1: DEFAULT APPLICATIONS
PRINT '';
PRINT 'Step 1: Creating default applications...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Applications] WHERE [Id] = @AuthAppId)
BEGIN
    INSERT INTO [dbo].[Applications]
    ([Id], [Code], [Name], [Description], [BaseUrl], [IsActive], [AllowSelfRegistration], [RequireTwoFactor], [SessionTimeoutMinutes], [MaxConcurrentSessions], [CreatedAt], [CreatedBy])
    VALUES
    (@AuthAppId, N'auth', N'Auth System', N'Central Authentication and Authorization System', N'https://auth.company.com', 1, 0, 0, 60, 10, GETUTCDATE(), @SystemUserId);
    PRINT 'Created Auth System application';
END
ELSE
    PRINT 'Auth System application already exists';
GO

-- STEP 2: DEFAULT ROLES
PRINT '';
PRINT 'Step 2: Creating default roles...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'super-admin' AND [ApplicationId] IS NULL)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000001', N'super-admin', N'Super Administrator', N'Has all permissions across all applications', NULL, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'admin' AND [ApplicationId] = @AuthAppId)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000002', N'admin', N'Administrator', N'Can manage users, roles, and permissions in Auth System', @AuthAppId, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'user-manager' AND [ApplicationId] = @AuthAppId)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000003', N'user-manager', N'User Manager', N'Can manage users but not roles or permissions', @AuthAppId, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'auditor' AND [ApplicationId] = @AuthAppId)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000004', N'auditor', N'Auditor', N'Read-only access to audit logs and reports', @AuthAppId, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'user' AND [ApplicationId] IS NULL)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0000-000000000005', N'user', N'User', N'Basic authenticated user with profile access', NULL, 1, 1, GETUTCDATE(), @SystemUserId);

PRINT 'Created default roles';
GO

-- STEP 3: DEFAULT PERMISSIONS
PRINT '';
PRINT 'Step 3: Creating default permissions...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AuthAppId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000001', N'*', N'All Permissions', N'Super admin - grants all permissions', NULL, NULL, 0, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000002', N'auth:*', N'All Auth Permissions', N'Full access to Auth System', @AuthAppId, N'20000000-0000-0000-0000-000000000001', 1, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000010', N'auth:users:*', N'All User Permissions', N'Full access to user management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000020', N'auth:roles:*', N'All Role Permissions', N'Full access to role management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:permissions:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000030', N'auth:permissions:*', N'All Permission Permissions', N'Full access to permission management', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:audit:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000040', N'auth:audit:*', N'All Audit Permissions', N'Full access to audit logs', @AuthAppId, N'20000000-0000-0000-0000-000000000002', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000011', N'auth:users:read', N'Read Users', N'View user information', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:create')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000012', N'auth:users:create', N'Create Users', N'Create new users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:update')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000013', N'auth:users:update', N'Update Users', N'Modify user information', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:delete')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000014', N'auth:users:delete', N'Delete Users', N'Delete users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:users:manage-roles')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000015', N'auth:users:manage-roles', N'Manage User Roles', N'Assign and remove roles from users', @AuthAppId, N'20000000-0000-0000-0000-000000000010', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000021', N'auth:roles:read', N'Read Roles', N'View role information', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:create')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000022', N'auth:roles:create', N'Create Roles', N'Create new roles', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:update')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000023', N'auth:roles:update', N'Update Roles', N'Modify role information', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:roles:delete')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000024', N'auth:roles:delete', N'Delete Roles', N'Delete roles', @AuthAppId, N'20000000-0000-0000-0000-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'auth:audit:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000041', N'auth:audit:read', N'Read Audit Logs', N'View audit logs', @AuthAppId, N'20000000-0000-0000-0000-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'profile:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000100', N'profile:read', N'Read Own Profile', N'View own profile information', NULL, NULL, 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'profile:update')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0000-000000000101', N'profile:update', N'Update Own Profile', N'Modify own profile information', NULL, NULL, 3, 0, 1, GETUTCDATE(), @SystemUserId);

PRINT 'Created default permissions';
GO

-- STEP 4: PERMISSION IMPLICATIONS
PRINT '';
PRINT 'Step 4: Creating permission implications...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000013' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000013', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000014' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000014', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000012' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000012', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000015' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000011')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000015', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000015' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000015', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000022' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000022', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000023' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000023', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000024' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000021')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000024', N'20000000-0000-0000-0000-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0000-000000000101' AND [ImpliedPermissionId] = N'20000000-0000-0000-0000-000000000100')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0000-000000000101', N'20000000-0000-0000-0000-000000000100', GETUTCDATE(), @SystemUserId);

PRINT 'Created permission implications';
GO

-- STEP 5: ROLE PERMISSIONS
PRINT '';
PRINT 'Step 5: Assigning permissions to roles...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000001' AND [PermissionId] = N'20000000-0000-0000-0000-000000000001')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000001', N'20000000-0000-0000-0000-000000000001', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000002' AND [PermissionId] = N'20000000-0000-0000-0000-000000000002')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000002', N'20000000-0000-0000-0000-000000000002', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000003' AND [PermissionId] = N'20000000-0000-0000-0000-000000000010')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000003', N'20000000-0000-0000-0000-000000000010', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000004' AND [PermissionId] = N'20000000-0000-0000-0000-000000000041')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000004', N'20000000-0000-0000-0000-000000000041', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000004' AND [PermissionId] = N'20000000-0000-0000-0000-000000000011')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000004', N'20000000-0000-0000-0000-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000005' AND [PermissionId] = N'20000000-0000-0000-0000-000000000100')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000005', N'20000000-0000-0000-0000-000000000100', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0000-000000000005' AND [PermissionId] = N'20000000-0000-0000-0000-000000000101')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0000-000000000005', N'20000000-0000-0000-0000-000000000101', GETUTCDATE(), @SystemUserId);

PRINT 'Created role permissions';
GO

-- STEP 5.5: ORGANIZATION ROLES AND PERMISSIONS
PRINT '';
PRINT 'Step 5.5: Creating organization roles and permissions...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'org-owner' AND [ApplicationId] IS NULL)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0001-000000000001', N'org-owner', N'Organization Owner', N'Full control over organization - cannot be removed', NULL, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'org-admin' AND [ApplicationId] IS NULL)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0001-000000000002', N'org-admin', N'Organization Admin', N'Can manage members and app subscriptions', NULL, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Roles] WHERE [Code] = N'org-member' AND [ApplicationId] IS NULL)
    INSERT INTO [dbo].[Roles] ([Id], [Code], [Name], [Description], [ApplicationId], [IsSystem], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'10000000-0000-0000-0001-000000000003', N'org-member', N'Organization Member', N'Basic organization membership - access apps based on granted permissions', NULL, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000001', N'org:*', N'All Organization Permissions', N'Full organization management access', NULL, N'20000000-0000-0000-0000-000000000001', 1, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000011', N'org:read', N'View Organization', N'View organization details and settings', NULL, N'20000000-0000-0000-0001-000000000001', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:update')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000012', N'org:update', N'Update Organization', N'Modify organization settings', NULL, N'20000000-0000-0000-0001-000000000001', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:delete')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000013', N'org:delete', N'Delete Organization', N'Delete the organization', NULL, N'20000000-0000-0000-0001-000000000001', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000020', N'org:members:*', N'All Member Permissions', N'Full member management access', NULL, N'20000000-0000-0000-0001-000000000001', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000021', N'org:members:read', N'View Members', N'View organization members', NULL, N'20000000-0000-0000-0001-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:invite')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000022', N'org:members:invite', N'Invite Members', N'Invite new members to organization', NULL, N'20000000-0000-0000-0001-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:members:manage')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000023', N'org:members:manage', N'Manage Members', N'Update roles and remove members', NULL, N'20000000-0000-0000-0001-000000000020', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000030', N'org:apps:*', N'All App Subscription Permissions', N'Full app subscription management', NULL, N'20000000-0000-0000-0001-000000000001', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000031', N'org:apps:read', N'View Enabled Apps', N'View which apps are enabled for organization', NULL, N'20000000-0000-0000-0001-000000000030', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:apps:manage')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000032', N'org:apps:manage', N'Manage App Subscriptions', N'Enable or disable apps for organization', NULL, N'20000000-0000-0000-0001-000000000030', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:*')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000040', N'org:permissions:*', N'All User Permission Management', N'Full user permission management within organization', NULL, N'20000000-0000-0000-0001-000000000001', 2, 1, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:read')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000041', N'org:permissions:read', N'View User Permissions', N'View member permissions within organization', NULL, N'20000000-0000-0000-0001-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:grant')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000042', N'org:permissions:grant', N'Grant User Permissions', N'Grant permissions to members within organization', NULL, N'20000000-0000-0000-0001-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Permissions] WHERE [Code] = N'org:permissions:revoke')
    INSERT INTO [dbo].[Permissions] ([Id], [Code], [Name], [Description], [ApplicationId], [ParentId], [Level], [IsWildcard], [IsActive], [CreatedAt], [CreatedBy])
    VALUES (N'20000000-0000-0000-0001-000000000043', N'org:permissions:revoke', N'Revoke User Permissions', N'Revoke permissions from members within organization', NULL, N'20000000-0000-0000-0001-000000000040', 3, 0, 1, GETUTCDATE(), @SystemUserId);

-- Organization permission implications
IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000012' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000011')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000012', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000013' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000011')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000013', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000022' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000021')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000022', N'20000000-0000-0000-0001-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000023' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000021')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000023', N'20000000-0000-0000-0001-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000032' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000031')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000032', N'20000000-0000-0000-0001-000000000031', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000042' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000041')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000042', N'20000000-0000-0000-0001-000000000041', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[PermissionImplications] WHERE [PermissionId] = N'20000000-0000-0000-0001-000000000043' AND [ImpliedPermissionId] = N'20000000-0000-0000-0001-000000000041')
    INSERT INTO [dbo].[PermissionImplications] ([PermissionId], [ImpliedPermissionId], [CreatedAt], [CreatedBy]) VALUES (N'20000000-0000-0000-0001-000000000043', N'20000000-0000-0000-0001-000000000041', GETUTCDATE(), @SystemUserId);

-- Organization role permissions
IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000001' AND [PermissionId] = N'20000000-0000-0000-0001-000000000001')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000001', N'20000000-0000-0000-0001-000000000001', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000011')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000020')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000020', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000030')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000030', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000002' AND [PermissionId] = N'20000000-0000-0000-0001-000000000040')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000002', N'20000000-0000-0000-0001-000000000040', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000003' AND [PermissionId] = N'20000000-0000-0000-0001-000000000011')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000003', N'20000000-0000-0000-0001-000000000011', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000003' AND [PermissionId] = N'20000000-0000-0000-0001-000000000021')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000003', N'20000000-0000-0000-0001-000000000021', GETUTCDATE(), @SystemUserId);

IF NOT EXISTS (SELECT 1 FROM [dbo].[RolePermissions] WHERE [RoleId] = N'10000000-0000-0000-0001-000000000003' AND [PermissionId] = N'20000000-0000-0000-0001-000000000031')
    INSERT INTO [dbo].[RolePermissions] ([RoleId], [PermissionId], [GrantedAt], [GrantedBy]) VALUES (N'10000000-0000-0000-0001-000000000003', N'20000000-0000-0000-0001-000000000031', GETUTCDATE(), @SystemUserId);

PRINT 'Created organization roles and permissions';
GO

-- STEP 6: ADMIN USER
PRINT '';
PRINT 'Step 6: Creating admin user...';

DECLARE @SystemUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @AdminUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000002';
DECLARE @SuperAdminRoleId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000001';
DECLARE @UserRoleId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000005';

IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = @SystemUserId)
BEGIN
    INSERT INTO [dbo].[Users]
    ([Id], [Username], [Email], [NormalizedEmail], [PasswordHash], [FirstName], [LastName], [PreferredLanguage], [TimeZone], [IsEmailConfirmed], [Status], [CreatedAt], [CreatedBy])
    VALUES
    (@SystemUserId, N'system', N'system@localhost', N'SYSTEM@LOCALHOST', N'$argon2id$v=19$m=65536,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', N'System', N'Account', N'en', N'UTC', 1, 2, GETUTCDATE(), @SystemUserId);
    PRINT 'Created system user';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Users] WHERE [Id] = @AdminUserId)
BEGIN
    INSERT INTO [dbo].[Users]
    ([Id], [Username], [Email], [NormalizedEmail], [PasswordHash], [FirstName], [LastName], [PreferredLanguage], [TimeZone], [IsEmailConfirmed], [Status], [MustChangePassword], [CreatedAt], [CreatedBy])
    VALUES
    -- Working Argon2id hash for the default password 'Admin@123!' (current OWASP params m=19456,t=2,p=1).
    -- MustChangePassword = 1 forces a change on first login. Unpeppered: upgraded automatically if peppering is enabled.
    (@AdminUserId, N'admin', N'admin@company.com', N'ADMIN@COMPANY.COM', N'$argon2id$v=19$m=19456,t=2,p=1$NoKP1nsfZyPf3Hp_V4IHww$_zyvdZiGmyfs87h7_q2f3A.VzxgOfnKVmL5doZ3Kz5Y', N'System', N'Administrator', N'en', N'UTC', 1, 1, 1, GETUTCDATE(), @SystemUserId);
    PRINT 'Created admin user (default password Admin@123! - must be changed on first login)';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles] WHERE [UserId] = @AdminUserId AND [RoleId] = @SuperAdminRoleId)
    INSERT INTO [dbo].[UserRoles] ([UserId], [RoleId], [ApplicationId], [AssignedAt], [AssignedBy], [IsActive])
    VALUES (@AdminUserId, @SuperAdminRoleId, NULL, GETUTCDATE(), @SystemUserId, 1);

IF NOT EXISTS (SELECT 1 FROM [dbo].[UserRoles] WHERE [UserId] = @AdminUserId AND [RoleId] = @UserRoleId)
    INSERT INTO [dbo].[UserRoles] ([UserId], [RoleId], [ApplicationId], [AssignedAt], [AssignedBy], [IsActive])
    VALUES (@AdminUserId, @UserRoleId, NULL, GETUTCDATE(), @SystemUserId, 1);

PRINT 'Admin user setup complete';
GO

-- STEP 7: EXTERNAL AUTH PROVIDERS
PRINT '';
PRINT 'Step 7: Creating external auth providers...';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ExternalAuthProviders] WHERE [Code] = N'google')
    INSERT INTO [dbo].[ExternalAuthProviders] ([Code], [Name], [IconUrl], [IsEnabled], [DisplayOrder])
    VALUES (N'google', N'Google', N'https://www.gstatic.com/firebasejs/ui/2.0.0/images/auth/google.svg', 1, 1);
GO

-- ============================================
-- COMPLETION
-- ============================================
PRINT '';
PRINT '======================================';
PRINT 'Deployment complete!';
PRINT '';
PRINT 'IMPORTANT NOTES:';
PRINT '1. Update the admin user password hash before production deployment';
PRINT '2. Review all seed data for your environment';
PRINT '3. Consider adding additional application-specific roles and permissions';
GO
