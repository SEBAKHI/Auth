import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { PERMISSIONS } from "@/lib/constants"

const apiCall = vi.fn()
const grantedPermissions = new Set<string>()

const user = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "operator@example.test",
  displayName: "Console Operator",
  firstName: "Console",
  lastName: "Operator",
  status: "Active",
  emailConfirmed: false,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: "2026-08-20T07:00:00Z",
}

vi.mock("@authsystem/api/client", () => {
  const request = (path: string, options?: unknown) => {
    apiCall(path, options)
    if (path === "/api/v1/Users/{id}") {
      return Promise.resolve({ data: user })
    }
    return Promise.resolve({ data: [] })
  }
  return {
    api: {
      GET: request,
      POST: request,
      PUT: request,
      PATCH: request,
      DELETE: request,
    },
  }
})

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    hasPermission: (permission: string) => grantedPermissions.has(permission),
  }),
}))

vi.mock("@authsystem/api/use-profile-image", () => ({
  useProfileImage: () => ({
    pending: false,
    onChange: vi.fn(),
    onRemove: vi.fn(),
  }),
}))

vi.mock("@authsystem/ui/crumbs", () => ({
  usePageBreadcrumb: () => undefined,
}))

vi.mock("@authsystem/ui/data-table/data-table", () => ({
  DataTable: () => <div data-testid="data-table" />,
}))

vi.mock("@authsystem/ui/common/detail-list", () => ({
  DetailList: () => <div data-testid="detail-list" />,
}))

vi.mock("@authsystem/ui/common/avatar-menu", () => ({
  AvatarMenu: () => <div data-testid="avatar-menu" />,
}))

vi.mock("@authsystem/ui/common/entity-avatar", () => ({
  EntityAvatar: () => <div data-testid="entity-avatar" />,
}))

vi.mock("./user-form-dialog", () => ({
  UserFormDialog: ({ open }: { open: boolean }) =>
    open ? <div role="dialog" aria-label="Edit user" /> : null,
}))

vi.mock("./user-roles-dialog", () => ({
  UserRolesDialog: ({ open }: { open: boolean }) =>
    open ? <div role="dialog" aria-label="Manage roles" /> : null,
}))

vi.mock("./user-permissions-dialog", () => ({
  UserPermissionsDialog: ({ open }: { open: boolean }) =>
    open ? <div role="dialog" aria-label="Manage permissions" /> : null,
}))

vi.mock("@authsystem/ui/common/verify-email-dialog", () => ({
  VerifyEmailDialog: ({ open }: { open: boolean }) =>
    open ? <div role="dialog" aria-label="Confirm email" /> : null,
}))

import { UserDetailPage } from "./user-detail-page"

