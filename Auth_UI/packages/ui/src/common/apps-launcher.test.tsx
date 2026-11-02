import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

import type { Schemas } from "@authsystem/api/types"
import { ThemeProvider } from "@authsystem/ui/theme-provider"

const getMock = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => getMock(...args) },
}))

const { AppsLauncher } = await import("./apps-launcher")
// The panel's chunk, loaded ahead so the lazy import resolves at once: these
// tests are about what the panel does, not how long a loaded runner takes to
// transform a module (the full suite timed out on it).
await import("./application-tiles")

function app(name: string, launchUrl: string): Schemas["MyApplicationDto"] {
  return { id: `id-${name}`, name, logoUrl: null, logoUrlDark: null, launchUrl }
}

function renderLauncher() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <ThemeProvider defaultTheme="light" storageKey="test-theme">
        <AppsLauncher />
      </ThemeProvider>
    </QueryClientProvider>
  )
}

/**
 * The grid button beside the avatar (owner, 2026-11-02): the person's
 * applications as tiles in a panel, fetched only when the panel opens.
 */
describe("AppsLauncher", () => {
  beforeEach(() => {
    getMock.mockReset()
    localStorage.clear()
    getMock.mockResolvedValue({
      data: [
        app("Crm", "https://crm.example.com/"),
        app("Wiki", "https://wiki.example.com/"),
        // Not https: dropped, never drawn as a tile.
        app("Legacy", "http://legacy.example.com/"),
      ],
      error: undefined,
    })
  })

  it("is a header-sized icon button named in the reader's language, and asks for nothing until opened", () => {
    renderLauncher()

    const button = screen.getByRole("button", { name: "Applications" })
    expect(button).toHaveAttribute("data-size", "icon-header")
    expect(button).toHaveAttribute("aria-haspopup", "dialog")
    expect(getMock).not.toHaveBeenCalled()
  })

  it("opens a panel of the person's application tiles, each opening a new tab", async () => {
    renderLauncher()

    await userEvent.click(screen.getByRole("button", { name: "Applications" }))

    const list = await screen.findByRole("list", { name: "Applications" }, { timeout: 5000 })
    expect(getMock).toHaveBeenCalledWith("/api/v1/Users/me/applications")
    const links = within(list).getAllByRole("link")
    expect(links).toHaveLength(2)
    expect(links[0]).toHaveAttribute("href", "https://crm.example.com/")
    expect(links[0]).toHaveAttribute("target", "_blank")
    expect(links[0]).toHaveAttribute("rel", "noopener noreferrer")
    expect(within(list).queryByText("Legacy")).not.toBeInTheDocument()
  })

  it("closes once a tile is followed", async () => {
    renderLauncher()
    await userEvent.click(screen.getByRole("button", { name: "Applications" }))
    const list = await screen.findByRole("list", { name: "Applications" }, { timeout: 5000 })

    const crm = within(list).getAllByRole("link")[0]
    // jsdom does not navigate; the click handler is what is under test.
    crm.addEventListener("click", (event) => event.preventDefault())
    await userEvent.click(crm)

    await waitFor(() =>
      expect(screen.queryByRole("list", { name: "Applications" })).not.toBeInTheDocument()
    )
  })
})
