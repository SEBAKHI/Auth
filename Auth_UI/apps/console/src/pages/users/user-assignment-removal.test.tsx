import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ReactNode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"
import { en } from "@authsystem/i18n/locales/en"

import {
  heldIds,
  parseScopedAssignmentKey,
  pickedAssignmentKey,
  scopedAssignmentKey,
} from "./scoped-assignment-key"
import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

/**
 * A role or a direct permission is held at a scope, and the same one may be held
 * platform-wide and inside an application. The dialogs keyed a chip by the id
 * alone and sent no scope on removal, so the API removed the platform
 * assignment (or nothing) whichever chip was clicked: "Changes saved", and the
 * application's role stayed. These drive the real AssignmentDialog with both
 * scopes present and read the DELETE that leaves.
 */

const { del, get } = vi.hoisted(() => ({ del: vi.fn(), get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: vi.fn(),
    DELETE: (...args: unknown[]) => del(...args),
  },
}))

const userId = "11111111-1111-1111-1111-111111111111"
const roleId = "22222222-2222-2222-2222-222222222222"
const permissionId = "33333333-3333-3333-3333-333333333333"
const shopId = "44444444-4444-4444-4444-444444444444"

const user = { id: userId, email: "member@example.test" } as Schemas["UserDto"]

const roleRows = [
  { id: "r-1", roleId, roleName: "Store manager", applicationId: null },
  {
    id: "r-2",
    roleId,
    roleName: "Store manager",
    applicationId: shopId,
    applicationName: "SHOP",
  },
]

const permissionRows = [
  {
    id: "p-1",
    permissionId,
    permissionCode: "reports:read",
    applicationId: null,
  },
  {
    id: "p-2",
    permissionId,
    permissionCode: "reports:read",
    applicationId: shopId,
    applicationName: "SHOP",
  },
]

function renderWithQueries(node: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(<QueryClientProvider client={client}>{node}</QueryClientProvider>)
}

async function removeChipAndSave(label: string) {
  const api = userEvent.setup()
  const chip = (await screen.findByText(label)).closest<HTMLElement>(
    '[data-slot="badge"]'
  )
  expect(chip).not.toBeNull()
  await api.click(within(chip!).getByRole("button", { name: en.common.remove }))
  await api.click(
    screen.getByRole("button", {
      name: en.common.saveWithCount.replace("{{count}}", "1"),
    })
  )
  await api.click(await screen.findByRole("button", { name: en.common.save }))
  await waitFor(() => expect(del).toHaveBeenCalledTimes(1))
}

beforeEach(() => {
  del.mockReset().mockResolvedValue({})
  get.mockReset().mockImplementation((path: string) =>
    Promise.resolve({
      data:
        path === "/api/v1/Users/{id}/roles"
          ? roleRows
          : path === "/api/v1/Users/{id}/permissions"
            ? permissionRows
            : [],
    })
  )
})

describe("removing a user's role", () => {
  it("shows each scope as its own chip, named by its application", async () => {
    renderWithQueries(
      <UserRolesDialog open onOpenChange={vi.fn()} user={user} />
    )

    expect(await screen.findByText("Store manager (SHOP)")).toBeVisible()
    expect(screen.getByText("Store manager")).toBeVisible()
  })

  it("sends the application of an application-scoped assignment", async () => {
    renderWithQueries(
      <UserRolesDialog open onOpenChange={vi.fn()} user={user} />
    )

    await removeChipAndSave("Store manager (SHOP)")

    expect(del).toHaveBeenCalledWith("/api/v1/Users/{id}/roles/{roleId}", {
      params: {
        path: { id: userId, roleId },
        query: { applicationId: shopId },
      },
    })
  })

  it("sends no application for the platform-wide assignment", async () => {
    renderWithQueries(
      <UserRolesDialog open onOpenChange={vi.fn()} user={user} />
    )

    await removeChipAndSave("Store manager")

    expect(del).toHaveBeenCalledWith("/api/v1/Users/{id}/roles/{roleId}", {
      params: { path: { id: userId, roleId }, query: undefined },
    })
  })
})

