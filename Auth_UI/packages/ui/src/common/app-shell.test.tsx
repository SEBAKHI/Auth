import type { ReactNode } from "react"
import { act, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { Building2, LayoutDashboard } from "lucide-react"
import { Navigate, RouterProvider, createMemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"
import { DirectionProvider } from "@authsystem/i18n/direction"
import { TooltipProvider } from "@authsystem/ui/tooltip"
import { crumb } from "@authsystem/ui/crumbs"
import { AppShell, type AppNavItem } from "./app-shell"

// The header's account, language and theme controls each pull an API-backed
// context of their own and none of them take part in the navigation below.
vi.mock("@authsystem/ui/common/user-menu", () => ({ UserMenu: () => null }))
vi.mock("@authsystem/ui/common/language-toggle", () => ({
  LanguageToggle: () => null,
}))
vi.mock("@authsystem/ui/common/theme-toggle", () => ({ ThemeToggle: () => null }))
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ name: "Auth", logoUrl: null, isPending: false }),
  BrandingLogo: ({ fallback }: { fallback: ReactNode }) => fallback,
}))

const PHONE_WIDTH = 390
const DESKTOP_WIDTH = window.innerWidth

const NAV: AppNavItem[] = [
  { titleKey: "dashboard", url: "/", icon: LayoutDashboard },
  { titleKey: "organizations", url: "/organizations", icon: Building2 },
]

function setViewportWidth(width: number) {
  Object.defineProperty(window, "innerWidth", {
    configurable: true,
    writable: true,
    value: width,
  })
}

/** The accounts app's shape: `/` redirects, and `/profile` is the real home. */
function renderLandingShell(...initialEntries: string[]) {
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: (
          <AppShell
            navItems={NAV}
            navGroupKey="account"
            homeKey="account"
            homeHref="/profile"
          />
        ),
        children: [
          { index: true, element: <Navigate to="/profile" replace /> },
          {
            path: "profile",
            element: <div>profile page</div>,
            handle: crumb("profile", "/profile"),
          },
          {
            path: "organizations",
            element: <div>organizations page</div>,
            handle: crumb("organizations", "/organizations"),
          },
        ],
      },
    ],
    { initialEntries, initialIndex: initialEntries.length - 1 }
  )

  render(
    <DirectionProvider>
      <TooltipProvider>
        <RouterProvider router={router} />
      </TooltipProvider>
    </DirectionProvider>
  )
}

function renderShell(...initialEntries: string[]) {
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: (
          <AppShell
            navItems={NAV}
            navGroupKey="platform"
            homeKey="dashboard"
          />
        ),
        handle: crumb("dashboard", "/"),
        children: [
          { index: true, element: <div>dashboard page</div> },
          {
            path: "organizations",
            element: <div>organizations page</div>,
            handle: crumb("organizations", "/organizations"),
          },
          {
            path: "organizations/:id",
            element: <div>organization page</div>,
            handle: crumb("organizations", "/organizations", true),
          },
        ],
      },
    ],
    { initialEntries, initialIndex: initialEntries.length - 1 }
  )

  render(
    // Both apps mount the shell under these two providers.
    <DirectionProvider>
      <TooltipProvider>
        <RouterProvider router={router} />
      </TooltipProvider>
    </DirectionProvider>
  )

  return router
}

/** The mobile nav is a Sheet, so it is the one dialog the shell can show. */
const drawer = () => screen.queryByRole("dialog")

async function openDrawer(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole("button", { name: /toggle sidebar/i }))
  return screen.findByRole("dialog")
}

function navLink(container: HTMLElement, href: string) {
  const link = within(container)
    .getAllByRole("link")
    .find((candidate) => candidate.getAttribute("href") === href)
  if (!link) throw new Error(`No sidebar link for ${href}`)
  return link
}

