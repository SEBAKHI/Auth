import { PERMISSIONS } from "./permissions"

export type NotificationDestinationId =
  | "overview"
  | "templates"
  | "layouts"
  | "outbox"
  | "policy"

export type PermissionCheck = (permission: string | undefined) => boolean

export interface NotificationDestination {
  id: NotificationDestinationId
  route: string
  permission: string
  tabLabelKey: string
  search: {
    id: string
    titleKey: string
    descriptionKey: string
    altTitleKeys: string[]
    pathKeys: string[]
  }
}

/**
 * The only notification IA registry. Route guards, the sidebar, tabs, and
 * global search consume these same permission/path pairs.
 */
export const NOTIFICATION_DESTINATIONS: readonly NotificationDestination[] = [
  {
    id: "overview",
    route: "/notifications",
    permission: PERMISSIONS.notificationTemplates.read,
    tabLabelKey: "notifications.tabOverview",
    search: {
      id: "notifications",
      titleKey: "notifications.overviewTitle",
      descriptionKey: "notifications.overviewSubtitle",
      altTitleKeys: ["notifications.tabOverview", "nav.notifications"],
      pathKeys: [],
    },
  },
  {
    id: "templates",
    route: "/notifications/templates",
    permission: PERMISSIONS.notificationTemplates.read,
    tabLabelKey: "notifications.tabTemplates",
    search: {
      id: "notification-templates",
      titleKey: "notifications.title",
      descriptionKey: "notifications.subtitle",
      altTitleKeys: ["notifications.tabTemplates", "nav.notificationTemplates"],
      pathKeys: ["nav.notifications"],
    },
  },
  {
    id: "layouts",
    route: "/notifications/layouts",
    permission: PERMISSIONS.notificationTemplates.read,
    tabLabelKey: "notifications.tabLayouts",
    search: {
      id: "notification-layouts",
      titleKey: "notifications.layoutsTitle",
      descriptionKey: "notifications.layoutsSubtitle",
      altTitleKeys: ["notifications.tabLayouts", "nav.notificationLayouts"],
      pathKeys: ["nav.notifications"],
    },
  },
  {
    id: "outbox",
    route: "/notifications/outbox",
    permission: PERMISSIONS.notificationTemplates.read,
    tabLabelKey: "notifications.tabDeliveryLog",
    search: {
      id: "notification-outbox",
      titleKey: "notifications.outboxTitle",
      descriptionKey: "notifications.outboxSubtitle",
      altTitleKeys: ["notifications.tabDeliveryLog", "nav.notificationOutbox"],
      pathKeys: ["nav.notifications"],
    },
  },
  {
    id: "policy",
    route: "/notifications/policy",
    permission: PERMISSIONS.privacyPolicy.read,
    tabLabelKey: "notifications.tabPolicy",
    search: {
      id: "notification-policy",
      titleKey: "notifications.policyTitle",
      descriptionKey: "notifications.policySubtitle",
      altTitleKeys: ["notifications.tabPolicy", "nav.notificationPolicy"],
      pathKeys: ["nav.notifications"],
    },
  },
] as const

export function notificationDestination(id: NotificationDestinationId) {
  const destination = NOTIFICATION_DESTINATIONS.find((item) => item.id === id)
  if (!destination) throw new Error(`Unknown notification destination: ${id}`)
  return destination
}

export function visibleNotificationDestinations(
  hasPermission: PermissionCheck
) {
  return NOTIFICATION_DESTINATIONS.filter((item) =>
    hasPermission(item.permission)
  )
}

export function notificationLandingPath(
  hasPermission: PermissionCheck
): string | null {
  return visibleNotificationDestinations(hasPermission)[0]?.route ?? null
}

export const NOTIFICATION_SEARCH_SURFACES = NOTIFICATION_DESTINATIONS.map(
  ({ route, permission, search }) => ({ route, permission, ...search })
)
