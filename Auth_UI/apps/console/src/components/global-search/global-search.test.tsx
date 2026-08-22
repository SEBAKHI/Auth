import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { MemoryRouter, useLocation } from "react-router-dom"
import { Users } from "lucide-react"

import i18n from "@authsystem/i18n"

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    // The start-state contract does not need settings. Keeping that permission
    // off also proves the idle state makes no settings request.
    hasPermission: (permission: string | undefined) =>
      permission !== "system-settings:manage",
    user: { id: "11111111-1111-1111-1111-111111111111" },
  }),
}))

const recordSearch = vi.hoisted(() => ({
  state: {
    groups: [] as Array<{
      sourceKey: string
      headingKey: string
      icon: typeof Users
      listRoute: string
      entries: Array<{
        kind: "record"
        id: string
        sourceKey: string
        title: string
        description: string
        route: string
        keywords: string
      }>
      totalEntries: number
    }>,
    total: 0,
    isPending: false,
    isError: false,
    retry: vi.fn(),
  },
}))

vi.mock("./use-record-search", () => ({
  useRecordSearch: () => recordSearch.state,
}))

import { GlobalSearch } from "./global-search"

const USER_ID = "11111111-1111-1111-1111-111111111111"
const RECENT_KEY = `authsystem.settingsSearch.recent.${USER_ID}`

function LocationProbe() {
  const location = useLocation()
  return (
    <output aria-label="current location">
      {location.pathname + location.search}
    </output>
  )
}

function renderSearch() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/origin"]}>
        <GlobalSearch />
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

async function openSearch() {
  await userEvent.click(screen.getByRole("button", { name: "Search" }))
  return screen.findByRole("dialog", { name: "Search" })
}

describe("GlobalSearch idle start state", () => {
  beforeEach(async () => {
    localStorage.clear()
    await i18n.changeLanguage("en")
    recordSearch.state.groups = []
    recordSearch.state.total = 0
    recordSearch.state.isPending = false
    recordSearch.state.isError = false
    recordSearch.state.retry.mockReset()
  })

  it("shows quick navigation when there is no history", async () => {
    renderSearch()
    const dialog = await openSearch()

    expect(within(dialog).queryByText("Recent", { exact: true })).toBeNull()
    expect(within(dialog).getByText("Jump to", { exact: true })).toBeVisible()
    expect(within(dialog).getByText("Users", { exact: true })).toBeVisible()
    expect(dialog.querySelector('[data-slot="command-separator"]')).toBeNull()
  })

  it("shows Recent then a separator then Jump to when history exists", async () => {
    localStorage.setItem(
      RECENT_KEY,
      JSON.stringify([{ id: "profile", route: "/profile" }])
    )
    renderSearch()
    const dialog = await openSearch()

    const recentHeading = within(dialog).getByText("Recent", { exact: true })
    const jumpHeading = within(dialog).getByText("Jump to", { exact: true })
    const separator = dialog.querySelector('[data-slot="command-separator"]')

    expect(recentHeading).toBeVisible()
    expect(within(dialog).getByText("My profile", { exact: true })).toBeVisible()
    expect(separator).toBeVisible()
    expect(jumpHeading).toBeVisible()
    expect(
      recentHeading.compareDocumentPosition(jumpHeading) &
        Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy()
  })

  it("clears only Recent and keeps quick navigation operational", async () => {
    localStorage.setItem(
      RECENT_KEY,
      JSON.stringify([{ id: "profile", route: "/profile" }])
    )
    renderSearch()
    const dialog = await openSearch()

    await userEvent.click(within(dialog).getByText("Clear recent", { exact: true }))

    expect(within(dialog).queryByText("Recent", { exact: true })).toBeNull()
    expect(within(dialog).getByText("Jump to", { exact: true })).toBeVisible()
    expect(localStorage.getItem(RECENT_KEY)).toBe("[]")

    await userEvent.click(within(dialog).getByText("Users", { exact: true }))
    expect(screen.getByLabelText("current location")).toHaveTextContent("/users")
  })

  it("opens a static result and spends Escape on clearing before closing", async () => {
    renderSearch()
    const dialog = await openSearch()
    const input = within(dialog).getByPlaceholderText("Search the console…")

    await userEvent.type(input, "users")
    await userEvent.keyboard("{Escape}")
    expect(input).toHaveValue("")
    expect(dialog).toBeVisible()

    await userEvent.type(input, "users")
    await userEvent.click(within(dialog).getByText("Users", { exact: true }))
    expect(screen.getByLabelText("current location")).toHaveTextContent("/users")
  })

  it("explains an empty query after record search settles", async () => {
    renderSearch()
    const dialog = await openSearch()

    await userEvent.type(
      within(dialog).getByPlaceholderText("Search the console…"),
      "zzzzzz"
    )

    expect(
      await within(dialog).findByText(/Nothing matches/, {}, { timeout: 1_000 })
    ).toBeVisible()
    expect(within(dialog).getByText(/Try fewer words/)).toBeVisible()
  })

  it("renders record failures and hands capped groups to their owning list", async () => {
    recordSearch.state.groups = [
      {
        sourceKey: "user",
        headingKey: "nav.users",
        icon: Users,
        listRoute: "/users",
        entries: [
          {
            kind: "record",
            id: "user:1",
            sourceKey: "user",
            title: "Alice Operator",
            description: "alice@example.test",
            route: "/users/1",
            keywords: "",
          },
        ],
        totalEntries: 8,
      },
    ]
    recordSearch.state.total = 1
    recordSearch.state.isError = true
    renderSearch()
    const dialog = await openSearch()

    await userEvent.type(
      within(dialog).getByPlaceholderText("Search the console…"),
      "alice"
    )
    expect(
      within(dialog).getByRole("option", { name: /Alice Operator/ })
    ).toBeVisible()
    expect(within(dialog).getByText("Some results couldn’t be loaded.")).toBeVisible()
    await userEvent.click(within(dialog).getByText("Try again"))
    expect(recordSearch.state.retry).toHaveBeenCalledOnce()

    await userEvent.click(within(dialog).getByText("See all in Users"))
    expect(screen.getByLabelText("current location")).toHaveTextContent(
      "/users?q=alice"
    )
  })
})
