import {
  AppWindow,
  Building2,
  KeyRound,
  LayoutDashboard,
  KeySquare,
  Lock,
  ScrollText,
  Settings2,
  ShieldCheck,
  Users,
  Webhook,
} from "lucide-react"
import type { LucideIcon } from "lucide-react"

/**
 * Permission codes, mirroring the backend `[RequirePermission]` attributes.
 * Used to gate navigation, routes, and actions in the UI. The API remains the
 * authoritative enforcement point.
 */
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
  secrets: {
    manage: "secrets.manage",
  },
  platformSettings: {
    manage: "platform-settings:manage",
  },
  // Platform-wide administration over ALL organizations — distinct from the
  // membership-scoped org:* permissions used by self-service.
  organizations: {
    read: "organizations:read",
    manage: "organizations:manage",
  },
} as const

export interface NavItem {
  /** i18n key under `nav.*`. */
  titleKey: string
  /** Absolute route path. */
  url: string
  icon: LucideIcon
  /** Permission required to see this item; omit for any authenticated user. */
  permission?: string
}

/** Primary sidebar navigation. */
export const NAV_ITEMS: NavItem[] = [
  { titleKey: "dashboard", url: "/", icon: LayoutDashboard },
  {
    titleKey: "users",
    url: "/users",
    icon: Users,
    permission: PERMISSIONS.users.read,
  },
  {
    titleKey: "roles",
    url: "/roles",
    icon: ShieldCheck,
    permission: PERMISSIONS.roles.read,
  },
  {
    titleKey: "permissions",
    url: "/permissions",
    icon: KeyRound,
    permission: PERMISSIONS.permissions.read,
  },
  {
    titleKey: "applications",
    url: "/applications",
    icon: AppWindow,
    permission: PERMISSIONS.applications.read,
  },
  // Self-service (membership-scoped) — visible to any authenticated user.
  { titleKey: "organizations", url: "/organizations", icon: Building2 },
  {
    titleKey: "apiKeys",
    url: "/api-keys",
    icon: KeySquare,
    permission: PERMISSIONS.apiKeys.read,
  },
  {
    titleKey: "webhookKeys",
    url: "/webhook-keys",
    icon: Webhook,
    permission: PERMISSIONS.webhookKeys.read,
  },
  {
    titleKey: "auditLogs",
    url: "/audit-logs",
    icon: ScrollText,
    permission: PERMISSIONS.auditLogs.read,
  },
  {
    titleKey: "secrets",
    url: "/admin/secrets",
    icon: Lock,
    permission: PERMISSIONS.secrets.manage,
  },
  {
    titleKey: "platformSettings",
    url: "/admin/platform-settings",
    icon: Settings2,
    permission: PERMISSIONS.platformSettings.manage,
  },
]

export { DEFAULT_PAGE_SIZE } from "@astoom/api/constants"