describe("revoking a user's direct permission", () => {
  it("sends the application of an application-scoped grant", async () => {
    renderWithQueries(
      <UserPermissionsDialog open onOpenChange={vi.fn()} user={user} />
    )

    await removeChipAndSave("reports:read (SHOP)")

    expect(del).toHaveBeenCalledWith(
      "/api/v1/Users/{id}/permissions/{permissionId}",
      {
        params: {
          path: { id: userId, permissionId },
          query: { applicationId: shopId },
        },
      }
    )
  })

  it("sends no application for the platform-wide grant", async () => {
    renderWithQueries(
      <UserPermissionsDialog open onOpenChange={vi.fn()} user={user} />
    )

    await removeChipAndSave("reports:read")

    expect(del).toHaveBeenCalledWith(
      "/api/v1/Users/{id}/permissions/{permissionId}",
      { params: { path: { id: userId, permissionId }, query: undefined } }
    )
  })
})

describe("scoped assignment keys", () => {
  it("round-trips the id and the scope", () => {
    expect(
      parseScopedAssignmentKey(scopedAssignmentKey(roleId, shopId))
    ).toEqual({ id: roleId, applicationId: shopId })
    expect(parseScopedAssignmentKey(scopedAssignmentKey(roleId, null))).toEqual(
      {
        id: roleId,
        applicationId: undefined,
      }
    )
    expect(scopedAssignmentKey(roleId, shopId)).not.toBe(
      scopedAssignmentKey(roleId)
    )
  })

  it("offers an id held at any scope no more", () => {
    expect([...heldIds([scopedAssignmentKey(roleId, shopId)])]).toEqual([
      roleId,
    ])
  })

  it("re-picks the assignment staged for removal instead of adding beside it", () => {
    const staged = scopedAssignmentKey(roleId, shopId)

    expect(pickedAssignmentKey([staged], roleId)).toBe(staged)
    expect(pickedAssignmentKey([], roleId)).toBe(scopedAssignmentKey(roleId))
  })
})

/**
 * OI-224: each assignment and grant carries whether the API would accept its
 * removal from this administrator (`canRemove`). A refused one shows no ✕ —
 * hidden, not disabled — and the others keep theirs; an answer the response
 * does not carry (an older API) keeps the ✕. Deliberate break: map every chip
 * as removable again.
 */
describe("a removal the API would refuse", () => {
  function chip(label: string) {
    return screen
      .getAllByText(label)
      .map((node) => node.closest<HTMLElement>('[data-slot="badge"]'))
      .find((badge) => badge?.textContent === label)
  }

  function removeButton(label: string) {
    const badge = chip(label)
    expect(badge).toBeDefined()
    return within(badge!).queryByRole("button", { name: en.common.remove })
  }

  it.each([
    ["role", "/api/v1/Users/{id}/roles", "Store manager"],
    ["permission", "/api/v1/Users/{id}/permissions", "reports:read"],
  ])(
    "shows no remove control on a refused %s, and keeps it on the others",
    async (kind, path, platformLabel) => {
      const rows = kind === "role" ? roleRows : permissionRows
      get.mockImplementation((requested: string) =>
        Promise.resolve({
          data:
            requested === path
              ? rows.map((row) => ({
                  ...row,
                  canRemove: row.applicationId === null ? false : true,
                }))
              : [],
        })
      )
      renderWithQueries(
        kind === "role" ? (
          <UserRolesDialog open onOpenChange={vi.fn()} user={user} />
        ) : (
          <UserPermissionsDialog open onOpenChange={vi.fn()} user={user} />
        )
      )

      await screen.findByText(`${platformLabel} (SHOP)`)
      expect(removeButton(platformLabel)).toBeNull()
      expect(removeButton(`${platformLabel} (SHOP)`)).not.toBeNull()
    }
  )

  it("keeps the remove control when the response does not say", async () => {
    renderWithQueries(
      <UserRolesDialog open onOpenChange={vi.fn()} user={user} />
    )

    await screen.findByText("Store manager (SHOP)")
    expect(removeButton("Store manager")).not.toBeNull()
    expect(removeButton("Store manager (SHOP)")).not.toBeNull()
  })
})
