import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  cleanup,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ReactNode } from "react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

const mocks = vi.hoisted(() => ({
  tables: new Map<string, Record<string, unknown>>(),
  apiCall: vi.fn(),
}))

function emptyPayload() {
  return Object.assign([], {
    id: "11111111-1111-1111-1111-111111111111",
    name: "Test entity",
    code: "TEST",
    email: "test@example.test",
    isActive: true,
    accessMode: "Restricted",
    users: [],
    applications: [],
    organizations: [],
    roles: [],
    permissions: [],
    logs: [],
    templates: [],
    layouts: [],
    items: [],
    outboxItems: [],
    apiKeys: [],
    webhookKeys: [],
    environments: [],
    members: [],
    invitations: [],
    implications: [],
    totalCount: 0,
    totalPages: 5,
    pageNumber: 1,
    pageSize: 20,
  })
}

vi.mock("@authsystem/api/client", () => {
  const request = (...args: unknown[]) => {
    mocks.apiCall(...args)
    return Promise.resolve({ data: emptyPayload(), error: undefined })
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

vi.mock("@authsystem/ui/crumbs", () => ({
  usePageBreadcrumb: () => undefined,
}))

vi.mock("@authsystem/ui/data-table/data-table", () => ({
  DataTable: (props: Record<string, unknown>) => {
    mocks.tables.set(String(props.tableId), props)
    return <div data-testid={`table-${String(props.tableId)}`} />
  },
}))

vi.mock("@authsystem/ui/common/search-input", () => ({
  SearchInput: ({
    value,
    onChange,
    placeholder,
  }: {
    value?: string
    onChange?: (value: string) => void
    placeholder?: string
  }) => (
    <input
      aria-label={placeholder ?? "search"}
      value={value ?? ""}
      onChange={(event) => onChange?.(event.target.value)}
    />
  ),
}))

vi.mock("@authsystem/ui/common/page-header", () => ({
  PageHeader: ({
    title,
    actions,
  }: {
    title?: ReactNode
    actions?: ReactNode
  }) => (
    <header>
      {title}
      {actions}
    </header>
  ),
}))

vi.mock("@authsystem/ui/common/detail-list", () => ({
  DetailList: () => <div data-testid="detail-list" />,
}))

vi.mock("@authsystem/ui/common/confirm-dialog", () => ({
  ConfirmDialog: () => null,
}))

vi.mock("@authsystem/ui/common/logo-avatar", () => ({
  LogoAvatar: () => <div data-testid="logo-avatar" />,
}))

vi.mock("@authsystem/ui/tabs", async () => {
  const React = await import("react")
  const ActiveTab = React.createContext("")
  return {
    Tabs: ({
      children,
      value,
      defaultValue,
    }: {
      children?: ReactNode
      value?: string
      defaultValue?: string
    }) => (
      <ActiveTab.Provider value={value ?? defaultValue ?? ""}>
        <div>{children}</div>
      </ActiveTab.Provider>
    ),
    TabsList: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
    TabsTrigger: ({ children }: { children?: ReactNode }) => (
      <button type="button">{children}</button>
    ),
    TabsContent: ({
      children,
      value,
    }: {
      children?: ReactNode
      value: string
    }) =>
      React.useContext(ActiveTab) === value ? <div>{children}</div> : null,
    // The notification section strip borrows these class strings to look like
    // a tab list while staying real navigation. Appearance is not what these
    // cases assert, so the stubs keep the mock free of the Radix primitives.
    tabsListVariants: () => "",
    tabsTriggerVariants: () => "",
  }
})

import { OrganizationDetailPage } from "@authsystem/account/pages/organizations/organization-detail-page"
import { OrganizationsPage } from "@authsystem/account/pages/organizations/organizations-page"
import { ApplicationDetailPage } from "./applications/application-detail-page"
import { ApplicationsPage } from "./applications/applications-page"
import { NotificationLayoutsPage } from "./notifications/notification-layouts-page"
import { NotificationTemplatesPage } from "./notifications/notification-templates-page"
import { OrganizationsAdminPage } from "./organizations/organizations-admin-page"
import { PermissionDetailPage } from "./permissions/permission-detail-page"
import { PermissionsPage } from "./permissions/permissions-page"
import { RoleDetailPage } from "./roles/role-detail-page"
import { RolesPage } from "./roles/roles-page"
import { UserDetailPage } from "./users/user-detail-page"
import { UsersPage } from "./users/users-page"

import { NotificationPolicyPage } from "./notifications/notification-policy-page"

/**
 * Every destination a record row offers is a real link.
 *
 * The pages are rendered with DataTable mocked, so each table's `columns` are
 * captured as data; each cell is then rendered with a fixture row and searched
 * for an anchor. That gives one list of every (table, column, href) this console
 * produces, compared against the list below - so a cell that quietly goes back
 * to `<button onClick={navigate}>` fails here, and a new record table with no
 * link fails here too.
 */

/** One row that satisfies every DTO shape these tables read. */
const ROW = {
  id: "11111111-1111-1111-1111-111111111111",
  userId: "22222222-2222-2222-2222-222222222222",
  roleId: "33333333-3333-3333-3333-333333333333",
  permissionId: "44444444-4444-4444-4444-444444444444",
  applicationId: "55555555-5555-5555-5555-555555555555",
  organizationId: "66666666-6666-6666-6666-666666666666",
  name: "Row name",
  fullName: "Row person",
  displayName: "Row person",
  applicationName: "Row app",
  typeName: "Row type",
  typeCode: "row.type",
  code: "row:code",
  version: "1.0.0",
  email: "row@example.test",
  firstName: "Row",
  lastName: "Person",
  channel: "Email",
  isDeleted: false,
  isActive: true,
}

/**
 * A detail page mounts only the open tab's table, so each tab is opened in turn
 * through the URL parameter that owns it. Without this the matrix would see one
 * table per detail page and quietly miss the rest.
 */
async function linksFor(element: ReactNode, tabs: readonly string[] = [""]) {
  mocks.tables.clear()
  for (const tab of tabs) {
    const client = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const entry = tab ? `/?tab=${tab}` : "/"
    const view = render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[entry]}>{element}</MemoryRouter>
      </QueryClientProvider>
    )
    await waitFor(() => expect(mocks.tables.size).toBeGreaterThan(0))
    view.unmount()
  }

  const found: string[] = []
  for (const [tableId, props] of mocks.tables) {
    const columns = props.columns as Array<{
      id?: string
      accessorKey?: string
      cell?: (context: unknown) => ReactNode
    }>
    for (const column of columns ?? []) {
      if (typeof column.cell !== "function") continue
      const columnId = column.id ?? column.accessorKey ?? "?"
      const container = document.createElement("div")
      const cellClient = new QueryClient({
        defaultOptions: { queries: { retry: false } },
      })
      const { unmount } = render(
        <QueryClientProvider client={cellClient}>
          <MemoryRouter initialEntries={["/"]}>
            {column.cell({ row: { original: ROW, getValue: () => undefined } })}
          </MemoryRouter>
        </QueryClientProvider>,
        { container: document.body.appendChild(container) }
      )
      for (const anchor of within(container).queryAllByRole("link")) {
        found.push(`${tableId}.${columnId} -> ${anchor.getAttribute("href")}`)
      }
      unmount()
      container.remove()
    }
  }
  return found.sort()
}

describe("record destinations are links", () => {
  afterEach(cleanup)

  it.each([
    ["users list", () => <UsersPage />, [""]],
    ["applications list", () => <ApplicationsPage />, [""]],
    ["roles list", () => <RolesPage />, [""]],
    ["permissions list", () => <PermissionsPage />, [""]],
    ["organizations list (platform)", () => <OrganizationsAdminPage />, [""]],
    ["organizations list (self-service)", () => <OrganizationsPage />, [""]],
    ["notification templates list", () => <NotificationTemplatesPage />, [""]],
    ["notification layouts list", () => <NotificationLayoutsPage />, [""]],
    ["policy revisions list", () => <NotificationPolicyPage />, [""]],
    [
      "user detail",
      () => <UserDetailPage />,
      ["organizations", "applications", "roles", "permissions", "audit"],
    ],
    ["role detail", () => <RoleDetailPage />, ["users", "applications"]],
    [
      "application detail",
      () => <ApplicationDetailPage />,
      ["users", "organizations", "roles", "permissions"],
    ],
    [
      "permission detail",
      () => <PermissionDetailPage />,
      ["users", "implications"],
    ],
    [
      "organization detail",
      () => (
        <OrganizationDetailPage
          userHref={(id?: string) => (id ? `/users/${id}` : undefined)}
          applicationHref={(id?: string) =>
            id ? `/applications/${id}` : undefined
          }
        />
      ),
      ["members", "invitations", "applications"],
    ],
  ])("%s", async (_name, page, tabs) => {
    expect(await linksFor(page(), tabs)).toMatchSnapshot()
  })
  // The row menu repeats the destination for people who look for commands
  // there. Radix closes the menu on select, which is exactly why this is worth
  // pinning: without asChild the close would swallow the click and the item
  // would go nowhere.
  it("offers the record as a link in the row action menu", async () => {
    const user = userEvent.setup()
    mocks.tables.clear()
    const client = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const { unmount } = render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={["/"]}>
          <UsersPage />
        </MemoryRouter>
      </QueryClientProvider>
    )
    await waitFor(() => expect(mocks.tables.size).toBeGreaterThan(0))

    const columns = mocks.tables.get("users")?.columns as Array<{
      id?: string
      cell?: (context: unknown) => ReactNode
    }>
    const actions = columns.find((column) => column.id === "actions")
    unmount()

    render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={["/"]}>
          {actions?.cell?.({ row: { original: ROW } })}
        </MemoryRouter>
      </QueryClientProvider>
    )

    await user.click(screen.getByRole("button", { name: /actions/i }))
    const view = await screen.findByRole("menuitem", { name: "View" })
    expect(view).toHaveAttribute("href", `/users/${ROW.id}`)
  })
})
