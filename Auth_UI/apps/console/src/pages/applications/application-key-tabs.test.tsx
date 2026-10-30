import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import type { ReactNode } from "react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The key sections of OI-264.
 *
 * The API-key and webhook-key tables now appear twice each: on the main-menu
 * page across every application, and on an application's own tab. Two copies
 * of one table drift the moment a fix reaches only one of them, so these cases
 * render the REAL surfaces and compare what each hands its table, rather than
 * trusting that both happen to call the same factory.
 */

const mocks = vi.hoisted(() => ({
  tables: new Map<string, Record<string, unknown>>(),
  apiCall: vi.fn(),
  granted: new Set<string>(),
}))

vi.mock("@authsystem/api/client", () => {
  const request = (...args: unknown[]) => {
    mocks.apiCall(...args)
    const path = String(args[0])
    const data = path.endsWith("Keys")
      ? []
      : {
          id: APP_ID,
          name: "Customer portal",
          code: "CRM",
          accessMode: "Restricted",
          isActive: true,
          redirectUris: [],
        }
    return Promise.resolve({ data, error: undefined })
  }
  return {
    api: { GET: request, POST: request, PUT: request, DELETE: request },
  }
})

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    hasPermission: (permission: string) => mocks.granted.has(permission),
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

vi.mock("@authsystem/ui/common/application-select", () => ({
  ApplicationSelect: () => <div data-testid="application-picker" />,
}))

vi.mock("@authsystem/ui/common/detail-list", () => ({
  DetailList: () => <div data-testid="detail-list" />,
}))

vi.mock("@authsystem/ui/common/themed-logo", () => ({
  ThemedLogo: () => <div data-testid="themed-logo" />,
}))

vi.mock("@authsystem/ui/tabs", async () => {
  const React = await import("react")
  const ActiveTab = React.createContext("")
  return {
    Tabs: ({ children, value }: { children?: ReactNode; value?: string }) => (
      <ActiveTab.Provider value={value ?? ""}>
        <div>{children}</div>
      </ActiveTab.Provider>
    ),
    TabsList: ({ children }: { children?: ReactNode }) => <div>{children}</div>,
    TabsTrigger: ({
      children,
      value,
    }: {
      children?: ReactNode
      value: string
    }) => (
      <button type="button" data-tab={value}>
        {children}
      </button>
    ),
    TabsContent: ({
      children,
      value,
    }: {
      children?: ReactNode
      value: string
    }) =>
      React.useContext(ActiveTab) === value ? (
        <div data-testid={`panel-${value}`}>{children}</div>
      ) : null,
    tabsListVariants: () => "",
    tabsTriggerVariants: () => "",
  }
})

import { ApiKeysPage } from "../api-keys/api-keys-page"
import { WebhookKeysPage } from "../webhook-keys/webhook-keys-page"
import { ApplicationDetailPage } from "./application-detail-page"
import {
  ApplicationApiKeysTab,
  ApplicationWebhookKeysTab,
} from "./application-key-tabs"

const APP_ID = "11111111-1111-1111-1111-111111111111"

const ALL_KEY_PERMISSIONS = [
  "apikeys:read",
  "apikeys:create",
  "apikeys:revoke",
  "apikeys:rotate",
  "webhookkeys:read",
  "webhookkeys:create",
  "webhookkeys:revoke",
  "webhookkeys:rotate",
]

function LocationProbe() {
  const location = useLocation()
  return <output aria-label="location">{location.search}</output>
}

let client: QueryClient

