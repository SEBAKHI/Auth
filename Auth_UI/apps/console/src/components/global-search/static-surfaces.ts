import { TAB_QUERY_PARAM } from "@authsystem/ui/hooks/use-tab-param"

import { PERMISSIONS } from "@/lib/constants"
import { NOTIFICATION_SEARCH_SURFACES } from "@/lib/notification-destinations"

/**
 * Every place in the console that has its own address.
 *
 * Listed by hand because there is nothing to derive it from: the router knows
 * paths but not titles, and a page's title lives in its own JSX. `titleKey` and
 * `descriptionKey` are ordinary i18n keys — the same ones the page renders — so
 * a surface is searchable in whatever language the console is running in, and a
 * retitled page cannot drift from its search result.
 */
export interface StaticSurface {
  id: string
  route: string
  titleKey: string
  descriptionKey?: string
  /**
   * The other names this destination goes by, matched but not displayed.
   *
   * A page and the tab that opens it rarely share a label: the tab strip says
   * "Layouts" where the page header says "Notification Layouts", and the
   * sidebar may say a third thing. Someone searching types what they saw, and
   * what they saw was the tab.
   *
   * Matched rather than rendered, so one destination stays one row. Listing the
   * tab as an entry of its own would put two rows on screen for the same
   * address, differing only in wording.
   */
  altTitleKeys?: string[]
  /**
   * i18n keys naming where the result lives, shown beside its title. Empty for
   * a top-level destination, which is its own location.
   *
   * Without this two results read identically: "Sessions" is both a tab on the
   * profile and a section of system settings, and the title alone gives no way
   * to tell which one you are about to open.
   */
  pathKeys: string[]
  /** Permission required to see it; omitted means everyone signed in. */
  permission?: string
}

export const STATIC_SURFACES: readonly StaticSurface[] = [
  // ── Overview ──────────────────────────────────────────────────────────────
  {
    id: "dashboard",
    route: "/",
    titleKey: "dashboard.title",
    pathKeys: [],
  },
  // The dashboard's deep-dives are tabs, but the tab lives in the URL, so each
  // one is a real destination and is indexed as such. Every other tabbed page
  // in the console holds its tab in component state and cannot be linked into.
  {
    id: "dashboard-security",
    route: "/?tab=security",
    titleKey: "dashboard.tabSecurity",
    pathKeys: ["nav.dashboard"],
    permission: PERMISSIONS.auditLogs.read,
  },
  {
    id: "dashboard-people",
    route: "/?tab=people",
    titleKey: "dashboard.tabPeople",
    pathKeys: ["nav.dashboard"],
    permission: PERMISSIONS.users.read,
  },
  {
    id: "dashboard-apps",
    route: "/?tab=apps",
    titleKey: "dashboard.tabApplications",
    pathKeys: ["nav.dashboard"],
    permission: PERMISSIONS.applications.read,
  },
  {
    id: "dashboard-audit",
    route: "/?tab=audit",
    titleKey: "dashboard.tabAudit",
    pathKeys: ["nav.dashboard"],
    permission: PERMISSIONS.auditLogs.read,
  },

  // ── Directory ─────────────────────────────────────────────────────────────
  {
    id: "users",
    route: "/users",
    titleKey: "users.title",
    descriptionKey: "users.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.users.read,
  },
  {
    id: "roles",
    route: "/roles",
    titleKey: "roles.title",
    descriptionKey: "roles.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.roles.read,
  },
  {
    id: "permissions",
    route: "/permissions",
    titleKey: "permissions.title",
    descriptionKey: "permissions.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.permissions.read,
  },
  {
    id: "applications",
    route: "/applications",
    titleKey: "applications.title",
    descriptionKey: "applications.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.applications.read,
  },
  // No permission: the page falls back to the membership-scoped list, which
  // every signed-in user has.
  {
    id: "organizations",
    route: "/organizations",
    titleKey: "organizations.title",
    descriptionKey: "organizations.subtitle",
    pathKeys: [],
  },

  // ── Integration ───────────────────────────────────────────────────────────
  {
    id: "api-keys",
    route: "/api-keys",
    titleKey: "apiKeys.title",
    descriptionKey: "apiKeys.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.apiKeys.read,
  },
  {
    id: "webhook-keys",
    route: "/webhook-keys",
    titleKey: "webhookKeys.title",
    descriptionKey: "webhookKeys.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.webhookKeys.read,
  },
  {
    id: "audit-logs",
    route: "/audit-logs",
    titleKey: "auditLogs.title",
    descriptionKey: "auditLogs.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.auditLogs.read,
  },

  // ── Notifications ─────────────────────────────────────────────────────────
  ...NOTIFICATION_SEARCH_SURFACES,

  // ── Platform administration ───────────────────────────────────────────────
  {
    // The section this page belongs to contributes no row of its own — see
    // SECTION_COMPANION_PAGES — so this one carries both names.
    id: "secrets",
    route: "/admin/system-settings/SecretManagement/keys",
    titleKey: "secrets.title",
    descriptionKey: "secrets.subtitle",
    altTitleKeys: [
      "nav.secretManagement",
      "systemSettings.secretManagement.title",
    ],
    pathKeys: ["nav.systemSettings", "nav.secretManagement"],
    permission: PERMISSIONS.secrets.manage,
  },
  {
    id: "platform-settings",
    route: "/admin/platform-settings",
    titleKey: "platformSettings.title",
    descriptionKey: "platformSettings.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.platformSettings.manage,
  },
  {
    id: "system-settings",
    route: "/admin/system-settings",
    titleKey: "systemSettings.title",
    descriptionKey: "systemSettings.subtitle",
    pathKeys: [],
    permission: PERMISSIONS.systemSettings.manage,
  },

  // ── Your own account ──────────────────────────────────────────────────────
  // The account tab is the page's default, so it writes no parameter and the
  // page itself is the destination — one row, under every name it answers to,
  // rather than two rows one click apart.
  {
    id: "profile",
    route: "/profile",
    titleKey: "profile.title",
    descriptionKey: "profile.subtitle",
    altTitleKeys: ["profile.account", "profile.accountDetails", "nav.profile"],
    pathKeys: [],
  },
  {
    id: "profile-sessions",
    route: `/profile?${TAB_QUERY_PARAM}=sessions`,
    titleKey: "profile.sessions",
    pathKeys: ["nav.profile"],
  },
  {
    id: "profile-security",
    route: `/profile?${TAB_QUERY_PARAM}=security`,
    titleKey: "profile.security",
    pathKeys: ["nav.profile"],
  },
]
