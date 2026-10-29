import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * A scope's sign-in page designs (an application › Templates; platform settings): the
 * list says which one visitors see, activation and deactivation are confirmed naming the
 * scope's consequence, the active design offers no Delete (its row says why), and a new
 * design opens in the editor.
 *
 * Deliberate breaks: offer Delete on the active row; activate without the confirmation.
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
vi.mock("./login-design-editor", () => ({
  LoginDesignEditor: ({ designId }: { designId: string }) => <div data-testid="editor">{designId}</div>,
}))

import { LoginDesignsManager } from "./login-designs-manager"

const SCOPE = { kind: "application", applicationId: "app-1" } as const
const DESIGNS = [
  { id: "d1", name: "Autumn", isActive: true, showPlatformLogo: false, contentHash: "h1", createdAt: "2026-10-01T10:00:00Z" },
  { id: "d2", name: "Spring", isActive: false, showPlatformLogo: true, contentHash: "h2", createdAt: "2026-10-02T10:00:00Z" },
]

function renderManager() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <LoginDesignsManager scope={SCOPE} canManage appearance={null} />
    </QueryClientProvider>
  )
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  for (const mock of Object.values(api)) mock.mockReset()
  api.listDesigns.mockResolvedValue(DESIGNS)
})

afterEach(cleanup)

describe("LoginDesignsManager", () => {
  it("says which design visitors see, and marks it", async () => {
    renderManager()

    expect(await screen.findByText("Visitors see: Autumn")).toBeInTheDocument()
    expect(screen.getByText("Active")).toBeInTheDocument()
    expect(screen.getByText("Hides the platform logo")).toBeInTheDocument()
    expect(api.listDesigns).toHaveBeenCalledWith(SCOPE)
  })

  it("names the platform's design as the fallback when none is active", async () => {
    api.listDesigns.mockResolvedValue([DESIGNS[1]])
    renderManager()
    expect(await screen.findByText("Visitors see the platform's design.")).toBeInTheDocument()
  })

  it("offers no Delete on the active design, and says why", async () => {
    const user = userEvent.setup()
    renderManager()
    await screen.findByText("Visitors see: Autumn")

    expect(screen.getByText(/Visitors see this design now\. To delete it/)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Actions for Autumn" }))
    const menu = await screen.findByRole("menu")
    expect(within(menu).getByRole("menuitem", { name: "Deactivate" })).toBeInTheDocument()
    expect(within(menu).queryByRole("menuitem", { name: "Delete" })).toBeNull()
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

  it("creates a design by name and opens it in the editor", async () => {
    const user = userEvent.setup()
    api.createDesign.mockResolvedValue({ id: "d3", name: "Winter" })
    renderManager()
    await screen.findByText("Visitors see: Autumn")

    await user.click(screen.getByRole("button", { name: "New design" }))
    const dialog = await screen.findByRole("dialog")
    await user.type(within(dialog).getByRole("textbox", { name: "Name" }), "Winter")
    await user.click(within(dialog).getByRole("button", { name: "Create" }))

    await waitFor(() => expect(api.createDesign).toHaveBeenCalledWith(SCOPE, "Winter"))
    expect(await screen.findByTestId("editor")).toHaveTextContent("d3")
  })
})