describe("AppShell mobile navigation", () => {
  beforeEach(() => setViewportWidth(PHONE_WIDTH))
  afterEach(() => setViewportWidth(DESKTOP_WIDTH))

  it("closes the nav drawer after following one of its links", async () => {
    const user = userEvent.setup()
    renderShell("/")

    await user.click(navLink(await openDrawer(user), "/organizations"))

    // Without this the drawer stays on top of the page it just opened, and a
    // phone has no Escape key to dismiss it with.
    await waitFor(() => expect(drawer()).toBeNull())
    expect(screen.getByText("organizations page")).toBeInTheDocument()
  })

  it("closes the nav drawer when the entry for the current page is tapped", async () => {
    const user = userEvent.setup()
    renderShell("/organizations")

    // The route does not change here, so nothing but the tap itself can close
    // the drawer.
    await user.click(navLink(await openDrawer(user), "/organizations"))

    await waitFor(() => expect(drawer()).toBeNull())
  })

  it("closes the nav drawer when navigation comes from outside it", async () => {
    const user = userEvent.setup()
    const router = renderShell("/", "/organizations")

    await openDrawer(user)
    await act(async () => {
      await router.navigate(-1) // the browser's Back button
    })

    await waitFor(() => expect(drawer()).toBeNull())
  })

  it("keeps the breadcrumb trail inside its own box", () => {
    renderShell("/")

    // jsdom lays nothing out, so the guard is the containment contract itself:
    // the trail may shrink to nothing beside the header controls, and when it
    // does it has to clip and ellipsize instead of painting over them.
    const trail = document.querySelector<HTMLElement>('[data-slot="breadcrumb"]')
    expect(trail?.className).toContain("min-w-0")
    expect(trail?.className).toContain("overflow-hidden")

    // The home crumb carries the page title on the landing page, and it is the
    // one crumb that used to be rendered without a clamp.
    const page = document.querySelector<HTMLElement>(
      '[data-slot="breadcrumb-page"]'
    )
    expect(page?.className).toContain("truncate")
    expect(page?.closest('[data-slot="breadcrumb-item"]')?.className).toContain(
      "min-w-0"
    )
  })

  it("leaves the page reachable around the open drawer", async () => {
    const user = userEvent.setup()
    renderShell("/")

    const content = await openDrawer(user)

    // House sheets are full-bleed below `sm`; this one has no close button, so
    // covering the viewport would leave no way out at all. It stays a drawer
    // with a tappable overlay beside it at every width.
    expect(content.className).toContain("max-w-(--sidebar-width)")
    expect(content.className).not.toMatch(/sm:max-w-/)
    expect(
      document.querySelector('[data-slot="sheet-overlay"]')
    ).not.toBeNull()
  })
})

/**
 * The way back on a screen too narrow for a trail.
 *
 * The header gives the trail about a hundred pixels below `lg`, which truncated
 * every crumb to two characters - a trail that answers neither of the two
 * questions a breadcrumb exists to answer. These cases pin the replacement:
 * one link, naming the page one level up.
 */
describe("AppShell parent link", () => {
  const parentLink = () =>
    screen
      .queryAllByRole("link")
      .find((link) =>
        link.querySelector('[data-slot="parent-link-label"]')
      )

  it("points a record back at the list it belongs to", () => {
    renderShell("/organizations/abc")

    const link = parentLink()
    expect(link).toBeDefined()
    expect(link).toHaveAttribute("href", "/organizations")
    expect(link).toHaveTextContent("Organizations")
  })

  it("points a top-level list back at home", () => {
    renderShell("/organizations")

    expect(parentLink()).toHaveAttribute("href", "/")
  })

  it("offers nothing to climb from home itself", () => {
    renderShell("/")

    expect(parentLink()).toBeUndefined()
  })

  it("keeps the full trail available for wide screens", () => {
    renderShell("/organizations/abc")

    // Both surfaces exist in the markup; CSS decides which one the width gets.
    const trail = document.querySelector('[data-slot="breadcrumb"]')
    expect(trail).not.toBeNull()
    expect(trail?.className).toContain("lg:flex")
  })

  it("offers nothing to climb from a landing page that is not `/`", () => {
    // The accounts app's shape: `/` only redirects, and the real landing page
    // carries a crumb of its own. Recognising home by the crumb KEY alone read
    // that page as an inner page and offered a back link to `/` - which
    // redirects straight back to it.
    renderLandingShell("/profile")

    expect(parentLink()).toBeUndefined()
  })

  it("still climbs to the declared landing page from a sibling", () => {
    renderLandingShell("/organizations")

    expect(parentLink()).toHaveAttribute("href", "/profile")
  })
})
