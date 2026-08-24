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

type UserEventApi = ReturnType<typeof userEvent.setup>

/**
 * The surface puts exactly one action out front - the primary - and everything
 * else behind the named menu. These helpers ask for an action by NAME and let
 * the surface decide where it lives, so a change of which action is promoted
 * does not rewrite every case below.
 */
function promotedActions() {
  return [
    ...document.querySelectorAll<HTMLElement>(
      '[data-slot="page-action-surface-action"]'
    ),
  ]
}

async function openActionMenu(api: UserEventApi) {
  await api.click(screen.getByRole("button", { name: "Actions" }))
  return within(await screen.findByRole("menu"))
}

/** Every action the page offers: the primary first, then the menu, in order. */
async function actionLabels(api: UserEventApi) {
  const labels: (string | null)[] = []

  labels.push(...promotedActions().map((button) => button.textContent))

  if (screen.queryByRole("button", { name: "Actions" })) {
    const menu = await openActionMenu(api)
    labels.push(
      ...menu.getAllByRole("menuitem").map((item) => item.textContent)
    )
    await api.keyboard("{Escape}")
  }

  return labels
}

async function clickAction(api: UserEventApi, name: string | RegExp) {
  const promoted = screen.queryByRole("button", { name })
  if (promoted && promotedActions().includes(promoted)) {
    await api.click(promoted)
    return
  }

  const menu = await openActionMenu(api)
  await api.click(menu.getByRole("menuitem", { name }))
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
    renderPage([permission])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    expect(await actionLabels(userEventApi)).toEqual(expected)
  })

  it("connects visible edit, assignment, email, status, and danger actions", async () => {
    renderPage([
      PERMISSIONS.users.update,
      PERMISSIONS.users.manageRoles,
      PERMISSIONS.users.managePermissions,
      PERMISSIONS.users.manage,
      PERMISSIONS.users.delete,
    ])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await clickAction(userEventApi, "Edit")
    expect(screen.getByRole("dialog", { name: "Edit user" })).toBeVisible()
    await clickAction(userEventApi, "Manage roles")
    expect(screen.getByRole("dialog", { name: "Manage roles" })).toBeVisible()
    await clickAction(userEventApi, "Manage permissions")
    expect(
      screen.getByRole("dialog", { name: "Manage permissions" })
    ).toBeVisible()

    await clickAction(userEventApi, "Send password reset email")
    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Auth/forgot-password",
        expect.objectContaining({ body: { email: user.email } })
      )
    )
    await clickAction(userEventApi, "Deactivate")
    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/deactivate",
        expect.anything()
      )
    )
  })

  it("keeps destructive and confirm-required actions behind dialogs", async () => {
    renderPage([PERMISSIONS.users.manage, PERMISSIONS.users.delete])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await clickAction(userEventApi, "Lock")
    expect(
      screen.getByRole("alertdialog", { name: "Lock account" })
    ).toBeVisible()
    await userEventApi.click(screen.getByRole("button", { name: "Cancel" }))

    const menu = await openActionMenu(userEventApi)
    expect(menu.getByRole("menuitem", { name: "Delete" })).toHaveAttribute(
      "data-variant",
      "destructive"
    )
    await userEventApi.click(menu.getByRole("menuitem", { name: "Delete" }))
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
    renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await clickAction(userEventApi, "Unlock")

    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/unlock",
        expect.anything()
      )
    )
  }, 15_000)

  it("activates an inactive account straight from the action", async () => {
    user.status = "Inactive"
    renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await clickAction(userEventApi, "Activate")

    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith(
        "/api/v1/Users/{id}/activate",
        expect.anything()
      )
    )
  }, 15_000)

  it("locks only after the reason dialog is confirmed", async () => {
    renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await clickAction(userEventApi, "Lock")
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
    renderPage([PERMISSIONS.users.manage])
    const userEventApi = userEvent.setup()
    await screen.findByText("Console Operator", { selector: "h1" })

    await clickAction(userEventApi, /resend|confirmation/i)

    expect(await screen.findByRole("dialog")).toBeInTheDocument()
  }, 15_000)
})