function renderAt(path: string, route: string, element: ReactNode) {
  client = new QueryClient({
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

type CapturedColumn = {
  id?: string
  accessorKey?: string
  enableSorting?: boolean
  enableHiding?: boolean
  meta?: { defaultHidden?: boolean; covers?: string[]; label?: string }
}

async function tableOf(tableId: string) {
  await waitFor(() => expect(mocks.tables.has(tableId)).toBe(true))
  return mocks.tables.get(tableId) as { columns: CapturedColumn[] }
}

const idsOf = (columns: CapturedColumn[]) =>
  columns.map((column) => column.id ?? column.accessorKey)

/** What a reader sees of each column, apart from whether it starts hidden. */
const shapeOf = (columns: CapturedColumn[]) =>
  columns.map((column) => ({
    id: column.id ?? column.accessorKey,
    label: column.meta?.label,
    covers: column.meta?.covers,
    enableSorting: column.enableSorting,
    enableHiding: column.enableHiding,
  }))

const columnOf = (columns: CapturedColumn[], id: string) => {
  const found = columns.find((column) => column.id === id)
  if (!found) throw new Error(`no column "${id}"`)
  return found
}

beforeEach(() => {
  mocks.granted = new Set(ALL_KEY_PERMISSIONS)
})

afterEach(() => {
  cleanup()
  mocks.tables.clear()
  mocks.apiCall.mockClear()
})

const SURFACES = [
  {
    kind: "API keys",
    path: "/api/v1/ApiKeys",
    queryKey: "api-keys",
    global: { tableId: "api-keys", path: "/api-keys", page: <ApiKeysPage /> },
    tab: {
      tableId: "app-api-keys",
      element: <ApplicationApiKeysTab appId={APP_ID} />,
    },
    newKey: "New API key",
  },
  {
    kind: "webhook keys",
    path: "/api/v1/WebhookKeys",
    queryKey: "webhook-keys",
    global: {
      tableId: "webhook-keys",
      path: "/webhook-keys",
      page: <WebhookKeysPage />,
    },
    tab: {
      tableId: "app-webhook-keys",
      element: <ApplicationWebhookKeysTab appId={APP_ID} />,
    },
    newKey: "New webhook key",
  },
] as const

describe.each(SURFACES)("$kind: one table on two surfaces", (surface) => {
  it("gives the main-menu page and the application tab the same columns in the same order", async () => {
    renderAt(surface.global.path, surface.global.path, surface.global.page)
    const global = await tableOf(surface.global.tableId)
    cleanup()
    renderAt("/tab", "/tab", surface.tab.element)
    const tab = await tableOf(surface.tab.tableId)

    expect(idsOf(global.columns).length).toBeGreaterThan(0)
    expect(idsOf(tab.columns)).toEqual(idsOf(global.columns))
    expect(shapeOf(tab.columns)).toEqual(shapeOf(global.columns))
    // The row actions are part of the definition too: both surfaces offer them.
    expect(idsOf(global.columns)).toContain("actions")
  })

  it("files both surfaces under one query key per application, so a change on either refreshes both", async () => {
    renderAt("/tab", "/tab", surface.tab.element)
    await tableOf(surface.tab.tableId)
    await waitFor(() =>
      expect(
        client
          .getQueryCache()
          .find({
            queryKey: [surface.queryKey, { applicationId: APP_ID }],
            exact: true,
          })
      ).toBeDefined()
    )
    cleanup()
    renderAt(
      `${surface.global.path}?applicationId=${APP_ID}`,
      surface.global.path,
      surface.global.page
    )
    await tableOf(surface.global.tableId)
    await waitFor(() =>
      expect(
        client
          .getQueryCache()
          .find({
            queryKey: [surface.queryKey, { applicationId: APP_ID }],
            exact: true,
          })
      ).toBeDefined()
    )
  })

  it("shows the application column on the main-menu page and hides it on the tab", async () => {
    renderAt(surface.global.path, surface.global.path, surface.global.page)
    const global = await tableOf(surface.global.tableId)
    cleanup()
    renderAt("/tab", "/tab", surface.tab.element)
    const tab = await tableOf(surface.tab.tableId)

    const onPage = columnOf(global.columns, "applicationName")
    expect(onPage.meta?.defaultHidden).toBeUndefined()
    expect(onPage.meta?.covers).toEqual(["applicationId"])
    // The server's sort list has no application name: not sortable.
    expect(onPage.enableSorting).toBe(false)
    expect(columnOf(tab.columns, "applicationName").meta?.defaultHidden).toBe(
      true
    )
  })

  it("lists only the open application's keys on the tab, with no picker and an enabled create button", async () => {
    renderAt("/tab", "/tab", surface.tab.element)
    await tableOf(surface.tab.tableId)

    await waitFor(() =>
      expect(mocks.apiCall).toHaveBeenCalledWith(
        surface.path,
        expect.objectContaining({
          params: { query: { applicationId: APP_ID } },
        })
      )
    )
    expect(screen.queryByTestId("application-picker")).toBeNull()
    expect(screen.getByRole("button", { name: surface.newKey })).toBeEnabled()
  })

  it("keeps the picker on the main-menu page, where create waits for a chosen application", async () => {
    renderAt(surface.global.path, surface.global.path, surface.global.page)
    await tableOf(surface.global.tableId)

    expect(screen.getByTestId("application-picker")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: surface.newKey })).toBeDisabled()
    await waitFor(() =>
      expect(mocks.apiCall).toHaveBeenCalledWith(
        surface.path,
        expect.objectContaining({ params: { query: {} } })
      )
    )
  })
})

describe("the application page's key tabs", () => {
  const detailPath = (tab?: string) =>
    `/applications/${APP_ID}${tab ? `?tab=${tab}` : ""}`
  const renderDetail = (tab?: string) =>
    renderAt(detailPath(tab), "/applications/:id", <ApplicationDetailPage />)
  const tabs = () =>
    Array.from(document.querySelectorAll("[data-tab]")).map((node) =>
      node.getAttribute("data-tab")
    )

  it("offers identity keys, then API keys, then webhook keys, to a reader of both", async () => {
    renderDetail()
    await screen.findByText("Application identity keys")
    const all = tabs()
    expect(all.slice(all.indexOf("machine-keys"))).toEqual([
      "machine-keys",
      "api-keys",
      "webhook-keys",
    ])
  })

  it("offers the API-key tab only with apikeys:read, and the webhook tab only with webhookkeys:read", async () => {
    mocks.granted = new Set(["webhookkeys:read"])
    renderDetail()
    await screen.findByTestId("panel-users")
    expect(tabs()).not.toContain("api-keys")
    expect(tabs()).toContain("webhook-keys")
    cleanup()

    mocks.granted = new Set(["apikeys:read"])
    renderDetail()
    await screen.findByTestId("panel-users")
    expect(tabs()).toContain("api-keys")
    expect(tabs()).not.toContain("webhook-keys")
  })

  it.each([
    ["api-keys", "apikeys:read", "app-api-keys"],
    ["webhook-keys", "webhookkeys:read", "app-webhook-keys"],
  ])(
    "opens ?tab=%s for a reader with %s, and falls back to the default tab without it",
    async (tab, permission, tableId) => {
      mocks.granted = new Set([permission])
      renderDetail(tab)
      await tableOf(tableId)
      cleanup()
      mocks.tables.clear()

      mocks.granted = new Set()
      renderDetail(tab)
      await screen.findByTestId("panel-users")
      expect(screen.queryByTestId(`panel-${tab}`)).toBeNull()
      expect(mocks.tables.has(tableId)).toBe(false)
    }
  )

  it("keeps a key tab's list state under its own namespace, apart from the page's tab parameter", async () => {
    renderDetail("api-keys&apiKeys.q=billing&apiKeys.status=revoked")
    const table = (await tableOf("app-api-keys")) as unknown as {
      globalFilter: string
      columnFilters: unknown
    }
    expect(table.globalFilter).toBe("billing")
    expect(table.columnFilters).toEqual([{ id: "status", value: ["revoked"] }])
  })
})
