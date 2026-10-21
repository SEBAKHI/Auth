import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  cleanup,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { __resetDataTableStorage } from "@authsystem/ui/data-table/storage"
import { permissionMatches } from "@authsystem/auth/permission-matching"

/**
 * OI-197: the organization page shows each control only to a person whose
 * token lets its endpoint through; a button that can only answer 403 is a dead
 * end. Seeded roles: a plain member reads members and applications; an
 * organization administrator holds `org:members:*`, `org:apps:*` and
 * `org:permissions:*` but not `org:update`; the owner holds `org:*`.
 */

const ORG = "11111111-1111-1111-1111-111111111111"
const PLAIN_MEMBER = [`${ORG}:org:members:read`, `${ORG}:org:apps:read`]
const ORG_ADMIN = [
  `${ORG}:org:members:*`,
  `${ORG}:org:apps:*`,
  `${ORG}:org:permissions:*`,
]
const OWNER = [`${ORG}:org:*`]

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  refreshSessionNow: vi.fn(),
  token: null as string | null,
  permissions: [] as string[],
}))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: vi.fn(),
    PUT: vi.fn(),
    DELETE: vi.fn(),
  },
  refreshSessionNow: () => mocks.refreshSessionNow(),
}))

vi.mock("@authsystem/api/token-store", async (importOriginal) => ({
  ...(await importOriginal<object>()),
  getAccessToken: () => mocks.token,
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    status: "authenticated",
    hasPermission: (code: string) => permissionMatches(mocks.permissions, code),
  }),
}))

vi.mock("@authsystem/api/ui-preferences", () => ({
  fetchUiPreferences: vi.fn(async () => ({})),
  putUiPreference: vi.fn(async () => true),
  deleteUiPreference: vi.fn(async () => true),
}))

const { OrganizationDetailPage } = await import("./organization-detail-page")

function tokenWith(orgPerm: string[]) {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/=+$/, "")
  return `${encode({ alg: "none" })}.${encode({
    sub: "99999999-9999-9999-9999-999999999999",
    org_perm: orgPerm,
  })}.signature`
}

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function renderPage(search = "") {
  mocks.get.mockImplementation((requestPath: string) => {
    if (requestPath === "/api/v1/Organizations/{id}")
      return ok({
        id: ORG,
        name: "Acme Corporation",
        code: "acme",
        isActive: true,
        ownerId: "88888888-8888-8888-8888-888888888888",
      })
    if (requestPath === "/api/v1/Organizations/{id}/members")
      return ok({
        members: [
          {
            userId: "55555555-5555-5555-5555-555555555555",
            email: "colleague@example.test",
            roleName: "Organization Member",
          },
        ],
        totalCount: 1,
        totalPages: 1,
      })
    if (requestPath === "/api/v1/Organizations/{id}/applications")
      return ok([
        {
          applicationId: "33333333-3333-3333-3333-333333333333",
          applicationName: "CRM",
          isActive: true,
        },
      ])
    return ok([])
  })
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/organizations/${ORG}${search}`]}>
        <Routes>
          <Route
            path="/organizations/:id"
            element={<OrganizationDetailPage />}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

/** The page's header has loaded once its title is on screen. */
async function headerLoaded() {
  await screen.findByRole("heading", { name: "Acme Corporation" })
}

/**
 * The menu of the table row holding `rowText`, opened: its item names, in
 * order. Null when the row has no menu.
 */
async function rowMenuItems(rowText: string): Promise<string[] | null> {
  const row = await waitFor(() => {
    const match = screen
      .getAllByRole("row")
      .find((candidate) => candidate.textContent?.includes(rowText))
    expect(match, rowText).toBeDefined()
    return match!
  })
  const trigger = within(row).queryByRole("button", { name: "Actions" })
  if (!trigger) return null
  await userEvent.click(trigger)
  const menu = await screen.findByRole("menu")
  return within(menu)
    .getAllByRole("menuitem")
    .map((item) => item.textContent ?? "")
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  mocks.permissions = []
  mocks.token = null
  mocks.refreshSessionNow.mockResolvedValue(true)
})

afterEach(() => {
  cleanup()
  __resetDataTableStorage()
  vi.clearAllMocks()
})

describe("the members tab", () => {
  it("has no actions column for a plain member", async () => {
    mocks.token = tokenWith(PLAIN_MEMBER)

    renderPage()

    expect(await rowMenuItems("colleague@example.test")).toBeNull()
    expect(
      screen.queryByRole("columnheader", { name: "Actions" }),
      "no empty actions column"
    ).toBeNull()
  })

  it("offers an organization administrator every member action", async () => {
    mocks.token = tokenWith(ORG_ADMIN)

    renderPage()

    expect(
      await screen.findByRole("columnheader", { name: "Actions" }),
      "the header the plain member's case looks for, when it exists"
    ).toBeInTheDocument()
    expect(await rowMenuItems("colleague@example.test")).toEqual([
      "Change role",
      "Manage app roles",
      "Remove member",
    ])
  })

  it("offers Change role and Remove, and not the application roles, with org:members:manage alone", async () => {
    mocks.token = tokenWith([`${ORG}:org:members:manage`])

    renderPage()

    expect(await rowMenuItems("colleague@example.test")).toEqual([
      "Change role",
      "Remove member",
    ])
  })

  it("offers only the application roles with org:permissions:read alone", async () => {
    mocks.token = tokenWith([`${ORG}:org:permissions:read`])

    renderPage()

    expect(await rowMenuItems("colleague@example.test")).toEqual([
      "Manage app roles",
    ])
  })
})

describe("the header", () => {
  it("has no Edit and a logo that only displays, for an organization administrator", async () => {
    mocks.token = tokenWith(ORG_ADMIN)

    renderPage()
    await headerLoaded()

    expect(screen.queryByRole("button", { name: "Edit" })).toBeNull()
    expect(screen.queryByRole("button", { name: "Avatar" })).toBeNull()
  })

  it("offers Edit and the logo's menu to the owner", async () => {
    mocks.token = tokenWith(OWNER)

    renderPage()
    await headerLoaded()

    expect(screen.getByRole("button", { name: "Edit" })).toBeVisible()
    expect(screen.getByRole("button", { name: "Avatar" })).toBeVisible()
  })

  it("offers Edit to a platform holder of org:update without organizations:read, as the API does", async () => {
    mocks.permissions = ["org:update"]
    mocks.token = tokenWith([])

    renderPage()
    await headerLoaded()

    expect(screen.getByRole("button", { name: "Edit" })).toBeVisible()
  })
})

describe("the applications tab", () => {
  it("has no Enable application and no row menu for a plain member", async () => {
    mocks.token = tokenWith(PLAIN_MEMBER)

    renderPage("?tab=applications")

    expect(await rowMenuItems("CRM")).toBeNull()
    expect(
      screen.queryByRole("button", { name: "Enable application" })
    ).toBeNull()
    expect(
      screen.queryByRole("columnheader", { name: "Actions" }),
      "no empty actions column"
    ).toBeNull()
  })

  it("offers Enable application and the row menu with org:apps:manage", async () => {
    mocks.token = tokenWith([`${ORG}:org:apps:read`, `${ORG}:org:apps:manage`])

    renderPage("?tab=applications")

    expect(
      await screen.findByRole("button", { name: "Enable application" })
    ).toBeVisible()
    expect(
      screen.getByRole("columnheader", { name: "Actions" })
    ).toBeInTheDocument()
    expect(await rowMenuItems("CRM")).toEqual(["Edit subscription", "Remove"])
  })
})
