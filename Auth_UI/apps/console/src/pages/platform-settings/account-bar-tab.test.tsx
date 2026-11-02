import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * Platform settings › Account bar (owner, 2026-11-02): the code a linked application adds,
 * written from choices. Picking an application writes where its own sign-out sends the
 * browser, from its registered return address; with none registered, the tab says so and
 * links to the application's page instead of leaving a blank.
 */

const get = vi.fn()
vi.mock("@authsystem/api/client", () => ({ api: { GET: (...args: unknown[]) => get(...args) } }))

import { AccountBarTab } from "./account-bar-tab"

const APPLICATIONS = [
  { id: "a1", code: "CMS", name: "Commissions", postLogoutRedirectUris: ["https://cms.example.com/signed-out"] },
  { id: "a2", code: "WIKI", name: "Wiki", postLogoutRedirectUris: [] },
]

beforeEach(async () => {
  await i18n.changeLanguage("en")
  get.mockReset()
  get.mockImplementation(async (path: string, options?: { params?: { path?: { id?: string } } }) => {
    if (path === "/api/v1/Applications") {
      return { data: { applications: APPLICATIONS, totalCount: APPLICATIONS.length }, response: new Response() }
    }
    const application = APPLICATIONS.find((item) => item.id === options?.params?.path?.id)
    return { data: application, response: new Response() }
  })
})

function renderTab() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <AccountBarTab />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

function codeUnder(title: string) {
  const heading = screen.getByRole("heading", { name: title })
  return heading.parentElement!.parentElement!.querySelector("code")!.textContent
}

async function pick(user: ReturnType<typeof userEvent.setup>, name: string) {
  await user.click(screen.getByRole("combobox", { name: "Application" }))
  await user.click(await screen.findByRole("option", { name }))
}

describe("AccountBarTab", () => {
  it("writes the header code from the choices", async () => {
    const user = userEvent.setup()
    renderTab()
    expect(codeUnder("Code for the application's header")).toContain('signout-href="/sign-out"')

    await user.click(screen.getByRole("radio", { name: "Avatar only" }))
    await user.click(screen.getByRole("radio", { name: "Dark" }))
    const code = codeUnder("Code for the application's header")
    expect(code).toContain('hide="apps"')
    expect(code).toContain('theme="dark"')
    expect(codeUnder("The application's Content-Security-Policy")).toMatch(/^script-src https:/)
  })

  it("writes the application's end-session address from its registered return address", async () => {
    const user = userEvent.setup()
    renderTab()
    await pick(user, "Commissions")
    expect(await screen.findByRole("heading", { name: "Where the application's sign-out sends the browser" })).toBeInTheDocument()
    expect(codeUnder("Where the application's sign-out sends the browser")).toMatch(
      /\/api\/v1\/auth\/end-session\?client_id=CMS&post_logout_redirect_uri=https%3A%2F%2Fcms\.example\.com%2Fsigned-out$/
    )
  })

  it("links to the application's page when it has no return address yet", async () => {
    const user = userEvent.setup()
    renderTab()
    await pick(user, "Wiki")
    const alert = await screen.findByText(/no return address after sign-out registered yet/)
    expect(within(alert.closest("[data-slot=alert]")!).getByRole("link", { name: "Add one on Wiki's page" })).toHaveAttribute(
      "href",
      "/applications/a2"
    )
    expect(screen.queryByRole("heading", { name: "Where the application's sign-out sends the browser" })).toBeNull()
  })
})
