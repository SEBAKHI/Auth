CREATE TABLE [dbo].[UserKnownDevices]
(
    [Id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_UserKnownDevices_Id] DEFAULT NEWID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [DeviceHash] CHAR(64) NOT NULL,
    [DeviceName] NVARCHAR(100) NULL,
    [FirstSeenAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserKnownDevices_FirstSeenAt] DEFAULT GETUTCDATE(),
    [LastSeenAt] DATETIME2 NOT NULL CONSTRAINT [DF_UserKnownDevices_LastSeenAt] DEFAULT GETUTCDATE(),
    [LastAlertSentAt] DATETIME2 NULL,

    CONSTRAINT [PK_UserKnownDevices] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_UserKnownDevices_Users] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [UQ_UserKnownDevices_UserDevice] UNIQUE ([UserId], [DeviceHash])
);
GO

-- Devices a user has signed in from before, so a sign-in from a new one can be
-- reported to them. Recognition only — never an authorization input.
--
-- DeviceHash: SHA-256 over the client device id, browser family and OS family.
-- The hash rather than the parts, so this table is not a user-agent log, and
-- the *families* rather than versions: a browser that auto-updates weekly
-- would otherwise raise an alert on every update, which is the fastest way to
-- train someone to ignore these emails. IP is excluded for the same reason —
-- carrier NAT, VPNs and roaming change it constantly.
--
-- DeviceName: the human label for the email body ("Chrome on Windows"), kept
-- alongside the hash because the hash cannot be reversed to produce it.
--
-- LastAlertSentAt: anti-spam floor. A genuinely new device is new exactly once,
-- so this only guards against a burst of concurrent first sign-ins.
--
-- The FK does not cascade: the hard-delete purge in UserRepository removes
-- these rows explicitly, and UserHardDeleteSqlTests fails the build if a new
-- Users-referencing table is added without extending it.

-- Indexes
-- The unique constraint above already indexes (UserId, DeviceHash), which
-- serves both reads: the per-device lookup and the "has any device" probe.
