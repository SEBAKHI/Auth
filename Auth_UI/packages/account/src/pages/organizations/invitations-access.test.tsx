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
    mocks.permissions = [ORGANIZATION_GATES.inviteMembers.platformScope]
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

/**
 * The endpoints behind each gate the page applies, by verb and route template
 * as the controller declares them. A gate fronts every endpoint its controls
 * call; the invitations gate fronts the list, as the tab is the list.
 */
const GATE_ENDPOINTS: Record<keyof typeof ORGANIZATION_GATES, string[]> = {
  inviteMembers: ['HttpGet("{id:guid}/invitations")'],
  manageMembers: [
    'HttpPut("{orgId:guid}/members/{userId:guid}/role")',
    'HttpDelete("{orgId:guid}/members/{userId:guid}")',
  ],
  readMemberAppRoles: ['HttpGet("{orgId:guid}/members/{userId:guid}/roles")'],
  manageMemberAppRoles: [
    'HttpGet("{orgId:guid}/applications/{applicationId:guid}/assignable-roles")',
    'HttpPost("{orgId:guid}/members/{userId:guid}/roles")',
    'HttpDelete("{orgId:guid}/members/{userId:guid}/roles/{roleId:guid}")',
  ],
  updateOrganization: ['HttpPut("{id:guid}")'],
  manageApplications: [
    'HttpGet("{id:guid}/applications/available")',
    'HttpPost("{id:guid}/applications")',
    'HttpPut("{id:guid}/applications/{applicationId:guid}")',
    'HttpDelete("{id:guid}/applications/{applicationId:guid}")',
  ],
}

/** The string constants of one nested class of `PermissionCodes`. */
function constantsOf(source: string, className: string): Map<string, string> {
  const start = source.indexOf(`public static class ${className}\n`)
  const startCrlf = source.indexOf(`public static class ${className}\r\n`)
  const from = start >= 0 ? start : startCrlf
  expect(from, `class ${className}`).toBeGreaterThanOrEqual(0)
  const end = source.indexOf("public static class", from + 1)
  const body = source.slice(from, end < 0 ? undefined : end)
  return new Map(
    [...body.matchAll(/const string (\w+) = "([^"]+)"/g)].map((match) => [
      match[1]!,
      match[2]!,
    ])
  )
}

describe("the gate codes", () => {
  it("are each endpoint's own: its attribute's code, and its platform scope exactly where it has one", () => {
    const root = repositoryRoot()
    const codes = readFileSync(
      path.join(root, "Auth/Auth.Domain/Constants/PermissionCodes.cs"),
      "utf8"
    )
    const org = constantsOf(codes, "Org")
    const organizations = constantsOf(codes, "Organizations")
    const controller = readFileSync(
      path.join(
        root,
        "Auth/Auth_API/Modules/OrganizationManagement/Controllers/OrganizationsController.cs"
      ),
      "utf8"
    )

    let checked = 0
    for (const [name, endpoints] of Object.entries(GATE_ENDPOINTS)) {
      const gate: { code: string; platformScope?: string } =
        ORGANIZATION_GATES[name as keyof typeof ORGANIZATION_GATES]
      for (const endpoint of endpoints) {
        const at = controller.indexOf(`[${endpoint}]`)
        expect(at, endpoint).toBeGreaterThanOrEqual(0)
        const next = controller.indexOf("[Http", at + 1)
        const action = controller.slice(at, next < 0 ? undefined : next)

        // Exactly one gate in the action, so a neighbour's cannot stand in.
        expect(
          action.match(/\[RequirePermission\(/g),
          `${endpoint}: one gate`
        ).toHaveLength(1)
        const attribute = action.match(
          /\[RequirePermission\(PermissionCodes\.Org\.(\w+)\)\]/
        )
        expect(attribute, `${endpoint}: its gate`).not.toBeNull()
        expect(org.get(attribute![1]!), `${name} → ${endpoint}`).toBe(
          gate.code
        )

        const scope = action.match(
          /PlatformScope\s*=\s*HasPermissionClaim\(PermissionCodes\.Organizations\.(\w+)\)/
        )
        // A scope set in any other form must not read as "no scope".
        expect(
          /PlatformScope\s*=/.test(action),
          `${endpoint}: a PlatformScope this test can read`
        ).toBe(scope !== null)
        expect(
          scope ? organizations.get(scope[1]!) : undefined,
          `${name} → ${endpoint}: the platform scope`
        ).toBe(gate.platformScope)
        checked += 1
      }
    }
    expect(checked).toBe(Object.values(GATE_ENDPOINTS).flat().length)
    expect(checked).toBeGreaterThan(0)
  })
})
