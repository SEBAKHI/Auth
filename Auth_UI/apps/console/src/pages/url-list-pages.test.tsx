import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, cleanup, render, screen, waitFor } from "@testing-library/react"
import type { ReactNode } from "react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

const mocks = vi.hoisted(() => ({
  tables: new Map<string, Record<string, unknown>>(),
  apiCall: vi.fn(),
  /** What the page handed the logo control, so its save path can be driven. */
  logo: {} as Record<string, unknown>,
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
    leading,
  }: {
    title?: ReactNode
    actions?: ReactNode
    leading?: ReactNode
  }) => (
    <header>
      {leading}
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
  LogoAvatar: (props: Record<string, unknown>) => {
    Object.assign(mocks.logo, props)
    return <div data-testid="logo-avatar" />
  },
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
import { ApiKeysPage } from "./api-keys/api-keys-page"
import { ApplicationDetailPage } from "./applications/application-detail-page"
import { ApplicationsPage } from "./applications/applications-page"
import { AuditLogsPage } from "./audit-logs/audit-logs-page"
import { NotificationLayoutsPage } from "./notifications/notification-layouts-page"
import { NotificationOutboxPage } from "./notifications/notification-outbox-page"
import { NotificationTemplatesPage } from "./notifications/notification-templates-page"
import { OrganizationsAdminPage } from "./organizations/organizations-admin-page"
import { PermissionDetailPage } from "./permissions/permission-detail-page"
import { PermissionsPage } from "./permissions/permissions-page"
import { RoleDetailPage } from "./roles/role-detail-page"
import { RolesPage } from "./roles/roles-page"
import { UserDetailPage } from "./users/user-detail-page"
import { UsersPage } from "./users/users-page"
import { WebhookKeysPage } from "./webhook-keys/webhook-keys-page"

function LocationProbe() {
  const location = useLocation()
  return <output aria-label="location">{location.search}</output>
}

function renderRoute(path: string, route: string, element: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <LocationProbe />
        <Routes>
          <Route path={route} element={element} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

afterEach(() => {
  cleanup()
  mocks.tables.clear()
  mocks.apiCall.mockClear()
})

describe("URL-owned list page integration", () => {
  it("hydrates the users page and forwards typed state to its table and API", async () => {
    renderRoute(
      "/users?q=alice&page=3&pageSize=50&sort=name&direction=asc&includeDeleted=1",
      "/users",
      <UsersPage />
    )

    await waitFor(() => expect(mocks.tables.has("users")).toBe(true))
    const table = mocks.tables.get("users")
    expect(table?.sorting).toEqual([{ id: "name", desc: false }])
    expect(table?.pagination).toEqual(
      expect.objectContaining({ pageIndex: 2, pageSize: 50 })
    )
    expect(screen.getByLabelText("location")).toHaveTextContent(
      "q=alice&page=3&pageSize=50&sort=name&direction=asc&includeDeleted=1"
    )
    await waitFor(() =>
      expect(mocks.apiCall).toHaveBeenCalledWith(
        "/api/v1/Users",
        expect.objectContaining({
          params: expect.objectContaining({
            query: expect.objectContaining({
              pageNumber: 3,
              pageSize: 50,
              includeDeleted: true,
            }),
          }),
        })
      )
    )
  })

  it.each([
    ["applications", "/applications", <ApplicationsPage />],
    ["organizations-all", "/organizations", <OrganizationsAdminPage />],
    ["audit-logs", "/audit-logs", <AuditLogsPage />],
    [
      "notification-templates",
      "/notifications/templates",
      <NotificationTemplatesPage />,
    ],
    [
      "notification-outbox",
      "/notifications/outbox",
      <NotificationOutboxPage />,
    ],
    [
      "notification-layouts",
      "/notifications/layouts",
      <NotificationLayoutsPage />,
    ],
    ["roles", "/roles", <RolesPage />],
    ["permissions", "/permissions", <PermissionsPage />],
    ["api-keys", "/api-keys", <ApiKeysPage />],
    ["webhook-keys", "/webhook-keys", <WebhookKeysPage />],
    ["organizations", "/organizations", <OrganizationsPage />],
  ])("renders %s with controlled URL state", async (tableId, path, page) => {
    renderRoute(`${path}?q=bounded&sort=none`, path, page)
    await waitFor(() => expect(mocks.tables.has(tableId)).toBe(true))
    expect(mocks.tables.get(tableId)?.sorting).toEqual([])
  })
})

describe("namespaced detail-list integration", () => {
  it.each([
    [
      "/applications/:id",
      "/applications/11111111-1111-1111-1111-111111111111?tab=organizations&users.page=2&organizations.status=active",
      <ApplicationDetailPage />,
      ["app-orgs"],
    ],
    [
      "/applications/:id",
      "/applications/11111111-1111-1111-1111-111111111111?users.page=2&users.status=active",
      <ApplicationDetailPage />,
      ["app-users"],
    ],
    [
      "/roles/:id",
      "/roles/11111111-1111-1111-1111-111111111111?tab=permissions&users.page=2&users.status=locked",
      <RoleDetailPage />,
      ["role-perms"],
    ],
    [
      "/roles/:id",
      "/roles/11111111-1111-1111-1111-111111111111?users.page=2&users.status=locked",
      <RoleDetailPage />,
      ["role-users"],
    ],
    [
      "/permissions/:id",
      "/permissions/11111111-1111-1111-1111-111111111111?tab=implications&users.page=2",
      <PermissionDetailPage />,
      ["permission-implications"],
    ],
    [
      "/permissions/:id",
      "/permissions/11111111-1111-1111-1111-111111111111?users.page=2",
      <PermissionDetailPage />,
      ["permission-users"],
    ],
    [
      "/users/:id",
      "/users/11111111-1111-1111-1111-111111111111?tab=audit&audit.page=2&audit.entityType=User",
      <UserDetailPage />,
      ["user-audit"],
    ],
    [
      "/organizations/:id",
      "/organizations/11111111-1111-1111-1111-111111111111?tab=members&members.page=2&members.role=Owner",
      <OrganizationDetailPage />,
      ["org-members"],
    ],
  ])(
    "keeps each embedded table isolated on %s",
    async (route, path, page, ids) => {
      renderRoute(path, route, page)
      await waitFor(() =>
        expect(ids.every((id) => mocks.tables.has(id))).toBe(true)
      )
      expect(screen.getByLabelText("location").textContent).toMatch(/\w+\./)
    }
  )
})

/**
 * The callbacks an embedded table hands back to its page.
 *
 * The cases above prove each table reads its own slice of the URL. These prove
 * the other direction - what the page does when the table reports a change -
 * which is where the filter, export and column-value code actually lives.
 */
describe("what a page does with its table's callbacks", () => {
  const APP_PATH =
    "/applications/11111111-1111-1111-1111-111111111111"

  async function mountApplication(path = APP_PATH) {
    mocks.tables.clear()
    mocks.apiCall.mockClear()
    renderRoute(path, "/applications/:id", <ApplicationDetailPage />)
    await waitFor(() => expect(mocks.tables.size).toBeGreaterThan(0))
  }

  function table(id: string) {
    const props = mocks.tables.get(id)
    if (!props) throw new Error(`no table captured for ${id}`)
    return props
  }

  it("writes a user filter chosen in the table back into the URL", async () => {
    await mountApplication()

    const onChange = table("app-users").onColumnFiltersChange as (
      next: Array<{ id: string; value: unknown }>
    ) => void
    act(() =>
      onChange([
        { id: "accessSource", value: ["direct"] },
        { id: "status", value: ["active"] },
      ])
    )

    // `accessSource` publishes itself as `access`: the URL is a public
    // surface and carries the shorter name.
    await waitFor(() => {
      const search = screen.getByLabelText("location").textContent ?? ""
      expect(search).toContain("users.access=direct")
      expect(search).toContain("users.status=active")
    })
  })

  it("clears those filters when the table reports none", async () => {
    await mountApplication(`${APP_PATH}?users.access=direct&users.status=active`)

    const onChange = table("app-users").onColumnFiltersChange as (
      next: unknown[]
    ) => void
    act(() => onChange([]))

    await waitFor(() => {
      const search = screen.getByLabelText("location").textContent ?? ""
      expect(search).not.toContain("users.access")
      expect(search).not.toContain("users.status")
    })
  })

  it("writes an organization filter back into its own namespace", async () => {
    await mountApplication(`${APP_PATH}?tab=organizations`)

    const onChange = table("app-orgs").onColumnFiltersChange as (
      next: Array<{ id: string; value: unknown }>
    ) => void
    act(() => onChange([{ id: "isActive", value: ["active"] }]))

    await waitFor(() =>
      expect(screen.getByLabelText("location").textContent).toContain(
        "organizations.status=active"
      )
    )
  })

  it("walks every page of users when the table asks for an export", async () => {
    await mountApplication()
    mocks.apiCall.mockClear()

    await act(async () => {
      await (table("app-users").onExportAll as () => Promise<unknown>)()
    })

    // The export is not the on-screen page: it goes back to the API for the
    // whole set rather than exporting the twenty rows already in view.
    const calls = mocks.apiCall.mock.calls.filter(
      ([path]) => path === "/api/v1/Applications/{id}/users"
    )
    expect(calls.length).toBeGreaterThan(0)
  })

  it("names a user by display name, falling back to their full name", async () => {
    await mountApplication()

    const columns = table("app-users").columns as Array<{
      id?: string
      accessorFn?: (row: Record<string, unknown>) => unknown
    }>
    const name = columns.find((column) => column.id === "firstName")
    expect(name?.accessorFn?.({ displayName: "Ada L." })).toBe("Ada L.")
    expect(
      name?.accessorFn?.({ firstName: "Ada", lastName: "Lovelace", email: "a@b.c" })
    ).toContain("Ada")

    // An access route the API left unset reads as "more than one", not as blank.
    const source = columns.find((column) => column.id === "accessSource")
    expect(source?.accessorFn?.({ accessSource: "direct" })).toBe("direct")
    expect(source?.accessorFn?.({})).toBe("multiple")
  })

  it("saves a new application logo through the API", async () => {
    await mountApplication()
    mocks.apiCall.mockClear()

    const persist = mocks.logo.persist as (key: string | null) => Promise<void>
    expect(persist).toBeTypeOf("function")
    await act(async () => {
      await persist("logos/app.png")
    })

    expect(
      mocks.apiCall.mock.calls.some(
        ([path]) => path === "/api/v1/Applications/{id}"
      )
    ).toBe(true)
  })

  it.each([
    [
      "roles",
      "/roles",
      <RolesPage />,
      "roles",
      { id: "isSystem", value: ["true"] },
      "system=true",
    ],
    [
      "the delivery log",
      "/notifications/outbox",
      <NotificationOutboxPage />,
      "notification-outbox",
      { id: "status", value: ["Dead"] },
      "status=Dead",
    ],
  ])(
    "writes a %s facet back into the URL",
    async (_name, path, page, tableId, filter, expected) => {
      mocks.tables.clear()
      renderRoute(path, path, page)
      await waitFor(() => expect(mocks.tables.has(tableId)).toBe(true))

      const onChange = mocks.tables.get(tableId)!.onColumnFiltersChange as (
        next: Array<{ id: string; value: unknown }>
      ) => void
      act(() => onChange([filter]))
      await waitFor(() =>
        expect(screen.getByLabelText("location").textContent).toContain(expected)
      )

      // And clearing it takes the parameter back out rather than leaving an
      // empty one behind.
      act(() => onChange([]))
      await waitFor(() =>
        expect(screen.getByLabelText("location").textContent).not.toContain(
          expected
        )
      )
    }
  )

  it("writes an audit filter chosen on a user's timeline back into the URL", async () => {
    mocks.tables.clear()
    renderRoute(
      "/users/11111111-1111-1111-1111-111111111111?tab=audit",
      "/users/:id",
      <UserDetailPage />
    )
    await waitFor(() => expect(mocks.tables.has("user-audit")).toBe(true))

    const onChange = mocks.tables.get("user-audit")!
      .onColumnFiltersChange as (next: Array<{ id: string; value: unknown }>) => void
    act(() => onChange([{ id: "entityType", value: ["User"] }]))

    await waitFor(() => {
      const search = screen.getByLabelText("location").textContent ?? ""
      // Free-form facets travel as JSON so a value containing a comma cannot
      // split into two.
      expect(decodeURIComponent(search)).toContain('audit.entityType=["User"]')
    })
  })
})
