CREATE TABLE [dbo].[UserRoles]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_UserRoles_Id DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] UNIQUEIDENTIFIER NOT NULL,
    [AssignedAt] DATETIME2(7) NOT NULL CONSTRAINT DF_UserRoles_AssignedAt DEFAULT GETUTCDATE(),
    [AssignedBy] UNIQUEIDENTIFIER NOT NULL,
    [RevokedAt] DATETIME2(7) NULL,
    [RevokedBy] UNIQUEIDENTIFIER NULL,
    [IsActive] BIT NOT NULL CONSTRAINT DF_UserRoles_IsActive DEFAULT 1,

    CONSTRAINT PK_UserRoles PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles]([Id]) ON DELETE CASCADE,
    CONSTRAINT UQ_UserRoles_UserRole UNIQUE NONCLUSTERED ([UserId], [RoleId])
);

GO

-- =============================================
-- Indexes for UserRoles Table
-- =============================================
CREATE NONCLUSTERED INDEX IX_UserRoles_UserId 
    ON [dbo].[UserRoles]([UserId] ASC) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX IX_UserRoles_RoleId 
    ON [dbo].[UserRoles]([RoleId] ASC) WHERE [IsActive] = 1;
GO

CREATE NONCLUSTERED INDEX IX_UserRoles_IsActive 
    ON [dbo].[UserRoles]([IsActive] ASC);
GO
