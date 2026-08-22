/** Permission codes mirroring the backend RequirePermission attributes. */
export const PERMISSIONS = {
  users: {
    read: "users:read",
    create: "users:create",
    update: "users:update",
    delete: "users:delete",
    manageRoles: "users:manage-roles",
    managePermissions: "users:manage-permissions",
    manage: "users:manage",
  },
  roles: {
    read: "roles:read",
    create: "roles:create",
    update: "roles:update",
    delete: "roles:delete",
  },
  permissions: {
    read: "permissions:read",
    create: "permissions:create",
    update: "permissions:update",
    delete: "permissions:delete",
    manage: "permissions:manage",
  },
  applications: {
    read: "applications:read",
    create: "applications:create",
    update: "applications:update",
    delete: "applications:delete",
  },
  apiKeys: {
    read: "apikeys:read",
    create: "apikeys:create",
    revoke: "apikeys:revoke",
    validate: "apikeys:validate",
    rotate: "apikeys:rotate",
  },
  webhookKeys: {
    read: "webhookkeys:read",
    create: "webhookkeys:create",
    validate: "webhookkeys:validate",
    revoke: "webhookkeys:revoke",
    rotate: "webhookkeys:rotate",
  },
  auditLogs: {
    read: "auditlogs:read",
    export: "auditlogs:export",
  },
  secrets: { manage: "secrets.manage" },
  platformSettings: { manage: "platform-settings:manage" },
  systemSettings: { manage: "system-settings:manage" },
  notificationTemplates: {
    read: "notification-templates:read",
    manage: "notification-templates:manage",
    publish: "notification-templates:publish",
  },
  notificationLayouts: { manage: "notification-layouts:manage" },
  privacyPolicy: {
    read: "privacy-policy:read",
    manage: "privacy-policy:manage",
  },
  organizations: {
    read: "organizations:read",
    manage: "organizations:manage",
  },
} as const
