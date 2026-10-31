import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, RouterProvider } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * A sign-in page design on its own page (D4 of the UI batch): Back returns to the list it
 * was opened from with that list's search, sort and filters (handed over in router state),
 * and to the owner's "Templates" tab otherwise, never to an address the state names outside
 * that list; a design that no longer exists says so and offers the way back instead of
 * loading forever.
 *
 * Deliberate breaks: ignore the handed-over address; accept any address from the state;
 * render the skeleton for a failed read.
 */

const api = vi.hoisted(() => ({ getDesign: vi.fn(), previewDesign: vi.fn(), saveDesign: vi.fn() }))

vi.mock("./login-design-api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./login-design-api")>()),
  ...api,
}))
vi.mock("./design-preview", () => ({ DesignPreview: () => null }))
vi.mock("@/pages/notifications/components/code-editor", () => ({
  CodeEditor: () => <div aria-label="code editor" />,
}))
vi.mock("@/pages/notifications/components/variable-palette", () => ({ VariablePalette: () => null }))

import { LoginDesignPage } from "./login-design-page"

const SCOPE = { kind: "application", applicationId: "app-1" } as const
const DESIGN = {
  id: "d1",
  applicationId: "app-1",
  name: "Autumn",
  content: "<h1>Hi</h1>",
  stringsJson: "{}",
  showPlatformLogo: true,
  isActive: false,
  contentHash: "h1",
  revision: "2026-10-29T10:00:00Z",
  createdAt: "2026-10-29T10:00:00Z",
  warnings: [],
}

function renderPage(state?: unknown) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: "/applications/:id/login-designs/:designId",
        element: <LoginDesignPage scope={SCOPE} canManage appearance={null} description="App designs" />,
      },
      { path: "/applications/:id", element: <p>application page</p> },
      { path: "/elsewhere", element: <p>elsewhere</p> },
    ],
    { initialEntries: [{ pathname: "/applications/app-1/login-designs/d1", state }] }
  )
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
  return router
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  for (const mock of Object.values(api)) mock.mockReset()
  api.getDesign.mockResolvedValue(DESIGN)
  api.previewDesign.mockResolvedValue({ html: "<h1>Hi</h1>", language: "en", dir: "ltr", warnings: [] })
})

afterEach(cleanup)

describe("LoginDesignPage", () => {
  it("names the page after the design, and Back returns to the list as it was left", async () => {
    const user = userEvent.setup()
    const router = renderPage({ returnTo: "/applications/app-1?tab=templates&designs.q=aut" })

    expect(await screen.findByRole("heading", { name: "Autumn" })).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Back to designs" }))

    expect(router.state.location.pathname).toBe("/applications/app-1")
    expect(router.state.location.search).toBe("?tab=templates&designs.q=aut")
  })

  it("goes back to the owner's Templates tab when opened directly, or when the state names another place", async () => {
    const user = userEvent.setup()
    const direct = renderPage()
    await user.click(await screen.findByRole("button", { name: "Back to designs" }))
    expect(`${direct.state.location.pathname}${direct.state.location.search}`).toBe("/applications/app-1?tab=templates")
    cleanup()

    const foreign = renderPage({ returnTo: "/elsewhere" })
    await user.click(await screen.findByRole("button", { name: "Back to designs" }))
    expect(`${foreign.state.location.pathname}${foreign.state.location.search}`).toBe("/applications/app-1?tab=templates")
  })

  it("says a deleted design no longer exists, and still offers the way back", async () => {
    const user = userEvent.setup()
    api.getDesign.mockRejectedValue({ status: 404, code: "LoginDesign.NotFound" })
    const router = renderPage()

    expect(await screen.findByText("This design no longer exists. It may have been deleted.")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Back to designs" }))
    expect(router.state.location.pathname).toBe("/applications/app-1")
  })
})
