import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"

/**
 * S08 PR B on the Security tab: how many recovery codes are left (AM-S08-1), the
 * warning while few are, the one-shot warning after a sign-in that spent one,
 * and the two changes to a factor in use — new recovery codes and a new
 * authenticator app — each from a recent two-factor session.
 */
vi.mock("@authsystem/api/client", () => ({
  api: { POST: vi.fn(), GET: vi.fn(), DELETE: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

const { logout, toastError } = vi.hoisted(() => ({
  logout: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ logout }),
}))

vi.mock("sonner", () => ({
  toast: { error: toastError, success: vi.fn() },
}))

import { api } from "@authsystem/api/client"
import { markRecoveryCodeSignIn } from "@authsystem/auth/recovery-code-notice"

import { ProfileSecurity } from "./profile-security"

const post = api.POST as unknown as ReturnType<typeof vi.fn>
const get = api.GET as unknown as ReturnType<typeof vi.fn>

const ME = {
  id: "user-1",
  email: "john@example.com",
  hasPassword: true,
  twoFactorEnabled: true,
} as Schemas["UserDto"]

const SECRET = {
  secret: "JBSWY3DPEHPK3PXP",
  qrCodeUri: "otpauth://totp/Example:john@example.com?secret=JBSWY3DPEHPK3PXP",
  manualEntryKey: "JBSW Y3DP EHPK 3PXP",
  emailCodeRequired: false,
}

const problem = (status: number, code: string) => ({
  error: { status, code, detail: `server sentence for ${code}` },
})

function givenRemaining(count: number | null) {
  get.mockImplementation(async (path: string) =>
    path === "/api/v1/auth/2fa/status"
      ? { data: { recoveryCodesRemaining: count } }
      : { data: undefined }
  )
}

function renderTab(me: Schemas["UserDto"] = ME) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ProfileSecurity me={me} />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  post.mockReset()
  get.mockReset()
  logout.mockReset()
  toastError.mockReset()
  window.localStorage.clear()
})

describe("recovery codes left (AM-S08-1)", () => {
  it("says how many are left, and warns at three or fewer with the way to new ones", async () => {
    givenRemaining(3)
    renderTab()

    expect(await screen.findByText(/profile\.recoveryCodesRemaining\|\{"count":3\}/)).toBeInTheDocument()
    const alert = screen.getByRole("alert")
    expect(within(alert).getByText("profile.recoveryCodesLowTitle")).toBeInTheDocument()
    expect(within(alert).getByRole("button", { name: "profile.regenerateRecoveryCodes" })).toBeInTheDocument()
  })

  it("does not warn while more than three are left", async () => {
    givenRemaining(4)
    renderTab()

    expect(await screen.findByText(/profile\.recoveryCodesRemaining\|\{"count":4\}/)).toBeInTheDocument()
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
  })

  it("asks nothing of an account without a factor", () => {
    renderTab({ ...ME, twoFactorEnabled: false })

    expect(get).not.toHaveBeenCalledWith("/api/v1/auth/2fa/status")
    expect(screen.queryByText(/profile\.recoveryCodesRemaining/)).not.toBeInTheDocument()
  })

  it("warns once after a sign-in that spent a recovery code, for that account only", async () => {
    givenRemaining(9)
    markRecoveryCodeSignIn("someone-else")
    markRecoveryCodeSignIn(ME.id)
    renderTab()

    expect(await screen.findByText("profile.recoveryCodeUsedTitle")).toBeInTheDocument()
    // Shown once: the notice is gone for the next visit, and never touched for
    // another account on this browser.
    await waitFor(() =>
      expect(window.localStorage.getItem("auth.recoveryCodeSignIn:user-1")).toBeNull()
    )
    expect(window.localStorage.getItem("auth.recoveryCodeSignIn:someone-else")).toBe("1")
  })
})

