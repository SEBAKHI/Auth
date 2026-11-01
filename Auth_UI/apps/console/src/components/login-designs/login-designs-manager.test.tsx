import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, RouterProvider, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { TooltipProvider } from "@authsystem/ui/tooltip"

/**
 * A scope's sign-in page designs as a table (an application › Templates; platform settings
 * › Templates): the list says which one visitors see; each name opens the design on its own
 * page, carrying the list's address so Back returns to it; activation is confirmed naming the
 * scope's consequence; the active design's Delete stays in its menu, disabled, saying why;
 * and a new design opens on its own page.
 *
 * Deliberate breaks: offer an enabled Delete on the active row; drop the Delete item from
 * the active row; activate without the confirmation; link a name without the list's address.
 */

const api = vi.hoisted(() => ({
  listDesigns: vi.fn(),
  createDesign: vi.fn(),
  copyDesign: vi.fn(),
  setDesignActive: vi.fn(),
  deleteDesign: vi.fn(),
}))

vi.mock("./login-design-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./login-design-api")>()),
  ...api,
}))

import { LoginDesignsManager } from "./login-designs-manager"

const SCOPE = { kind: "application", applicationId: "app-1" } as const
const DESIGNS = [
  { id: "d1", name: "Autumn", isActive: true, showPlatformLogo: false, contentHash: "h1", createdAt: "2026-10-01T10:00:00Z" },
  { id: "d2", name: "Spring", isActive: false, showPlatformLogo: true, contentHash: "h2", createdAt: "2026-10-02T10:00:00Z" },
]

function DesignRoute() {
  const location = useLocation()
  return (
    <div data-testid="design-page">
      {location.pathname}|{JSON.stringify(location.state)}
    </div>
  )
}

function renderManager(initialEntry = "/applications/app-1?tab=templates") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: "/applications/:id",
        element: (
          <LoginDesignsManager scope={SCOPE} canManage tableId="app-login-designs" urlNamespace="designs" />
        ),
      },
      { path: "/applications/:id/login-designs/:designId", element: <DesignRoute /> },
    ],
    { initialEntries: [initialEntry] }
  )
  render(
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <RouterProvider router={router} />
      </TooltipProvider>
    </QueryClientProvider>
  )
  return router
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  for (const mock of Object.values(api)) mock.mockReset()
  api.listDesigns.mockResolvedValue(DESIGNS)
})

afterEach(cleanup)

describe("LoginDesignsManager", () => {
  it("lists the designs in a table and says which one visitors see", async () => {
    renderManager()

    expect(await screen.findByText("Visitors see: Autumn")).toBeInTheDocument()
    const table = screen.getByRole("table")
    const autumn = within(table).getByRole("row", { name: /Autumn/ })
    expect(within(autumn).getByText("Active")).toBeInTheDocument()
    expect(within(autumn).getByText("Hidden")).toBeInTheDocument()
    expect(within(within(table).getByRole("row", { name: /Spring/ })).getByText("Inactive")).toBeInTheDocument()
    expect(api.listDesigns).toHaveBeenCalledWith(SCOPE)
  })

  it("names the platform's design as the fallback when none is active", async () => {
    api.listDesigns.mockResolvedValue([DESIGNS[1]])
    renderManager()
    expect(await screen.findByText("Visitors see the platform's design.")).toBeInTheDocument()
  })

  it("opens a design on its own page, handing over the list's address with its search", async () => {
    const user = userEvent.setup()
    const router = renderManager("/applications/app-1?tab=templates&designs.q=spr")

    await user.click(await screen.findByRole("link", { name: "Spring" }))

    expect(router.state.location.pathname).toBe("/applications/app-1/login-designs/d2")
    expect(router.state.location.state).toEqual({
      returnTo: "/applications/app-1?tab=templates&designs.q=spr",
    })
  })

  it("keeps Delete in the active design's menu, disabled, and says why", async () => {
    const user = userEvent.setup()
    renderManager()
    await screen.findByText("Visitors see: Autumn")

    await user.click(screen.getByRole("button", { name: "Actions for Autumn" }))
    const menu = await screen.findByRole("menu")
    expect(within(menu).getByRole("menuitem", { name: "Deactivate" })).toBeInTheDocument()
    const remove = within(menu).getByRole("menuitem", { name: "Delete" })
    expect(remove).toHaveAttribute("aria-disabled", "true")

    // Written in the menu, not on hover: keyboard and touch readers see it too.
    expect(within(menu).getByText(/Visitors see this design now\. To delete it/)).toBeVisible()
    expect(remove).toHaveAccessibleDescription(/Visitors see this design now\. To delete it/)
  })

  it("deletes an inactive design after a confirmation that names it", async () => {
    const user = userEvent.setup()
    api.deleteDesign.mockResolvedValue(undefined)
    renderManager()
    await screen.findByText("Visitors see: Autumn")

    await user.click(screen.getByRole("button", { name: "Actions for Spring" }))
    await user.click(await screen.findByRole("menuitem", { name: "Delete" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Delete Spring?")).toBeInTheDocument()
    expect(api.deleteDesign).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole("button", { name: "Delete" }))
    await waitFor(() => expect(api.deleteDesign).toHaveBeenCalledWith(SCOPE, "d2"))
  })

  it("activates only after a confirmation that says what visitors will see", async () => {
    const user = userEvent.setup()
    api.setDesignActive.mockResolvedValue({ ...DESIGNS[1], isActive: true })
    renderManager()
    await screen.findByText("Visitors see: Autumn")

    await user.click(screen.getByRole("button", { name: "Actions for Spring" }))
    await user.click(await screen.findByRole("menuitem", { name: "Activate" }))
    const dialog = await screen.findByRole("alertdialog")
    expect(within(dialog).getByText("Activate Spring?")).toBeInTheDocument()
    expect(within(dialog).getByText(/will see this design from now on/)).toBeInTheDocument()
    expect(api.setDesignActive).not.toHaveBeenCalled()

    await user.click(within(dialog).getByRole("button", { name: "Activate" }))
    await waitFor(() => expect(api.setDesignActive).toHaveBeenCalledWith(SCOPE, "d2", true))
  })

  it("creates a design by name and opens it on its own page", async () => {
    const user = userEvent.setup()
    api.createDesign.mockResolvedValue({ id: "d3", name: "Winter" })
    renderManager()
    await screen.findByText("Visitors see: Autumn")

    await user.click(screen.getByRole("button", { name: "New design" }))
    const dialog = await screen.findByRole("dialog")
    await user.type(within(dialog).getByRole("textbox", { name: "Name" }), "Winter")
    await user.click(within(dialog).getByRole("button", { name: "Create" }))

    await waitFor(() => expect(api.createDesign).toHaveBeenCalledWith(SCOPE, "Winter"))
    expect(await screen.findByTestId("design-page")).toHaveTextContent("/applications/app-1/login-designs/d3")
  })
})
