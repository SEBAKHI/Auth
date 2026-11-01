import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

import type { Schemas } from "@authsystem/api/types"
import { ThemeProvider } from "@authsystem/ui/theme-provider"

type MyApplication = Schemas["MyApplicationDto"]

const getMock = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => getMock(...args) },
}))

const { MyApplicationsPage } = await import("./my-applications-page")

function app(
  name: string,
  overrides: Partial<MyApplication> = {}
): MyApplication {
  return {
    id: `id-${name}`,
    name,
    logoUrl: null,
    logoUrlDark: null,
    launchUrl: `https://${name.toLowerCase()}.example.com/`,
    ...overrides,
  }
}

function renderPage(theme: "light" | "dark" = "light") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <ThemeProvider defaultTheme={theme} storageKey="test-theme">
        <MyApplicationsPage />
      </ThemeProvider>
    </QueryClientProvider>
  )
}

function answer(...applications: MyApplication[]) {
  getMock.mockResolvedValue({ data: applications, error: undefined })
}

/**
 * UI batch item 12: Home shows the person's applications as large tiles, the
 * name under the logo, and a tile opens its application in a new tab.
 */
describe("MyApplicationsPage", () => {
  beforeEach(() => {
    getMock.mockReset()
    localStorage.clear()
  })

  it("asks the self-service endpoint, which takes no user id", async () => {
    answer()
    renderPage()

    await screen.findByText("No applications yet")
    expect(getMock).toHaveBeenCalledWith("/api/v1/Users/me/applications")
  })

  it("draws each application as a link that opens a new tab without an opener or a referrer", async () => {
    answer(
      app("Crm"),
      app("Wiki", { launchUrl: "https://wiki.example.com/start?iss=x" })
    )
    renderPage()

    const list = await screen.findByRole("list", { name: "My applications" })
    const links = within(list).getAllByRole("link")
    expect(links).toHaveLength(2)

    const crm = within(list).getByRole("link", {
      name: "Crm (opens in a new tab)",
    })
    expect(crm).toHaveAttribute("href", "https://crm.example.com/")
    expect(crm).toHaveAttribute("target", "_blank")
    expect(crm.getAttribute("rel")?.split(" ")).toEqual(
      expect.arrayContaining(["noopener", "noreferrer"])
    )
    expect(within(list).getByRole("link", { name: /^Wiki/ })).toHaveAttribute(
      "href",
      "https://wiki.example.com/start?iss=x"
    )
  })

  it("keeps the server's order", async () => {
    answer(app("Alpha"), app("Bravo"), app("Charlie"))
    renderPage()

    const list = await screen.findByRole("list", { name: "My applications" })
    expect(
      within(list)
        .getAllByRole("link")
        .map(
          (link) => link.querySelector("[data-slot=item-title]")?.textContent
        )
    ).toEqual(["Alpha", "Bravo", "Charlie"])
  })

  it.each([
    ["javascript:alert(1)"],
    ["http://crm.example.com/"],
    ["/relative/path"],
    ["https://crm.example.com@evil.example.com/"],
    ["http://localhost:3000/"],
  ])("never turns %s into a link", async (launchUrl) => {
    answer(app("Unsafe", { launchUrl }), app("Safe"))
    renderPage()

    const list = await screen.findByRole("list", { name: "My applications" })
    expect(
      within(list)
        .getAllByRole("link")
        .map((link) => link.getAttribute("href"))
    ).toEqual(["https://safe.example.com/"])
    expect(screen.queryByText("Unsafe")).not.toBeInTheDocument()
  })

  it("keeps a hash-routed home address, as the server does", async () => {
    answer(app("Spa", { launchUrl: "https://spa.example.com/#/" }))
    renderPage()

    expect(await screen.findByRole("link", { name: /^Spa/ })).toHaveAttribute(
      "href",
      "https://spa.example.com/#/"
    )
  })

  it("shows the empty state, not an empty grid, when nothing can be opened", async () => {
    answer(app("Unsafe", { launchUrl: "javascript:alert(1)" }))
    renderPage()

    expect(await screen.findByText("No applications yet")).toBeInTheDocument()
    expect(
      screen.queryByRole("list", { name: "My applications" })
    ).not.toBeInTheDocument()
  })

  it("draws placeholder tiles while the list loads", () => {
    getMock.mockReturnValue(new Promise(() => {}))
    renderPage()

    const loading = screen.getByRole("list", { name: "Loading…" })
    expect(loading).toHaveAttribute("aria-busy", "true")
    expect(within(loading).getAllByRole("listitem")).toHaveLength(6)
  })

  it("offers a retry when the list cannot be loaded", async () => {
    getMock.mockResolvedValueOnce({ data: undefined, error: { status: 500 } })
    renderPage()

    const retry = await screen.findByRole("button", { name: "Retry" })
    expect(screen.getByText("Failed to load data.")).toBeInTheDocument()

    answer(app("Crm"))
    await userEvent.click(retry)
    expect(
      await screen.findByRole("link", { name: /^Crm/ })
    ).toBeInTheDocument()
  })

  it("names the link once: the logo is not announced beside the name", async () => {
    answer(app("Crm", { logoUrl: "https://cdn.example.com/crm.webp" }))
    renderPage()

    const link = await screen.findByRole("link", { name: /^Crm/ })
    expect(link.querySelector("[data-slot=item-media]")).toHaveAttribute(
      "aria-hidden",
      "true"
    )
  })

  it("falls back to the initials while a logo is missing or failed", async () => {
    answer(app("Customer Portal", { launchUrl: "https://portal.example.com/" }))
    renderPage()

    const link = await screen.findByRole("link", { name: /^Customer Portal/ })
    expect(within(link).getByText("CP")).toBeInTheDocument()
  })
})
