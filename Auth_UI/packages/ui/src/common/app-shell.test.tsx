import type { ReactNode } from "react"
import { act, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { Building2, LayoutDashboard } from "lucide-react"
import { RouterProvider, createMemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"
import { DirectionProvider } from "@authsystem/i18n/direction"
import { TooltipProvider } from "@authsystem/ui/tooltip"
import { AppShell, type AppNavItem } from "./app-shell"

// The header's account, language and theme controls each pull an API-backed
// context of their own and none of them take part in the navigation below.
vi.mock("@authsystem/ui/common/user-menu", () => ({ UserMenu: () => null }))
vi.mock("@authsystem/ui/common/language-toggle", () => ({
  LanguageToggle: () => null,
}))
vi.mock("@authsystem/ui/common/theme-toggle", () => ({ ThemeToggle: () => null }))
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ name: "Auth", logoUrl: null }),
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
        children: [
          { index: true, element: <div>dashboard page</div> },
          { path: "organizations", element: <div>organizations page</div> },
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
