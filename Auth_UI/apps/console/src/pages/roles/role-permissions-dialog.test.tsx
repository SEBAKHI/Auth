import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ReactNode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { en } from "@authsystem/i18n/locales/en"

import { RolePermissionsDialog } from "./role-permissions-dialog"

/**
 * The role read says which of the role's codes the viewer may remove (OI-226 T4):
 * a code the viewer does not hold, or the last full administrator's "*", is refused
 * by the API, so its chip offers no remove control. An older API sends no field,
 * and every chip stays removable as before.
 */

const { get } = vi.hoisted(() => ({ get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

const roleId = "22222222-2222-2222-2222-222222222222"

const catalogue = [
  { id: "p-wildcard", code: "*", applicationId: null, isActive: true },
  { id: "p-users", code: "users:*", applicationId: null, isActive: true },
]

function renderDialog(removableCodes?: string[] | null) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const node: ReactNode = (
    <RolePermissionsDialog
      open
      onOpenChange={vi.fn()}
      roleId={roleId}
      roleName="Super Administrator"
      applicationId={null}
      grantedCodes={["*", "users:*"]}
      removableCodes={removableCodes}
    />
  )
  render(<QueryClientProvider client={client}>{node}</QueryClientProvider>)
}

async function removeControlOf(code: string) {
  const chip = (await screen.findByText(code)).closest<HTMLElement>(
    '[data-slot="badge"]'
  )
  expect(chip).not.toBeNull()
  return within(chip!).queryByRole("button", { name: en.common.remove })
}

beforeEach(() => {
  get.mockReset().mockResolvedValue({ data: catalogue })
})

describe("removing a permission from a role", () => {
  it("offers no remove control on a code the viewer may not remove", async () => {
    renderDialog(["users:*"])

    expect(await removeControlOf("*")).toBeNull()
    expect(await removeControlOf("users:*")).not.toBeNull()
  })

  it("offers none at all when the viewer may remove nothing", async () => {
    renderDialog([])

    expect(await removeControlOf("*")).toBeNull()
    expect(await removeControlOf("users:*")).toBeNull()
  })

  it.each([undefined, null])(
    "offers every remove control when the API sends no field (%s)",
    async (removableCodes) => {
      renderDialog(removableCodes)

      expect(await removeControlOf("*")).not.toBeNull()
      expect(await removeControlOf("users:*")).not.toBeNull()
    }
  )
})

/**
 * OI-223: the picker offers only what this administrator may add. The catalogue's
 * canGrant is the user grant's rule, which also asks applications:update for a
 * permission an application owns; adding one to a role asks only for the code
 * itself, so canGrant hides platform codes only. Deliberate break: ignore canGrant.
 */
describe("adding a permission to a role", () => {
  const crmId = "44444444-4444-4444-4444-444444444444"

  async function offeredFor(applicationId: string | null, rows: unknown[]) {
    get.mockResolvedValue({ data: rows })
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    })
    render(
      <QueryClientProvider client={client}>
        <RolePermissionsDialog
          open
          onOpenChange={vi.fn()}
          roleId={roleId}
          roleName="Agent"
          applicationId={applicationId}
          grantedCodes={[]}
        />
      </QueryClientProvider>
    )
    await userEvent.setup().click(await screen.findByRole("combobox"))
    const options = await screen.findAllByRole("option")
    return options.map((option) => option.textContent ?? "")
  }

  it("hides a platform code the viewer may not grant, and keeps one the API does not judge", async () => {
    const offered = await offeredFor(null, [
      { id: "p-wildcard", code: "*", applicationId: null, isActive: true, canGrant: false },
      { id: "p-users", code: "users:*", applicationId: null, isActive: true, canGrant: true },
      { id: "p-audit", code: "audit:read", applicationId: null, isActive: true },
    ])

    expect(offered.length).toBeGreaterThan(0)
    expect(offered.some((label) => label.startsWith("*"))).toBe(false)
    expect(offered.some((label) => label.startsWith("users:*"))).toBe(true)
    expect(offered.some((label) => label.startsWith("audit:read"))).toBe(true)
  })

  it("keeps an application's code that the user grant's rule refuses", async () => {
    const offered = await offeredFor(crmId, [
      { id: "p-leads", code: "crm:leads:read", applicationId: crmId, isActive: true, canGrant: false },
    ])

    expect(offered.some((label) => label.startsWith("crm:leads:read"))).toBe(true)
  })
})
