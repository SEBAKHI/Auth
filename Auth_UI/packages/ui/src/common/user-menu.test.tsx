import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

vi.mock("@authsystem/api/client", () => ({
  api: { GET: vi.fn().mockResolvedValue({ data: {}, error: undefined }) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    user: { firstName: "Jane", lastName: "Doe", email: "jane@example.com" },
    logout: vi.fn(),
  }),
}))

const { UserMenu } = await import("./user-menu")

async function openMenu(props: Parameters<typeof UserMenu>[0]) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <UserMenu {...props} />
      </MemoryRouter>
    </QueryClientProvider>
  )
  await userEvent.click(screen.getByRole("button", { name: "Jane Doe" }))
}

/**
 * The person's applications left the avatar menu for the grid button beside
 * it (owner, 2026-11-02): the menu holds the profile and signing out only.
 */
describe("UserMenu", () => {
  it("offers the profile and signing out, and no applications entry", async () => {
    await openMenu({})

    expect(screen.getByRole("menuitem", { name: "Profile" })).toHaveAttribute(
      "href",
      "/profile"
    )
    expect(screen.getByRole("menuitem", { name: "Sign out" })).toBeInTheDocument()
    expect(screen.getAllByRole("menuitem")).toHaveLength(2)
    expect(
      screen.queryByRole("menuitem", { name: "My applications" })
    ).not.toBeInTheDocument()
  })

  it("drops the profile entry where the shell's nav already links it", async () => {
    await openMenu({ showProfile: false })

    expect(
      screen.queryByRole("menuitem", { name: "Profile" })
    ).not.toBeInTheDocument()
    expect(screen.getAllByRole("menuitem")).toHaveLength(1)
  })

  it("draws its avatar as large as the header's buttons", () => {
    const client = new QueryClient()
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter>
          <UserMenu />
        </MemoryRouter>
      </QueryClientProvider>
    )
    const trigger = screen.getByRole("button", { name: "Jane Doe" })
    expect(trigger).toHaveAttribute("data-size", "icon-header")
    expect(trigger.querySelector("[data-slot=avatar]")).toHaveClass("size-header-button")
  })
})
