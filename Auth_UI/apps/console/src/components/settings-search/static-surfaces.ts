import { PERMISSIONS } from "@/lib/constants"

/**
 * Settings surfaces that are not driven by the backend registry, so they can
 * only be listed by hand.
 *
 * `titleKey`/`descriptionKey` are ordinary i18n keys, which is what keeps this
 * list searchable in whatever language the console is running in.
 */
export interface StaticSurface {
  id: string
  route: string
  titleKey: string
  descriptionKey?: string
  /**
   * i18n keys naming where the result lives, shown beside its title.
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
  {
    id: "platform-settings",
    route: "/admin/platform-settings",
    titleKey: "platformSettings.title",
    descriptionKey: "platformSettings.subtitle",
    pathKeys: ["nav.platform"],
    permission: PERMISSIONS.platformSettings.manage,
  },
  {
    id: "secrets",
    route: "/admin/secrets",
    titleKey: "secrets.title",
    descriptionKey: "secrets.subtitle",
    pathKeys: ["nav.platform"],
    permission: PERMISSIONS.secrets.manage,
  },
  {
    id: "profile-account",
    route: "/profile",
    titleKey: "profile.accountDetails",
    descriptionKey: "profile.accountDetailsSubtitle",
    pathKeys: ["nav.profile"],
  },
  {
    id: "profile-sessions",
    route: "/profile",
    titleKey: "profile.sessions",
    pathKeys: ["nav.profile"],
  },
  {
    id: "profile-security",
    route: "/profile",
    titleKey: "profile.security",
    pathKeys: ["nav.profile"],
  },
  {
    id: "notification-templates",
    route: "/notifications/templates",
    titleKey: "notifications.title",
    descriptionKey: "notifications.subtitle",
    pathKeys: ["nav.notifications"],
    permission: PERMISSIONS.notificationTemplates.read,
  },
  {
    id: "notification-layouts",
    route: "/notifications/layouts",
    titleKey: "notifications.layoutsTitle",
    descriptionKey: "notifications.layoutsSubtitle",
    pathKeys: ["nav.notifications"],
    permission: PERMISSIONS.notificationTemplates.read,
  },
  {
    id: "notification-outbox",
    route: "/notifications/outbox",
    titleKey: "notifications.outboxTitle",
    descriptionKey: "notifications.outboxSubtitle",
    pathKeys: ["nav.notifications"],
    permission: PERMISSIONS.notificationTemplates.read,
  },
]
