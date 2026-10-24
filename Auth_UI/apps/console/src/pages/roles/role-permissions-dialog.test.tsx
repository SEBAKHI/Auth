import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
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
