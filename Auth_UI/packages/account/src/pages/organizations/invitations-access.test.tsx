import { existsSync, readFileSync } from "node:fs"
import path from "node:path"

import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { __resetDataTableStorage } from "@authsystem/ui/data-table/storage"
import { permissionMatches } from "@authsystem/auth/permission-matching"

import { ORGANIZATION_GATES } from "../../lib/organization-permissions"

/**
 * D-91-1 (b2): an organization's invitations are the inviters'. The page shows
 * the Invitations tab, and with it Invite and Resend, only to a person whose
 * token grants `org:members:invite` in that organization (its `org_perm`
 * claims), or to a platform administrator the API widens; a link to the tab
 * lands anyone else on the members. A token with no claim at all for the
 * organization is renewed once, as the API checks the live membership then.
 */

const ORG = "11111111-1111-1111-1111-111111111111"
const OTHER_ORG = "22222222-2222-2222-2222-222222222222"

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
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

function renderPage(search = "") {
  mocks.get.mockImplementation((requestPath: string) => {
    if (requestPath === "/api/v1/Organizations/{id}")
      return ok({ id: ORG, name: "Acme Corporation", code: "acme", isActive: true })
    if (requestPath === "/api/v1/Organizations/{id}/members")
      return ok({ members: [], totalCount: 0, totalPages: 0 })
    if (requestPath === "/api/v1/Organizations/{id}/invitations")
      return ok({ invitations: [], totalCount: 0, totalPages: 0 })
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

function invitationReads() {
  return mocks.get.mock.calls.filter(
    ([requestPath]) => requestPath === "/api/v1/Organizations/{id}/invitations"
  )
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

describe("a member who may only read members", () => {
  beforeEach(() => {
    mocks.token = tokenWith([
      `${ORG}:org:members:read`,
      `${ORG}:org:apps:read`,
      `${OTHER_ORG}:org:members:invite`,
    ])
  })

  it("has no Invitations tab and no Invite button, and the list is never asked for", async () => {
    renderPage()

    expect(await screen.findByRole("tab", { name: "Members" })).toBeVisible()
    expect(screen.queryByRole("tab", { name: "Invitations" })).toBeNull()
    expect(screen.queryByRole("button", { name: "Invite member" })).toBeNull()
    expect(invitationReads()).toHaveLength(0)
    expect(
      mocks.refreshSessionNow,
      "the token speaks for this organization, so nothing is renewed"
    ).not.toHaveBeenCalled()
  })

  it("lands on the members when a link names the Invitations tab", async () => {
    renderPage("?tab=invitations&invite=1&role=org-member")

    const members = await screen.findByRole("tab", { name: "Members" })
    expect(members).toHaveAttribute("aria-selected", "true")
    expect(screen.queryByRole("dialog")).toBeNull()
    expect(invitationReads()).toHaveLength(0)
  })
})

describe("an inviter in this organization", () => {
  it.each(["org:members:invite", "org:members:*", "org:*"])(
    "sees the tab and its Invite button with %s",
    async (code) => {
      mocks.token = tokenWith([`${ORG}:${code}`])

      renderPage("?tab=invitations")

      expect(
        await screen.findByRole("tab", { name: "Invitations" })
      ).toHaveAttribute("aria-selected", "true")
      expect(
        await screen.findByRole("button", { name: "Invite member" })
      ).toBeVisible()
      await waitFor(() => expect(invitationReads().length).toBeGreaterThan(0))
    }
  )
})

describe("a platform administrator", () => {
  it("sees the tab through the platform claims the API widens on", async () => {
    mocks.permissions = ["*"]
    mocks.token = tokenWith([])

    renderPage()

    expect(await screen.findByRole("tab", { name: "Invitations" })).toBeVisible()
    expect(mocks.refreshSessionNow).not.toHaveBeenCalled()
  })

  it("does not see it with the widening permission alone, which the API's gate refuses", async () => {
    mocks.permissions = [ORGANIZATION_GATES.platformScope]
    mocks.token = tokenWith([])

    renderPage()

    expect(await screen.findByRole("tab", { name: "Members" })).toBeVisible()
    expect(screen.queryByRole("tab", { name: "Invitations" })).toBeNull()
  })
})

describe("a token minted before the membership", () => {
  it("is renewed once, and the tab appears when the new token grants it", async () => {
    mocks.token = tokenWith([])
    mocks.refreshSessionNow.mockImplementation(async () => {
      mocks.token = tokenWith([`${ORG}:org:*`])
      return true
    })

    renderPage()

    expect(await screen.findByRole("tab", { name: "Invitations" })).toBeVisible()
    expect(mocks.refreshSessionNow).toHaveBeenCalledTimes(1)
  })

  it("is renewed only once when the new token still says nothing", async () => {
    mocks.token = tokenWith([])

    renderPage()

    await waitFor(() => expect(mocks.refreshSessionNow).toHaveBeenCalled())
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(mocks.refreshSessionNow).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole("tab", { name: "Invitations" })).toBeNull()
  })
})

/** Walk up to the repository root, so the path holds wherever vitest is started. */
function repositoryRoot(): string {
  let directory = process.cwd()
  for (let depth = 0; depth < 10; depth += 1) {
    if (existsSync(path.join(directory, "Auth", "Auth.sln"))) return directory
    const parent = path.dirname(directory)
    if (parent === directory) break
    directory = parent
  }
  throw new Error("Auth/Auth.sln not found above " + process.cwd())
}

describe("the gate codes", () => {
  it("are the API's: the invitations list's attribute and the widening permission", () => {
    const root = repositoryRoot()
    const codes = readFileSync(
      path.join(root, "Auth/Auth.Domain/Constants/PermissionCodes.cs"),
      "utf8"
    )
    expect(codes).toContain(
      `MembersInvite = "${ORGANIZATION_GATES.inviteMembers}"`
    )
    expect(codes).toContain(`Read = "${ORGANIZATION_GATES.platformScope}"`)

    const controller = readFileSync(
      path.join(
        root,
        "Auth/Auth_API/Modules/OrganizationManagement/Controllers/OrganizationsController.cs"
      ),
      "utf8"
    )
    const list = controller.match(
      /\[HttpGet\("\{id:guid\}\/invitations"\)\]\s*\[RequirePermission\(PermissionCodes\.Org\.(\w+)\)\]/
    )
    expect(list, "the list action's gate").not.toBeNull()
    expect(list![1]).toBe("MembersInvite")
  })
})
