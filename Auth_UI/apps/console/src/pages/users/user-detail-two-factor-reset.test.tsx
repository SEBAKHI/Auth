import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

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
let ownQualifyingMfa = false
let ownAccountReadFails = false
/** The window's refresh, held by a test whose proof is still on its way when the page moves (F35). */
let refreshGate: Promise<boolean> | null = null
const refreshes = vi.fn()

let allowedActions: { resetTwoFactor?: boolean } | undefined

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

/** A second account, for an administrator who goes Back to it under the window (F24). */
const other = {
  ...target,
  id: "22222222-2222-2222-2222-222222222222",
  email: "other.person@example.test",
  displayName: "Other Person",
}

vi.mock("@authsystem/api/client", () => {
  const request = (path: string, options?: unknown) => {
    apiCall(path, options)
    if (path === "/api/v1/Users/{id}") {
      const id = (options as { params?: { path?: { id?: string } } } | undefined)?.params?.path?.id
      return Promise.resolve({ data: { ...(id === other.id ? other : target), allowedActions } })
    }
    if (path === "/api/v1/auth/2fa/status")
      return Promise.resolve({ data: { recoveryCodesRemaining: ownRecoveryCodes } })
    if (path === "/api/v1/auth/reauthentication")
      return Promise.resolve({ data: { passkey: false, password: true, emailCode: false, secondStep: "None", canProveInPlace: true } })
    if (path === "/api/v1/Users/me")
      return Promise.resolve(
        ownAccountReadFails
          ? { data: undefined, error: { status: 500, code: "General.Unexpected", detail: "x" } }
          : { data: { qualifyingMfa: ownQualifyingMfa } }
      )
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
    // The window refreshes this browser's token before it hands back (REVIEW-FIXES 152 F23).
    refreshSessionNow: () => {
      refreshes()
      return refreshGate ?? Promise.resolve(true)
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

/** The page on a router the test moves, as Back and Forward do: the page stays mounted (F24, F35). */
function renderRouted(permissions: string[], userId: string = target.id) {
  grantedPermissions.clear()
  permissions.forEach((permission) => grantedPermissions.add(permission))
  const router = createMemoryRouter([{ path: "/users/:id", element: <UserDetailPage /> }], {
    initialEntries: [`/users/${userId}`],
  })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
  return {
    go: (id: string) =>
      act(async () => {
        await router.navigate(`/users/${id}`)
      }),
  }
}

/** The resets posted for one account. */
const resets = (id: string) =>
  apiCall.mock.calls.filter(
    ([path, options]) =>
      path === "/api/v1/Users/{id}/two-factor/reset" &&
      (options as { params: { path: { id: string } } }).params.path.id === id
  )

const RESET = "Reset two-factor"

describe("resetting another account's two-factor", () => {
  beforeEach(async () => {
    apiCall.mockClear()
    refreshes.mockClear()
    refreshGate = null
    postAnswer = { data: undefined }
    ownRecoveryCodes = 4
    ownQualifyingMfa = false
    ownAccountReadFails = false
    signedIn.id = "admin-id"
    allowedActions = undefined
    await i18n.changeLanguage("en")
  })

  // OI-225 (11): the server's answer for this account. A user manager on the
  // top administrator's page held the permission and was refused after the
  // dialog. Absent (an older API) keeps the button, as the cases above show.
  // Deliberate break: gate the button on the permission alone.
  it("is not offered where the server says the reset would be refused", async () => {
    allowedActions = { resetTwoFactor: false }
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await screen.findByText(target.email)
    expect(screen.queryByRole("button", { name: RESET })).not.toBeInTheDocument()
  })

  it("is offered where the server says the reset would pass", async () => {
    allowedActions = { resetTwoFactor: true }
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    expect(await screen.findByRole("button", { name: RESET })).toBeInTheDocument()
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

  it("asks the administrator to prove themself where they stand, then goes on with the reset", async () => {
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))

    const proof = await screen.findByRole("dialog", { name: "Confirm it's you" })
    expect(proof).toHaveTextContent(
      "Resetting two-factor authentication for another account needs you to confirm it's you"
    )
    expect(apiCall).toHaveBeenCalledWith("/api/v1/auth/2fa/status", undefined)

    postAnswer = { error: undefined }
    await user.type(await within(proof).findByLabelText("Password"), "my password")
    await user.click(within(proof).getByRole("button", { name: "Confirm" }))

    await waitFor(() =>
      expect(apiCall.mock.calls.filter(([path]) => path === "/api/v1/Users/{id}/two-factor/reset")).toHaveLength(2)
    )
    expect(screen.queryByRole("dialog", { name: "Confirm it's you" })).not.toBeInTheDocument()
  }, 15_000)

  it("after a proof given on another account's page, resets nothing there, and asks that account's confirmation", async () => {
    // REVIEW-FIXES 152 F24: the page stays mounted from one account to the next, and Back is not
    // held by the window. Deliberate break, verified red: the proof's hand-back resets the page's
    // current account.
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const { go } = renderRouted([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))
    const proof = await screen.findByRole("dialog", { name: "Confirm it's you" })
    expect(resets(target.id)).toHaveLength(1)

    await go(other.id)
    expect(await screen.findByText(other.email)).toBeInTheDocument()

    postAnswer = { error: undefined }
    await user.type(await within(proof).findByLabelText("Password"), "my password")
    await user.click(within(proof).getByRole("button", { name: "Confirm" }))

    const confirmation = await screen.findByRole("alertdialog", { name: "Reset two-factor authentication?" })
    expect(confirmation).toHaveTextContent("Other Person")
    expect(resets(other.id)).toHaveLength(0)
    expect(resets(target.id)).toHaveLength(1)
  }, 15_000)

  it("after a proof that lands once the page shows another account, resets nothing there, and asks that account's confirmation", async () => {
    // REVIEW-FIXES 152 F35 (a): the proof was on its way during Back, so it hands back in the
    // render of the first account. Deliberate break, verified red: the hand-back compares the
    // confirmed account with that render's account, not with the one the page shows.
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const { go } = renderRouted([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))
    const proof = await screen.findByRole("dialog", { name: "Confirm it's you" })

    postAnswer = { error: undefined }
    let refreshed!: (fresh: boolean) => void
    refreshGate = new Promise<boolean>((resolve) => {
      refreshed = resolve
    })
    await user.type(await within(proof).findByLabelText("Password"), "my password")
    await user.click(within(proof).getByRole("button", { name: "Confirm" }))
    await waitFor(() => expect(refreshes).toHaveBeenCalledTimes(1))

    await go(other.id)
    expect(await screen.findByText(other.email)).toBeInTheDocument()
    await act(async () => {
      refreshed(true)
    })

    const confirmation = await screen.findByRole("alertdialog", { name: "Reset two-factor authentication?" })
    expect(confirmation).toHaveTextContent("Other Person")
    expect(resets(other.id)).toHaveLength(0)
    expect(resets(target.id)).toHaveLength(1)
  }, 15_000)

  it("after a proof given on the administrator's own page, leaves no confirmation waiting for the next account", async () => {
    // REVIEW-FIXES 152 F35 (b): the confirmation is never shown on one's own page, so a flag set
    // there would open it, unasked, on the next account opened. Deliberate break, verified red: the
    // hand-back asks for the confirmation whatever the page offers.
    signedIn.id = other.id
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const { go } = renderRouted([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])
    const user = userEvent.setup()

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))
    const proof = await screen.findByRole("dialog", { name: "Confirm it's you" })

    await go(other.id)
    expect(await screen.findByText(other.email)).toBeInTheDocument()

    postAnswer = { error: undefined }
    await user.type(await within(proof).findByLabelText("Password"), "my password")
    await user.click(within(proof).getByRole("button", { name: "Confirm" }))
    await waitFor(() => expect(screen.queryByRole("dialog", { name: "Confirm it's you" })).not.toBeInTheDocument())

    await go(target.id)
    expect(await screen.findByRole("button", { name: RESET })).toBeInTheDocument()
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(resets(target.id)).toHaveLength(1)
  }, 15_000)

  it("offers the window when its own account cannot be read: the window is a way forward", async () => {
    // NIT-4, as ownProofForReset's comment says. Deliberate break, verified red: a failed read
    // of /Users/me answered as "no factor", which sends the administrator to set one up.
    ownRecoveryCodes = null
    ownAccountReadFails = true
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))

    expect(await screen.findByRole("dialog", { name: "Confirm it's you" })).toBeInTheDocument()
    expect(screen.queryByRole("alertdialog", { name: "Set up your own two-factor authentication first" })).not.toBeInTheDocument()
  }, 15_000)

  it("asks an administrator whose factor is a passkey for that passkey, not to set a factor up", async () => {
    ownRecoveryCodes = null
    ownQualifyingMfa = true
    postAnswer = {
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "x" },
    }
    const user = userEvent.setup()
    renderPage([PERMISSIONS.users.read, PERMISSIONS.users.resetTwoFactor])

    await user.click(await screen.findByRole("button", { name: RESET }))
    await user.click(await screen.findByRole("button", { name: "Reset" }))

    expect(await screen.findByRole("dialog", { name: "Sign in again to continue" })).toHaveTextContent(
      "Your account's second factor is a passkey"
    )
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

/**
 * S24 PR B (FA12): which second factor an account holds, and whether a two-factor
 * policy counts it, apart — the emailed code never does. The account flag alone
 * answers for an API that does not say which.
 */
describe("the two-factor row names the factor and whether it counts", () => {
  beforeEach(async () => {
    apiCall.mockClear()
    allowedActions = undefined
    signedIn.id = "admin-id"
    await i18n.changeLanguage("en")
  })

  afterEach(() => {
    Object.assign(target, { twoFactorEnabled: false, totpEnabled: undefined, emailCodeEnabled: undefined, qualifyingMfa: undefined, passkeyCount: undefined })
  })

  it("an emailed code: named, and not counted as MFA", async () => {
    Object.assign(target, { twoFactorEnabled: true, totpEnabled: false, emailCodeEnabled: true, qualifyingMfa: false })
    renderPage([PERMISSIONS.users.read])

    expect(await screen.findByText("Code by email · does not count as MFA")).toBeInTheDocument()
  })

  it("an authenticator app: named, and counted as MFA", async () => {
    Object.assign(target, { twoFactorEnabled: true, totpEnabled: true, emailCodeEnabled: false, qualifyingMfa: true })
    renderPage([PERMISSIONS.users.read])

    expect(await screen.findByText("Authenticator app · counts as MFA")).toBeInTheDocument()
  })

  it("whether it counts is the server's qualifyingMfa, never worked out from the factor", async () => {
    // Review of #107: the standing was derived from totpEnabled, so a policy the
    // server applies (FA2) and the page could disagree without anything failing.
    Object.assign(target, { twoFactorEnabled: true, totpEnabled: true, emailCodeEnabled: false, qualifyingMfa: false })
    renderPage([PERMISSIONS.users.read])

    expect(await screen.findByText("Authenticator app · does not count as MFA")).toBeInTheDocument()
  })

  // S20 PR C (FA12): the passkey leg, only where the account holds one, with the
  // server's standing. An account without one reads as it always did.
  it("passkeys: counted, with the server's standing", async () => {
    Object.assign(target, { twoFactorEnabled: true, totpEnabled: false, emailCodeEnabled: false, qualifyingMfa: true, passkeyCount: 2 })
    renderPage([PERMISSIONS.users.read])

    expect(await screen.findByText("Passkeys (2) · counts as MFA")).toBeInTheDocument()
  })

  it("passkeys beside the app: both named", async () => {
    Object.assign(target, { twoFactorEnabled: true, totpEnabled: true, emailCodeEnabled: false, qualifyingMfa: true, passkeyCount: "1" })
    renderPage([PERMISSIONS.users.read])

    expect(await screen.findByText("Authenticator app · Passkeys (1) · counts as MFA")).toBeInTheDocument()
  })

  it("no passkey: the line reads as before", async () => {
    Object.assign(target, { twoFactorEnabled: true, totpEnabled: true, emailCodeEnabled: false, qualifyingMfa: true, passkeyCount: 0 })
    renderPage([PERMISSIONS.users.read])

    expect(await screen.findByText("Authenticator app · counts as MFA")).toBeInTheDocument()
    expect(screen.queryByText(/Passkeys \(/)).not.toBeInTheDocument()
  })
})
