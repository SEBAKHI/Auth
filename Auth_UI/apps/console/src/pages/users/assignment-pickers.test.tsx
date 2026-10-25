import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type { ReactNode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"
import { en } from "@authsystem/i18n/locales/en"

import { UserPermissionsDialog } from "./user-permissions-dialog"
import { UserRolesDialog } from "./user-roles-dialog"

/**
 * OI-223: the user page's pickers offer only what the server would let this
 * administrator grant (`canGrant` on each catalogue entry; absent, an older API,
 * keeps it), and an application's role or permission is picked at the scope the
 * server writes it at, its application. A platform-wide grant of an
 * application's permission (written before the server scoped it) used to hide
 * the grant that works. Deliberate break: ignore `canGrant` in a picker.
 */

const { get, post } = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: (...args: unknown[]) => post(...args),
    DELETE: vi.fn(),
  },
}))

const userId = "11111111-1111-1111-1111-111111111111"
const crmId = "44444444-4444-4444-4444-444444444444"
const user = { id: userId, email: "member@example.test" } as Schemas["UserDto"]

const roleCatalogue = [
  { id: "r-super", name: "Super Administrator", applicationId: null, canGrant: false },
  { id: "r-users", name: "User Manager", applicationId: null, canGrant: true },
  { id: "r-support", name: "Support", applicationId: null },
  {
    id: "r-agent",
    name: "Agent",
    applicationId: crmId,
    applicationName: "CRM",
    canGrant: true,
  },
]

const permissionCatalogue = [
  { id: "p-wildcard", code: "*", applicationId: null, canGrant: false },
  { id: "p-users", code: "users:read", applicationId: null, canGrant: true },
  {
    id: "p-leads",
    code: "crm:leads:read",
    applicationId: crmId,
    applicationName: "CRM",
    canGrant: true,
  },
  {
    id: "p-deals",
    code: "crm:deals:read",
    applicationId: crmId,
    applicationName: "CRM",
    canGrant: true,
  },
]

function respond(routes: Record<string, unknown[]>) {
  get.mockImplementation((path: string) =>
    Promise.resolve({ data: routes[path] ?? [] })
  )
}

function renderWithQueries(node: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(<QueryClientProvider client={client}>{node}</QueryClientProvider>)
}

async function openPicker() {
  const api = userEvent.setup()
  await api.click(await screen.findByRole("combobox"))
  await screen.findAllByRole("option")
  return api
}

function offered() {
  return screen.getAllByRole("option").map((option) => option.textContent)
}

beforeEach(() => {
  get.mockReset()
  post.mockReset().mockResolvedValue({})
})

describe("the user's role picker", () => {
  it("offers only the roles this administrator may assign", async () => {
    respond({ "/api/v1/Roles": roleCatalogue })
    renderWithQueries(<UserRolesDialog open onOpenChange={vi.fn()} user={user} />)

    await openPicker()

    const labels = offered()
    expect(labels.length).toBeGreaterThan(0)
    expect(labels.some((label) => label?.includes("Super Administrator"))).toBe(false)
    expect(labels.some((label) => label?.includes("User Manager"))).toBe(true)
    expect(labels.some((label) => label?.includes("Support"))).toBe(true)
  })

  it("offers an application's role while it is held only without a scope", async () => {
    respond({
      "/api/v1/Roles": roleCatalogue,
      // A row written before the server scoped application roles.
      "/api/v1/Users/{id}/roles": [
        { id: "ur-1", roleId: "r-agent", roleName: "Agent", applicationId: null },
      ],
    })
    renderWithQueries(<UserRolesDialog open onOpenChange={vi.fn()} user={user} />)

    await openPicker()

    expect(offered().some((label) => label?.includes("Agent (CRM)"))).toBe(true)
  })
})

describe("the user's permission picker", () => {
  it("offers only the permissions this administrator may grant", async () => {
    respond({ "/api/v1/Permissions": permissionCatalogue })
    renderWithQueries(
      <UserPermissionsDialog open onOpenChange={vi.fn()} user={user} />
    )

    await openPicker()

    const labels = offered()
    expect(labels.length).toBeGreaterThan(0)
    expect(labels.some((label) => label?.startsWith("*"))).toBe(false)
    expect(labels.some((label) => label?.includes("users:read"))).toBe(true)
  })

  it("is not hidden by a platform-wide grant of an application's permission, and hides the scoped one", async () => {
    respond({
      "/api/v1/Permissions": permissionCatalogue,
      "/api/v1/Users/{id}/permissions": [
        // Legacy: granted without a scope, so it never reached the application.
        {
          id: "up-1",
          permissionId: "p-leads",
          permissionCode: "crm:leads:read",
          applicationId: null,
        },
        // Granted inside its application: the one that works.
        {
          id: "up-2",
          permissionId: "p-deals",
          permissionCode: "crm:deals:read",
          applicationId: crmId,
          applicationName: "CRM",
        },
      ],
    })
    renderWithQueries(
      <UserPermissionsDialog open onOpenChange={vi.fn()} user={user} />
    )

    await openPicker()

    const labels = offered()
    expect(labels.some((label) => label?.includes("crm:leads:read (CRM)"))).toBe(true)
    expect(labels.some((label) => label?.includes("crm:deals:read"))).toBe(false)
  })

  it("stages an application's permission named by its application, and grants it", async () => {
    respond({ "/api/v1/Permissions": permissionCatalogue })
    renderWithQueries(
      <UserPermissionsDialog open onOpenChange={vi.fn()} user={user} />
    )

    const api = await openPicker()
    await api.click(
      screen.getAllByRole("option").find((option) =>
        option.textContent?.includes("crm:leads:read (CRM)")
      )!
    )
    await api.click(screen.getByRole("button", { name: en.users.grantPermission }))

    const staged = await screen.findAllByText("crm:leads:read (CRM)")
    expect(
      staged.some((node) => node.closest('[data-slot="badge"]') !== null)
    ).toBe(true)

    await api.click(
      screen.getByRole("button", {
        name: en.common.saveWithCount.replace("{{count}}", "1"),
      })
    )
    await api.click(await screen.findByRole("button", { name: en.common.save }))
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0][1]).toMatchObject({
      body: { permissionId: "p-leads" },
    })
  })
})
