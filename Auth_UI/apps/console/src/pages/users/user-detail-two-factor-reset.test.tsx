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
 */
const apiCall = vi.fn()
const grantedPermissions = new Set<string>()
const signedIn = { id: "admin-id" }
let postAnswer: { error?: unknown; data?: unknown } = { data: undefined }

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
