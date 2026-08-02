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
  /** Permission required to see it; omitted means everyone signed in. */
  permission?: string
}

export const STATIC_SURFACES: readonly StaticSurface[] = [
  {
    id: "platform-settings",
    route: "/admin/platform-settings",
    titleKey: "platformSettings.title",
    descriptionKey: "platformSettings.subtitle",
    permission: PERMISSIONS.platformSettings.manage,
  },
  {
    id: "secrets",
    route: "/admin/secrets",
    titleKey: "secrets.title",
    descriptionKey: "secrets.subtitle",
    permission: PERMISSIONS.secrets.manage,
  },
  {
    id: "profile-account",
    route: "/profile",
    titleKey: "profile.accountDetails",
    descriptionKey: "profile.accountDetailsSubtitle",
  },
  {
    id: "profile-sessions",
    route: "/profile",
    titleKey: "profile.sessions",
  },
  {
    id: "profile-security",
    route: "/profile",
    titleKey: "profile.security",
  },
  {
    id: "notification-templates",
    route: "/notifications/templates",
    titleKey: "notifications.title",
    descriptionKey: "notifications.subtitle",
    permission: PERMISSIONS.notificationTemplates.read,
  },
  {
    id: "notification-layouts",
    route: "/notifications/layouts",
    titleKey: "notifications.layoutsTitle",
    descriptionKey: "notifications.layoutsSubtitle",
    permission: PERMISSIONS.notificationTemplates.read,
  },
  {
    id: "notification-outbox",
    route: "/notifications/outbox",
    titleKey: "notifications.outboxTitle",
    descriptionKey: "notifications.outboxSubtitle",
    permission: PERMISSIONS.notificationTemplates.read,
  },
]