describe("new recovery codes", () => {
  it("proves the factor in a titled dialog, then shows the new codes once", async () => {
    givenRemaining(2)
    post.mockResolvedValue({ data: { recoveryCodes: ["NEW-1", "NEW-2"] } })
    const user = userEvent.setup()
    renderTab()

    await user.click((await screen.findAllByRole("button", { name: "profile.regenerateRecoveryCodes" }))[0])
    const dialog = await screen.findByRole("dialog", { name: "profile.regenerateRecoveryCodesTitle" })
    await user.type(within(dialog).getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(within(dialog).getByRole("button", { name: "profile.regenerateRecoveryCodes" }))

    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/recovery-codes", {
      body: { code: "123456", useRecoveryCode: false },
    })
    expect(await screen.findByRole("dialog", { name: "profile.recoveryCodesTitle" })).toBeInTheDocument()
    expect(screen.queryByRole("dialog", { name: "profile.regenerateRecoveryCodesTitle" })).not.toBeInTheDocument()
  }, 15_000)

  it("accepts a recovery code instead", async () => {
    givenRemaining(1)
    post.mockResolvedValue({ data: { recoveryCodes: ["NEW-1"] } })
    const user = userEvent.setup()
    renderTab()

    await user.click((await screen.findAllByRole("button", { name: "profile.regenerateRecoveryCodes" }))[0])
    const dialog = await screen.findByRole("dialog", { name: "profile.regenerateRecoveryCodesTitle" })
    await user.click(within(dialog).getByRole("button", { name: "auth.useRecoveryCode" }))
    await user.type(within(dialog).getByLabelText("auth.recoveryCode"), "ABCD-1234")
    await user.click(within(dialog).getByRole("button", { name: "profile.regenerateRecoveryCodes" }))

    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/recovery-codes", {
      body: { code: "ABCD-1234", useRecoveryCode: true },
    })
  }, 15_000)

  it("asks to sign in again when the session is not a recent two-factor one", async () => {
    givenRemaining(5)
    post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole("button", { name: "profile.regenerateRecoveryCodes" }))
    const dialog = await screen.findByRole("dialog", { name: "profile.regenerateRecoveryCodesTitle" })
    await user.type(within(dialog).getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(within(dialog).getByRole("button", { name: "profile.regenerateRecoveryCodes" }))

    expect(await screen.findByRole("alertdialog", { name: "auth.reauthenticateTitle" })).toBeInTheDocument()
    expect(screen.getByText("profile.twoFactorChangeNeedsRecentSignIn")).toBeInTheDocument()
    expect(toastError).not.toHaveBeenCalled()
  }, 15_000)

  it("leaves switching off with its own reason: a recent sign-in, two factors not required", async () => {
    givenRemaining(5)
    post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderTab()

    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(screen.getByRole("button", { name: "profile.disableTwoFactor" }))

    expect(await screen.findByRole("alertdialog", { name: "auth.reauthenticateTitle" })).toBeInTheDocument()
    expect(screen.getByText("auth.reauthenticateBody")).toBeInTheDocument()
    expect(screen.queryByText("profile.twoFactorChangeNeedsRecentSignIn")).not.toBeInTheDocument()
  }, 15_000)

  it("keeps the dialog for another try after a wrong code", async () => {
    givenRemaining(5)
    post.mockResolvedValue(problem(400, "User.InvalidTwoFactorCode"))
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole("button", { name: "profile.regenerateRecoveryCodes" }))
    const dialog = await screen.findByRole("dialog", { name: "profile.regenerateRecoveryCodesTitle" })
    await user.type(within(dialog).getByLabelText("auth.twoFactorCode"), "999999")
    await user.click(within(dialog).getByRole("button", { name: "profile.regenerateRecoveryCodes" }))

    await waitFor(() => expect(toastError).toHaveBeenCalled())
    expect(screen.getByRole("dialog", { name: "profile.regenerateRecoveryCodesTitle" })).toBeInTheDocument()
    expect(within(dialog).getByLabelText("auth.twoFactorCode")).toHaveValue("")
  }, 15_000)
})

describe("a new authenticator app", () => {
  it("proves the current factor, shows the new secret, and confirms with the new app's code", async () => {
    givenRemaining(8)
    post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/2fa/replace"
        ? { data: SECRET }
        : { data: { recoveryCodes: ["NEW-1"] } }
    )
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole("button", { name: "profile.replaceAuthenticator" }))
    const dialog = await screen.findByRole("dialog", { name: "profile.replaceAuthenticatorTitle" })
    await user.type(within(dialog).getByLabelText("auth.twoFactorCode"), "111111")
    await user.click(within(dialog).getByRole("button", { name: "common.next" }))

    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/replace", {
      body: { code: "111111", useRecoveryCode: false },
    })
    // The same panel the first setup shows.
    expect(await within(dialog).findByDisplayValue(SECRET.manualEntryKey)).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText("auth.twoFactorCode"), "222222")
    await user.click(within(dialog).getByRole("button", { name: "profile.replaceAuthenticatorConfirm" }))

    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/replace/confirm", {
      body: { code: "222222" },
    })
    expect(await screen.findByRole("dialog", { name: "profile.recoveryCodesTitle" })).toBeInTheDocument()
  }, 20_000)

  it("starts again when the new secret expired", async () => {
    givenRemaining(8)
    post.mockImplementation(async (path: string) =>
      path === "/api/v1/auth/2fa/replace"
        ? { data: SECRET }
        : problem(409, "TwoFactor.NoPendingReplacement")
    )
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole("button", { name: "profile.replaceAuthenticator" }))
    const dialog = await screen.findByRole("dialog", { name: "profile.replaceAuthenticatorTitle" })
    await user.type(within(dialog).getByLabelText("auth.twoFactorCode"), "111111")
    await user.click(within(dialog).getByRole("button", { name: "common.next" }))
    await user.type(await within(dialog).findByLabelText("auth.twoFactorCode"), "222222")
    await user.click(within(dialog).getByRole("button", { name: "profile.replaceAuthenticatorConfirm" }))

    await waitFor(() => expect(toastError).toHaveBeenCalled())
    // Back to the first step: prove the current factor for a fresh secret.
    expect(await within(dialog).findByText("profile.replaceAuthenticatorProveBody")).toBeInTheDocument()
    expect(within(dialog).queryByDisplayValue(SECRET.manualEntryKey)).not.toBeInTheDocument()
  }, 20_000)
})
