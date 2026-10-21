import { renderHook, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { permissionMatches } from "@authsystem/auth/permission-matching"

import { ORGANIZATION_GATES } from "../../lib/organization-permissions"

/**
 * OI-197: the organization page reads the caller's organization permissions
 * once, for all its controls, and answers each the way its endpoint does. A
 * mutation has no platform scope, so the platform code alone opens it; the
 * invitations list widens only with `organizations:read` as well. Requiring the
 * scope for a mutation would hide a control the API lets a platform
 * administrator use; dropping it for the list would show one the API refuses.
 */

const ORG = "11111111-1111-1111-1111-111111111111"
const OTHER_ORG = "22222222-2222-2222-2222-222222222222"

const mocks = vi.hoisted(() => ({
  refreshSessionNow: vi.fn(),
  token: null as string | null,
  permissions: [] as string[],
}))

vi.mock("@authsystem/api/client", () => ({
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

const { useOrganizationPermissions } =
  await import("@authsystem/auth/organization-permissions")

function tokenWith(orgPerm: string[]) {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/=+$/, "")
  return `${encode({ alg: "none" })}.${encode({
    sub: "99999999-9999-9999-9999-999999999999",
    org_perm: orgPerm,
  })}.signature`
}

function gates(organizationId: string | undefined = ORG) {
  return renderHook(() =>
    useOrganizationPermissions(organizationId, ORGANIZATION_GATES)
  )
}

const NONE = {
  inviteMembers: false,
  manageMembers: false,
  readMemberAppRoles: false,
  manageMemberAppRoles: false,
  updateOrganization: false,
  manageApplications: false,
}

beforeEach(() => {
  mocks.permissions = []
  mocks.token = tokenWith([])
  mocks.refreshSessionNow.mockResolvedValue(true)
})

afterEach(() => {
  vi.clearAllMocks()
})

describe("the platform branch follows each endpoint", () => {
  it("opens every mutation with the platform code alone, and keeps the invitations list shut without the scope", () => {
    mocks.permissions = ["org:*"]

    const { result } = gates()

    expect(result.current).toEqual({
      inviteMembers: false,
      manageMembers: true,
      readMemberAppRoles: true,
      manageMemberAppRoles: true,
      updateOrganization: true,
      manageApplications: true,
    })
  })

  it("opens the invitations list with the code and the scope together", () => {
    mocks.permissions = ["org:members:invite", "organizations:read"]

    expect(gates().result.current).toEqual({ ...NONE, inviteMembers: true })
  })

  it("does not open it with the code alone, nor with the scope alone", () => {
    mocks.permissions = ["org:members:invite"]
    expect(gates().result.current.inviteMembers).toBe(false)

    mocks.permissions = ["organizations:read"]
    expect(gates().result.current.inviteMembers).toBe(false)
  })

  it("opens one mutation with its own platform code, and nothing else", () => {
    mocks.permissions = ["org:update"]

    expect(gates().result.current).toEqual({
      ...NONE,
      updateOrganization: true,
    })
  })
})

describe("the organization's own claims", () => {
  it("grant an exact code", () => {
    mocks.token = tokenWith([`${ORG}:org:members:manage`])

    expect(gates().result.current).toEqual({ ...NONE, manageMembers: true })
  })

  it("grant every code under a wildcard", () => {
    mocks.token = tokenWith([`${ORG}:org:members:*`, `${ORG}:org:apps:read`])

    expect(gates().result.current).toEqual({
      ...NONE,
      inviteMembers: true,
      manageMembers: true,
    })
  })

  it("grant everything to the owner", () => {
    mocks.token = tokenWith([`${ORG}:org:*`])

    expect(gates().result.current).toEqual({
      inviteMembers: true,
      manageMembers: true,
      readMemberAppRoles: true,
      manageMemberAppRoles: true,
      updateOrganization: true,
      manageApplications: true,
    })
    expect(mocks.refreshSessionNow).not.toHaveBeenCalled()
  })

  it("never come from another organization's claims", () => {
    mocks.token = tokenWith([`${OTHER_ORG}:org:*`])

    expect(gates().result.current).toEqual(NONE)
  })
})

describe("a token that says nothing for the organization", () => {
  it("is renewed once per mount, whatever the number of gates and renders", async () => {
    expect(Object.keys(ORGANIZATION_GATES).length).toBeGreaterThan(1)

    const { rerender } = gates()
    await waitFor(() => expect(mocks.refreshSessionNow).toHaveBeenCalled())
    rerender()
    rerender()
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(mocks.refreshSessionNow).toHaveBeenCalledTimes(1)
  })

  it("opens the controls the renewed token grants", async () => {
    mocks.refreshSessionNow.mockImplementation(async () => {
      mocks.token = tokenWith([`${ORG}:org:apps:*`])
      return true
    })

    const { result } = gates()

    await waitFor(() =>
      expect(result.current).toEqual({ ...NONE, manageApplications: true })
    )
    expect(mocks.refreshSessionNow).toHaveBeenCalledTimes(1)
  })

  it("is not renewed when the platform claim already opens every gate", async () => {
    mocks.permissions = ["*"]

    const { result } = gates()

    expect(Object.values(result.current).every(Boolean)).toBe(true)
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(mocks.refreshSessionNow).not.toHaveBeenCalled()
  })

  it("is renewed when the platform claim opens only some of them", async () => {
    mocks.permissions = ["org:*"]

    gates()

    await waitFor(() =>
      expect(mocks.refreshSessionNow).toHaveBeenCalledTimes(1)
    )
  })
})
