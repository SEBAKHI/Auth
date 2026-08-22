import { cleanup, render, screen, within } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

import { buildSearchIndex } from "@/components/global-search/build-index"
import { STATIC_SURFACES } from "@/components/global-search/static-surfaces"
import { resolveNavItems } from "@/components/layout/nav-items"
import {
  NOTIFICATION_DESTINATIONS,
  NOTIFICATION_SEARCH_SURFACES,
  notificationDestination,
  notificationLandingPath,
  visibleNotificationDestinations,
} from "@/lib/notification-destinations"
import { PERMISSIONS } from "@/lib/permissions"
import { router } from "@/routes"
import { NotificationsTabs } from "./components/notifications-tabs"

let held = new Set<string>()
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    hasPermission: (permission?: string) => !permission || held.has(permission),
  }),
}))

const notificationSearchIds = new Set(
  NOTIFICATION_SEARCH_SURFACES.map((item) => item.id)
)

const matrix = [
  {
    role: "privacy-only",
    permissions: [PERMISSIONS.privacyPolicy.read],
    destinations: ["policy"],
    landing: "/notifications/policy",
    tabs: ["Privacy Policy"],
  },
  {
    role: "templates-only",
    permissions: [PERMISSIONS.notificationTemplates.read],
    destinations: ["overview", "templates", "layouts", "outbox"],
    landing: "/notifications",
    tabs: ["Overview", "Templates", "Layouts", "Delivery Log"],
  },
  {
    role: "both",
    permissions: [
      PERMISSIONS.notificationTemplates.read,
      PERMISSIONS.privacyPolicy.read,
    ],
    destinations: ["overview", "templates", "layouts", "outbox", "policy"],
    landing: "/notifications",
    tabs: [
      "Overview",
      "Templates",
      "Layouts",
      "Delivery Log",
      "Privacy Policy",
    ],
  },
  {
    role: "neither",
    permissions: [],
    destinations: [],
    landing: null,
    tabs: [],
  },
] as const

afterEach(() => cleanup())

describe("permission-aware notification IA", () => {
  it("keeps the concrete router tree wired to every declared notification destination", () => {
    const paths: string[] = []
    const visit = (routes: typeof router.routes) => {
      for (const route of routes) {
        if (route.path) paths.push(route.path)
        if (route.children) visit(route.children)
      }
    }
    visit(router.routes)

    expect(paths).toEqual(
      expect.arrayContaining([
        "notifications",
        "templates",
        "templates/:id",
        "layouts",
        "layouts/:id",
        "outbox",
        "policy",
        "policy/:id",
      ])
    )
  })

  it.each(matrix)(
    "keeps Route/Nav/Tabs/Search aligned for $role",
    async (entry) => {
      held = new Set(entry.permissions)
      const hasPermission = (permission?: string) =>
        !permission || held.has(permission)

      const visible = visibleNotificationDestinations(hasPermission)
      expect(visible.map((item) => item.id)).toEqual(entry.destinations)
      expect(notificationLandingPath(hasPermission)).toBe(entry.landing)

      const notificationNav = resolveNavItems(hasPermission).find(
        (item) => item.titleKey === "notifications"
      )
      expect(notificationNav?.url ?? null).toBe(entry.landing)

      const indexed = buildSearchIndex([], i18n.t, hasPermission)
        .filter((item) => notificationSearchIds.has(item.id))
        .map((item) => item.id)
      expect(indexed).toEqual(visible.map((item) => item.search.id))

      await i18n.changeLanguage("en")
      render(
        <MemoryRouter
          initialEntries={[entry.landing ?? "/notifications/policy"]}
        >
          <NotificationsTabs />
        </MemoryRouter>
      )
      // Links, not tabs: each section is an address, so the assertion pins the
      // href alongside the label. A tab has no href to pin.
      const sections = within(
        screen.getByRole("navigation", { name: "Notification sections" })
      ).queryAllByRole("link")
      expect(sections.map((link) => link.textContent)).toEqual(entry.tabs)
      expect(
        sections.every((link) => link.getAttribute("href")?.startsWith("/"))
      ).toBe(true)
    }
  )

  it("derives every notification search row from the destination registry", () => {
    const actual = STATIC_SURFACES.filter((surface) =>
      notificationSearchIds.has(surface.id)
    )
    expect(actual).toEqual(NOTIFICATION_SEARCH_SURFACES)
    expect(actual).toHaveLength(NOTIFICATION_DESTINATIONS.length)
  })

  it("fails closed when route metadata requests an unknown destination", () => {
    expect(notificationDestination("policy")).toEqual(
      NOTIFICATION_DESTINATIONS.find(
        (destination) => destination.id === "policy"
      )
    )
    expect(() => notificationDestination("unknown" as never)).toThrowError(
      "Unknown notification destination: unknown"
    )
  })
})
