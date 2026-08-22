import {
  AppWindow,
  Building2,
  KeyRound,
  LayoutDashboard,
  KeySquare,
  Mail,
  ScrollText,
  Settings2,
  ShieldCheck,
  SlidersHorizontal,
  Users,
  Webhook,
} from "lucide-react"
import type { LucideIcon } from "lucide-react"
import {
  notificationLandingPath,
  type PermissionCheck,
} from "./notification-destinations"
import { PERMISSIONS } from "./permissions"

/**
 * Permission codes, mirroring the backend `[RequirePermission]` attributes.
 * Used to gate navigation, routes, and actions in the UI. The API remains the
 * authoritative enforcement point.
 */
export interface NavItem {
  /** i18n key under `nav.*`. */
  titleKey: string
  /** Absolute route path. */
  url: string
  icon: LucideIcon
  /** Permission required to see this item; omit for any authenticated user. */
  permission?: string
  /** Role-aware destination; null hides the item. */
  resolveUrl?: (hasPermission: PermissionCheck) => string | null
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
    titleKey: "notifications",
    url: "/notifications",
    icon: Mail,
    resolveUrl: notificationLandingPath,
  },
  // Secret keys are not a sidebar destination of their own: they live under
  // System settings › Secret management, reached from that section's card and
  // from the "manage secrets" button on every secret-owned setting.
  {
    titleKey: "platformSettings",
    url: "/admin/platform-settings",
    icon: Settings2,
    permission: PERMISSIONS.platformSettings.manage,
  },
  {
    titleKey: "systemSettings",
    url: "/admin/system-settings",
    icon: SlidersHorizontal,
    permission: PERMISSIONS.systemSettings.manage,
  },
]

export { PERMISSIONS }
export { DEFAULT_PAGE_SIZE } from "@authsystem/api/constants"
