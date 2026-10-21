import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, within } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * OI-197: a member's application roles are read with `org:permissions:read`;
 * the assignable roles, the add and the remove need `org:permissions:manage`.
 * Without it the dialog lists the assignments and offers nothing that the API
 * would refuse, and asks for neither list behind the picker.
 */

const ORG = "11111111-1111-1111-1111-111111111111"
const MEMBER = "55555555-5555-5555-5555-555555555555"

const mocks = vi.hoisted(() => ({ get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

const { MemberAppRolesDialog } = await import("./member-app-roles-dialog")

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function renderDialog(canManage: boolean) {
  mocks.get.mockImplementation((requestPath: string) => {
    if (requestPath === "/api/v1/Organizations/{orgId}/members/{userId}/roles")
      return ok([
        {
          roleId: "44444444-4444-4444-4444-444444444444",
          roleName: "Sales representative",
          applicationName: "CRM",
        },
      ])
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
      <MemberAppRolesDialog
        open
        onOpenChange={() => {}}
        orgId={ORG}
        member={{ userId: MEMBER, email: "colleague@example.test" }}
        canManage={canManage}
      />
    </QueryClientProvider>
  )
}

function readPaths() {
  return mocks.get.mock.calls.map(([requestPath]) => requestPath as string)
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe("with org:permissions:read only", () => {
  it("lists the assignments, offers no change, and asks for neither list behind the picker", async () => {
    renderDialog(false)

    const dialog = await screen.findByRole("dialog", {
      name: "Manage app roles",
    })
    expect(
      await within(dialog).findByText("CRM · Sales representative")
    ).toBeVisible()
    expect(within(dialog).queryByRole("combobox")).toBeNull()
    expect(
      within(dialog).queryByRole("button", { name: "Assign role" })
    ).toBeNull()
    expect(within(dialog).queryByRole("button", { name: "Remove" })).toBeNull()
    expect(within(dialog).queryByRole("button", { name: /^Save/ })).toBeNull()
    // The footer's Close, beside the dialog's own corner one.
    expect(
      within(dialog).getAllByRole("button", { name: "Close" })
    ).toHaveLength(2)

    expect(readPaths()).toEqual([
      "/api/v1/Organizations/{orgId}/members/{userId}/roles",
    ])
  })
})

describe("with org:permissions:manage", () => {
  it("keeps the editor: the picker, a remove control on each chip, and Save", async () => {
    renderDialog(true)

    const dialog = await screen.findByRole("dialog", {
      name: "Manage app roles",
    })
    expect(
      await within(dialog).findByText("CRM · Sales representative")
    ).toBeVisible()
    expect(within(dialog).getAllByRole("combobox")).toHaveLength(2)
    expect(
      within(dialog).getByRole("button", { name: "Assign role" })
    ).toBeVisible()
    expect(within(dialog).getByRole("button", { name: "Remove" })).toBeVisible()
    expect(within(dialog).getByRole("button", { name: /^Save/ })).toBeVisible()
    expect(readPaths()).toContain("/api/v1/Organizations/{id}/applications")
  })
})
