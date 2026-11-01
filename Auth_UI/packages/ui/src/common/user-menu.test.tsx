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
 * UI batch item 12, owner decision D9: the console reaches the person's own
 * applications from the user menu. The accounts app links them from its nav
 * (Home), so it passes no route and shows no entry.
 */
describe("UserMenu", () => {
  it("links the person's applications where the shell asks for it", async () => {
    await openMenu({ myApplicationsHref: "/my-applications" })

    expect(
      screen.getByRole("menuitem", { name: "My applications" })
    ).toHaveAttribute("href", "/my-applications")
    expect(
      screen.getByRole("menuitem", { name: "Profile" })
    ).toBeInTheDocument()
  })

  it("shows no such entry by default", async () => {
    await openMenu({})

    expect(
      screen.queryByRole("menuitem", { name: "My applications" })
    ).not.toBeInTheDocument()
  })

  it("keeps the entry when the profile entry is hidden", async () => {
    await openMenu({
      showProfile: false,
      myApplicationsHref: "/my-applications",
    })

    expect(
      screen.getByRole("menuitem", { name: "My applications" })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("menuitem", { name: "Profile" })
    ).not.toBeInTheDocument()
  })
})
