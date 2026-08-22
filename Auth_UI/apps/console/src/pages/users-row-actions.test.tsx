import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ReactNode } from "react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const mocks = vi.hoisted(() => ({ apiCall: vi.fn() }))

const ROW = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "row@example.test",
  firstName: "Row",
  lastName: "Person",
  displayName: "Row Person",
  status: "Active",
  isDeleted: false,
  roles: [],
}

let row: Record<string, unknown> = ROW

vi.mock("@authsystem/api/client", () => {
  const request = (...args: unknown[]) => {
    mocks.apiCall(...args)
    // Array-shaped and object-shaped at once: some of these endpoints answer
    // with a bare list and some with a paged envelope.
    return Promise.resolve({
      data: Object.assign([row], {
        users: [row],
        items: [row],
        applications: [row],
        organizations: [row],
        templates: [row],
        layouts: [row],
        versions: [row],
        roles: [],
        permissions: [],
        totalCount: 1,
        totalPages: 1,
      }),
      error: undefined,
    })
  }
  return {
    api: {
      GET: request,
      POST: request,
      PUT: request,
      PATCH: request,
      DELETE: request,
    },
  }
})

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    hasPermission: () => true,
    user: { id: "99999999-9999-9999-9999-999999999999" },
  }),
}))

// Renders the real cells so the row menu is reachable, while leaving the table's
// own toolbar, paging and layout store out of these cases.
vi.mock("@authsystem/ui/data-table/data-table", () => ({
  DataTable: ({
    columns,
    data,
  }: {
    columns: Array<{ id?: string; cell?: (context: unknown) => ReactNode }>
    data: unknown[]
  }) => (
    <div>
      {data.map((original, index) => (
        <div key={index}>
          {columns.map((column, columnIndex) => (
            <span key={column.id ?? columnIndex}>
              {column.cell?.({ row: { original } })}
            </span>
          ))}
        </div>
      ))}
    </div>
  ),
}))

import { OrganizationsAdminPage } from "./organizations/organizations-admin-page"
import { PermissionsPage } from "./permissions/permissions-page"
import { RolesPage } from "./roles/roles-page"
import { ApplicationsPage } from "./applications/applications-page"
import { UsersPage } from "./users/users-page"

/**
 * The row action menu on /users.
 *
 * Every item opens a confirmation or a dialog before anything is sent, and a
 * deleted account is deliberately reduced to one destructive action. Both are
 * easy to break while rearranging a menu, and neither is reachable from a test
 * that only inspects the column definitions.
 */
function renderUsers() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/"]}>
        <UsersPage />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

async function openRowMenu() {
  const user = userEvent.setup()
  renderUsers()
  await waitFor(() =>
    expect(screen.getByRole("button", { name: /actions/i })).toBeInTheDocument()
  )
  await user.click(screen.getByRole("button", { name: /actions/i }))
  return user
}

describe("user row actions", () => {
  afterEach(() => {
    cleanup()
    row = ROW
    mocks.apiCall.mockClear()
  })

  it.each([
    ["Edit", "dialog", /edit user/i],
    ["Manage roles", "dialog", /manage roles/i],
    ["Manage permissions", "dialog", /manage permissions/i],
    ["Lock", "alertdialog", /lock/i],
    ["Delete", "alertdialog", /delete/i],
  ])(
    "%s asks before it acts",
    async (item, role, dialogName) => {
      const user = await openRowMenu()

      await user.click(await screen.findByRole("menuitem", { name: item }))

      const dialog = await screen.findByRole(role, { name: dialogName })
      expect(dialog).toBeInTheDocument()
      // Opening a surface is not the same as performing the action.
      expect(mocks.apiCall).not.toHaveBeenCalledWith(
        expect.stringContaining("/lock"),
        expect.anything()
      )
    },
    20_000
  )

  it("reduces a deleted account to permanent removal", async () => {
    row = { ...ROW, isDeleted: true }
    const user = await openRowMenu()

    const items = await screen.findAllByRole("menuitem")
    expect(items).toHaveLength(1)

    await user.click(items[0])
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument()
  }, 20_000)

  it("offers unlock instead of lock for a locked account", async () => {
    row = { ...ROW, status: "Locked" }
    await openRowMenu()

    expect(
      await screen.findByRole("menuitem", { name: /unlock/i })
    ).toBeInTheDocument()
    expect(screen.queryByRole("menuitem", { name: "Lock account" })).toBeNull()
  }, 20_000)
})

/**
 * The same rule across every list: a row menu asks before it acts.
 *
 * Each item either navigates (a link, already covered by the link matrix) or
 * opens a dialog. None of them may reach the API on the click that opened the
 * menu - a delete that fires straight from a menu item is unrecoverable, and
 * the menus are rearranged often enough that this is worth pinning once for all
 * of them rather than page by page.
 */
/**
 * The five lists that carry a row action menu. The notification lists are
 * absent because they have none: templates and layouts offer no row commands at
 * all, and policy revisions put Clone and Publish inline as visible buttons.
 */
const LISTS = [
  ["users", () => <UsersPage />],
  ["applications", () => <ApplicationsPage />],
  ["roles", () => <RolesPage />],
  ["permissions", () => <PermissionsPage />],
  ["organizations", () => <OrganizationsAdminPage />],
] as const

describe("row menus ask before they act", () => {
  afterEach(() => {
    cleanup()
    row = ROW
    mocks.apiCall.mockClear()
  })

  async function openMenu(page: () => ReactNode) {
    const user = userEvent.setup()
    const client = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={["/"]}>{page()}</MemoryRouter>
      </QueryClientProvider>
    )
    await waitFor(() =>
      expect(
        screen.getAllByRole("button", { name: /actions/i }).length
      ).toBeGreaterThan(0)
    )
    await user.click(screen.getAllByRole("button", { name: /actions/i })[0])
    return user
  }

  function sentARequestWithABody() {
    return mocks.apiCall.mock.calls.some(
      (call) =>
        typeof call[1] === "object" &&
        call[1] !== null &&
        "body" in (call[1] as object)
    )
  }

  it.each(LISTS)(
    "%s",
    async (_name, page) => {
      await openMenu(page)
      const labels = screen.getAllByRole("menuitem").map((item) => ({
        label: item.textContent ?? "",
        isLink: item.getAttribute("href") !== null,
      }))
      expect(labels.length).toBeGreaterThan(0)
      cleanup()

      // One fresh render per item: Radix hides the trigger while its menu is
      // open, so reopening in a loop fights the primitive rather than the code.
      for (const { label, isLink } of labels) {
        mocks.apiCall.mockClear()
        const user = await openMenu(page)
        await user.click(await screen.findByRole("menuitem", { name: label }))

        // Restoring access (activate, unlock) may act on the click; anything that
        // takes access or data away has to ask first.
        const restoresAccess = /^(activate|unlock)$/i.test(label.trim())
        if (!isLink && !restoresAccess) {
          const surface =
            screen.queryByRole("dialog") ?? screen.queryByRole("alertdialog")
          expect(surface, `"${label}" opened nothing`).not.toBeNull()
        }
        if (!restoresAccess) {
          expect(
            sentARequestWithABody(),
            `"${label}" sent a request straight from the menu`
          ).toBe(false)
        }
        cleanup()
      }
    },
    60_000
  )
})
