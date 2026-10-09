import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { PERMISSIONS } from "@/lib/constants"

/**
 * S08 PR B: an administrator resets another account's two-factor from the user
 * page, beside the two-factor row. Offered only with the permission, never on
 * one's own account, whatever the account flag says (the flag is one of two
 * sources of truth; the server decides), and only through a titled confirmation.
 * When the administrator's own sign-in is what the server refuses, the page
 * offers the next step: sign in again, or — with no factor of their own — set
 * one up first (D-57-1, never a dead end).
 */
const apiCall = vi.fn()
const grantedPermissions = new Set<string>()
const signedIn = { id: "admin-id" }
let postAnswer: { error?: unknown; data?: unknown } = { data: undefined }
let ownRecoveryCodes: number | null = 4

const target = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "lost.phone@example.test",
  displayName: "Lost Phone",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  createdAt: "2026-08-20T07:00:00Z",
}

vi.mock("@authsystem/api/client", () => {
  const request = (path: string, options?: unknown) => {
    apiCall(path, options)
    if (path === "/api/v1/Users/{id}") return Promise.resolve({ data: target })
    if (path === "/api/v1/auth/2fa/status")
      return Promise.resolve({ data: { recoveryCodesRemaining: ownRecoveryCodes } })
    return Promise.resolve({ data: [] })
  }
  return {
    api: {
      GET: request,
      POST: (path: string, options?: unknown) => {
        apiCall(path, options)
        return Promise.resolve(postAnswer)
      },
      PUT: request,
      PATCH: request,
      DELETE: request,
    },
  }
})

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    user: signedIn,
    hasPermission: (permission: string) => grantedPermissions.has(permission),
    logout: vi.fn(),
  }),
}))

vi.mock("@authsystem/api/use-profile-image", () => ({
  useProfileImage: () => ({ pending: false, onChange: vi.fn(), onRemove: vi.fn() }),
}))

vi.mock("@authsystem/ui/crumbs", () => ({ usePageBreadcrumb: () => undefined }))

vi.mock("@authsystem/ui/data-table/data-table", () => ({
  DataTable: () => <div data-testid="data-table" />,
}))

import { UserDetailPage } from "./user-detail-page"

function renderPage(permissions: string[], userId: string = target.id) {
  grantedPermissions.clear()
  permissions.forEach((permission) => grantedPermissions.add(permission))
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/users/${userId}`]}>
        <Routes>
          <Route path="/users/:id" element={<UserDetailPage />} />
          <Route path="/profile" element={<p>profile page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const RESET = "Reset two-factor"

describe("resetting another account's two-factor", () => {
  beforeEach(async () => {
    apiCall.mockClear()
    postAnswer = { data: undefined }
    ownRecoveryCodes = 4
    signedIn.id = "admin-id"
    await i18n.changeLanguage("en")
  })

  it("is offered beside the two-factor row with the permission, even while the flag says off", async () => {
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    expect(await screen.findByRole("button", { name: RESET })).toBeInTheDocument()
  })

  it("is not offered without the permission", async () => {
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.manage])

    await screen.findByText(target.email)
    expect(screen.queryByRole("button", { name: RESET })).not.toBeInTheDocument()
  })

  it("is not offered on one's own account", async () => {
    signedIn.id = target.id
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await screen.findByText(target.email)
    expect(screen.queryByRole("button", { name: RESET })).not.toBeInTheDocument()
  })

  it("asks in a titled confirmation that names the account, then calls the reset", async () => {
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    const dialog = await screen.findByRole("alertdialog", {
      name: "Reset two-factor authentication?",
    })
    expect(dialog).toHaveTextContent("Lost Phone")

    await user.click(screen.getByRole("button", { name: "Reset" }))

    await waitFor(() =>
      expect(apiCall).toHaveBeenCalledWith("/api/v1/Users/{id}/two-factor/reset", {
        params: { path: { id: target.id } },
      })
    )
    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    )
  }, 15_000)

  it("asks the administrator to sign in again when their own session is not a recent two-factor one", async () => {
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))

    const signInAgain = await screen.findByRole("alertdialog", {
      name: "Sign in again to continue",
    })
    expect(signInAgain).toHaveTextContent(
      "Resetting another account's two-factor authentication needs your own recent sign-in"
    )
    expect(apiCall).toHaveBeenCalledWith("/api/v1/auth/2fa/status", undefined)
  }, 15_000)

  it("sends an administrator without a factor to set one up first", async () => {
    ownRecoveryCodes = null
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))

    const setUpFirst = await screen.findByRole("alertdialog", {
      name: "Set up your own two-factor authentication first",
    })
    expect(screen.queryByRole("alertdialog", { name: "Sign in again to continue" })).not.toBeInTheDocument()
    await user.click(
      await screen.findByRole("button", { name: "Open security settings" })
    )

    expect(await screen.findByText("profile page")).toBeInTheDocument()
    expect(setUpFirst).not.toBeInTheDocument()
  }, 15_000)

  it("closes on a refusal: the server decides", async () => {
    postAnswer = {
      error: { status: 403, code: "TwoFactor.ResetNotPermitted", detail: "x" },
    }
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))

    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    )
  }, 15_000)
})