function renderPage(permissions: string[]) {
  grantedPermissions.clear()
  permissions.forEach((permission) => grantedPermissions.add(permission))
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/users/${user.id}`]}>
        <Routes>
          <Route path="/users/:id" element={<UserDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

function desktopActions(container: HTMLElement) {
  const surface = container.querySelector(
    '[data-slot="page-action-surface-desktop"]'
  )
  if (!(surface instanceof HTMLElement)) throw new Error("Missing action surface")
  return within(surface)
}

describe("UserDetailPage action surface", () => {
  beforeEach(async () => {
    apiCall.mockClear()
    grantedPermissions.clear()
    await i18n.changeLanguage("en")
  })

  it.each([
    [PERMISSIONS.users.update, ["Edit"]],
    [PERMISSIONS.users.manageRoles, ["Manage roles"]],
    [PERMISSIONS.users.managePermissions, ["Manage permissions"]],
    [
      PERMISSIONS.users.manage,
      [
        "Send password reset email",
        "Resend confirmation email",
        "Lock",
        "Deactivate",
      ],
    ],
    [PERMISSIONS.users.delete, ["Delete"]],
  ])("exposes only actions allowed by %s", async (permission, expected) => {
    const { container } = renderPage([permission])
    await screen.findByText("Console Operator", { selector: "h1" })

    const labels = desktopActions(container)
      .getAllByRole("button")
      .map((button) => button.textContent)
    expect(labels).toEqual(expected)
  })

  it("connects visible edit, assignment, email, status, and danger actions", async () => {
    const { container } = renderPage([
      PERMISSIONS.users.update,
      PERMISSIONS.users.manageRoles,
      PERMISSIONS.users.managePermissions,
      PERMISSIONS.users.manage,
      PERMISSIONS.users.delete,
    ])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })
    const actions = desktopActions(container)

    await userEventApi.click(actions.getByRole("button", { name: "Edit" }))
    expect(screen.getByRole("dialog", { name: "Edit user" })).toBeVisible()
    await userEventApi.click(
      actions.getByRole("button", { name: "Manage roles" })
    )
    expect(screen.getByRole("dialog", { name: "Manage roles" })).toBeVisible()
    await userEventApi.click(
      actions.getByRole("button", { name: "Manage permissions" })
    )
    expect(
      screen.getByRole("dialog", { name: "Manage permissions" })
    ).toBeVisible()

    await userEventApi.click(
      actions.getByRole("button", { name: "Send password reset email" })
    )
    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Auth/forgot-password",
        expect.objectContaining({ body: { email: user.email } })
      )
    )
    await userEventApi.click(
      actions.getByRole("button", { name: "Deactivate" })
    )
    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/deactivate",
        expect.anything()
      )
    )
  })

  it("keeps destructive and confirm-required actions behind dialogs", async () => {
    const { container } = renderPage([
      PERMISSIONS.users.manage,
      PERMISSIONS.users.delete,
    ])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })
    const actions = desktopActions(container)

    await userEventApi.click(actions.getByRole("button", { name: "Lock" }))
    expect(
      screen.getByRole("alertdialog", { name: "Lock account" })
    ).toBeVisible()
    await userEventApi.click(screen.getByRole("button", { name: "Cancel" }))

    expect(actions.getByRole("button", { name: "Delete" })).toHaveAttribute(
      "data-variant",
      "destructive"
    )
    await userEventApi.click(actions.getByRole("button", { name: "Delete" }))
    expect(
      screen.getByRole("alertdialog", { name: "Delete user" })
    ).toBeVisible()
  })
})

/**
 * The actions that change a person's access, driven to the request they send.
 *
 * Which of these the page offers depends on the account's current state - you
 * cannot unlock an account that is not locked - so each case sets the state
 * first and then asserts on the call, not on the button.
 *
 * Restoring access (unlock, activate) acts on the click; taking it away asks
 * first. Both halves of that rule are pinned here.
 */
describe("acting on a user's access", () => {
  const originalStatus = user.status

  afterEach(() => {
    user.status = originalStatus
    user.emailConfirmed = false
  })

  it("unlocks a locked account straight from the action", async () => {
    user.status = "Locked"
    const { container } = renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await userEventApi.click(
      desktopActions(container).getByRole("button", { name: "Unlock" })
    )

    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/unlock",
        expect.anything()
      )
    )
  }, 15_000)

  it("activates an inactive account straight from the action", async () => {
    user.status = "Inactive"
    const { container } = renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await userEventApi.click(
      desktopActions(container).getByRole("button", { name: "Activate" })
    )

    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/activate",
        expect.anything()
      )
    )
  }, 15_000)

  it("locks only after the reason dialog is confirmed", async () => {
    const { container } = renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await userEventApi.click(
      desktopActions(container).getByRole("button", { name: "Lock" })
    )
    const dialog = await screen.findByRole("alertdialog", {
      name: "Lock account",
    })
    // Opening the dialog is not the action.
    expect(apiCall).not.toHaveBeenCalledWith(
      "/api/v1/Users/{id}/lock",
      expect.anything()
    )

    await userEventApi.click(
      within(dialog).getByRole("button", { name: "Lock" })
    )
    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/lock",
        expect.anything()
      )
    )
  }, 15_000)

  it("offers to resend confirmation while the address is unconfirmed", async () => {
    const { container } = renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await userEventApi.click(
      desktopActions(container).getByRole("button", {
        name: /resend|confirmation/i,
      })
    )

    expect(await screen.findByRole("dialog")).toBeInTheDocument()
  }, 15_000)
})
