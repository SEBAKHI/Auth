import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { en } from "@authsystem/i18n/locales/en"

import { UserRolesDialog } from "./user-roles-dialog"

/**
 * OI-225 (2) and (3), on «Manage roles»:
 * - a role scoped to a deleted application names it and says it is deleted,
 *   instead of a bare role name that reads like a platform-wide assignment;
 * - the roles the user holds through organizations are listed below, read-only,
 *   each linking to the organization where it is removed, never with a remove
 *   control and never in the removable list;
 * - an older API without the endpoint answers 404, and the group is simply absent.
 */

const userId = "11111111-1111-1111-1111-111111111111"
const orgId = "55555555-5555-5555-5555-555555555555"
const appId = "44444444-4444-4444-4444-444444444444"

let organizationRoles: { data?: unknown; error?: unknown; response?: Response }

const ownRoles = [
  {
    roleId: "r-agent",
    roleName: "Agent",
    applicationId: appId,
    applicationName: "CRM",
    applicationIsDeleted: true,
    canRemove: true,
  },
  {
    roleId: "r-auditor",
    roleName: "Auditor",
    applicationId: null,
    canRemove: true,
  },
]

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (path: string) => {
      if (path === "/api/v1/Users/{id}/roles")
        return Promise.resolve({ data: ownRoles })
      if (path === "/api/v1/Users/{id}/organization-roles")
        return Promise.resolve(organizationRoles)
      return Promise.resolve({ data: [] })
    },
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

function renderDialog() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <UserRolesDialog
          open
          onOpenChange={vi.fn()}
          user={{ id: userId, email: "member@example.com" }}
        />
      </MemoryRouter>
    </QueryClientProvider>
  )
  return client
}

function chipOf(text: string) {
  return screen.getByText(text).closest<HTMLElement>('[data-slot="badge"]')
}

describe("the roles dialog's sources", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en")
    organizationRoles = {
      data: [
        {
          organizationId: orgId,
          organizationName: "Organization A",
          applicationId: appId,
          applicationName: "CRM",
          applicationIsDeleted: false,
          roleId: "r-editor",
          roleName: "Editor",
          expiresAt: null,
        },
      ],
    }
  })

  /** Deliberate break: label the chip with applicationName alone again. */
  it("names a deleted application on the chip and says it is deleted", async () => {
    renderDialog()

    const label = `Agent (${en.users.deletedApplication.replace("{{name}}", "CRM")})`
    expect(await screen.findByText(label)).toBeInTheDocument()
    expect(chipOf("Auditor")).not.toBeNull()
  })

  /** Deliberate break: add the organization rows to the removable list. */
  it("lists the roles held through organizations read-only, linking to the organization", async () => {
    renderDialog()

    const group = (
      await screen.findByText(en.users.organizationRoles.title)
    ).closest<HTMLElement>('[data-slot="field"]')
    expect(group).not.toBeNull()
    expect(within(group!).getByText("Editor (CRM)")).toBeInTheDocument()
    expect(
      within(group!).getByRole("link", { name: "Organization A" })
    ).toHaveAttribute("href", `/organizations/${orgId}`)
    expect(within(group!).queryByRole("button")).not.toBeInTheDocument()

    // The removable list is the user's own two, each with its remove control.
    const removable = screen.getAllByRole("button", { name: en.common.remove })
    expect(removable).toHaveLength(2)
    expect(chipOf("Editor (CRM)")).toBeNull()
  })

  it("shows no group, and no error, when an older API has no such endpoint", async () => {
    organizationRoles = {
      error: { status: 404, code: "Http.NotFound" },
      response: new Response(null, { status: 404 }),
    }
    const client = renderDialog()

    expect(await screen.findByText("Auditor")).toBeInTheDocument()
    // Absence proves nothing until the read has actually failed: wait for it.
    await waitFor(() =>
      expect(
        client.getQueryState(["users", userId, "organization-roles"])?.status
      ).toBe("error")
    )
    expect(
      screen.queryByText(en.users.organizationRoles.title)
    ).not.toBeInTheDocument()
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
    expect(
      screen.getAllByRole("button", { name: en.common.remove })
    ).toHaveLength(2)
  })
})
