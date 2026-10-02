import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"

/**
 * Which password card the Security tab shows.
 *
 * An account created by signing in with Google has no password, so the change
 * form demanded a current password it could never supply - not awkward, but
 * literally unsubmittable, and the only route out of it was a reset link the
 * product never mentioned. `hasPassword` had been on this very query all along
 * with no reader.
 *
 * The direction of the fallback is the part that matters. `hasPassword` is
 * optional in the generated schema, so an API that stopped sending it must land
 * on the change form (harmless for the many, useless for the few) rather than
 * hiding the change form from everybody.
 */
vi.mock("@authsystem/api/client", () => ({
  api: { POST: vi.fn(), GET: vi.fn(), DELETE: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  // @authsystem/api/errors pulls in the i18n singleton, which calls this at
  // import time; without it the whole module graph fails to load.
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

const { logout, toastError } = vi.hoisted(() => ({
  logout: vi.fn(),
  toastError: vi.fn(),
}))

// The sign-in-again dialog signs out through the session; nothing else here
// reads it.
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ logout }),
}))

vi.mock("sonner", () => ({
  toast: { error: toastError, success: vi.fn() },
}))

import { api } from "@authsystem/api/client"

import { ProfileSecurity } from "./profile-security"

function renderTab(me: Partial<Schemas["UserDto"]>) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <ProfileSecurity me={me as Schemas["UserDto"]} />
    </QueryClientProvider>
  )
}

const CURRENT_PASSWORD_LABEL = "auth.currentPassword"
const SET_PASSWORD_TITLE = "profile.setPassword"

describe("the Security tab password card", () => {
  it("offers to set one when the account has no password", () => {
    renderTab({ email: "external@example.com", hasPassword: false })

    expect(screen.getByText(SET_PASSWORD_TITLE)).toBeInTheDocument()
    // The field it could never fill is gone, not merely optional.
    expect(screen.queryByText(CURRENT_PASSWORD_LABEL)).not.toBeInTheDocument()
  })

  it("keeps the change form when the account has a password", () => {
    renderTab({ email: "john@example.com", hasPassword: true })

    expect(screen.getByText(CURRENT_PASSWORD_LABEL)).toBeInTheDocument()
    expect(screen.queryByText(SET_PASSWORD_TITLE)).not.toBeInTheDocument()
  })

  it("falls back to the change form when the API omits hasPassword", () => {
    renderTab({ email: "john@example.com" })

    expect(screen.getByText(CURRENT_PASSWORD_LABEL)).toBeInTheDocument()
    expect(screen.queryByText(SET_PASSWORD_TITLE)).not.toBeInTheDocument()
  })

  it("falls back to the change form when there is no address to mail", () => {
    // Nothing usable can be offered without an address, so do not offer a card
    // whose only button cannot work.
    renderTab({ email: undefined, hasPassword: false })

    expect(screen.getByText(CURRENT_PASSWORD_LABEL)).toBeInTheDocument()
    expect(screen.queryByText(SET_PASSWORD_TITLE)).not.toBeInTheDocument()
  })

  it("puts every reason the server refuses the new password under its field", async () => {
    // The API reports every broken rule at once; the card must show them all
    // under the control rather than the first one in a toast.
    const post = api.POST as unknown as ReturnType<typeof vi.fn>
    post.mockResolvedValue({
      error: {
        status: 400,
        code: "Password.TooShort",
        detail: "Password must be at least 12 characters long.",
        errors: [
          { code: "Password.TooShort", pointer: "#/newPassword" },
          { code: "Password.RequiresDigit", pointer: "#/newPassword" },
        ],
      },
    })
    const user = userEvent.setup()
    renderTab({ email: "john@example.com", hasPassword: true })

    await user.type(screen.getByLabelText(CURRENT_PASSWORD_LABEL), "OldPass1!")
    await user.type(screen.getByLabelText("auth.newPassword"), "NewPass1!")
    await user.type(screen.getByLabelText("auth.confirmPassword"), "NewPass1!")
    await user.click(
      screen.getByRole("button", { name: "profile.changePassword" })
    )

    expect(
      await screen.findByText("Password must be at least 12 characters long.")
    ).toBeVisible()
    expect(
      screen.getByText("Password must contain at least one digit.")
    ).toBeVisible()
    expect(screen.getByLabelText("auth.newPassword")).toHaveAttribute(
      "aria-invalid",
      "true"
    )
  }, 15_000)
})

/**
 * The two-factor card's changes: a stale sign-in, a recovery code for a lost
 * phone, and a factor another tab already changed.
 *
 * The card branches on the published CODE, never on the status:
 * TwoFactor.LockedOut is a 403 too, and signing in again does not unlock it.
 */
describe("the Security tab two-factor card", () => {
  const post = api.POST as unknown as ReturnType<typeof vi.fn>

  const SETUP = {
    secret: "JBSWY3DPEHPK3PXP",
    qrCodeUri:
      "otpauth://totp/Example:john@example.com?secret=JBSWY3DPEHPK3PXP",
    manualEntryKey: "JBSW Y3DP EHPK 3PXP",
  }

  const problem = (status: number, code: string) => ({
    error: { status, code, detail: `server sentence for ${code}` },
  })

  beforeEach(() => {
    post.mockReset()
    logout.mockReset()
    toastError.mockReset()
  })

  function renderCard(me: Partial<Schemas["UserDto"]>) {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    const invalidate = vi.spyOn(client, "invalidateQueries")
    render(
      <QueryClientProvider client={client}>
        <ProfileSecurity
          me={
            {
              email: "john@example.com",
              hasPassword: true,
              ...me,
            } as Schemas["UserDto"]
          }
        />
      </QueryClientProvider>
    )
    return { invalidate }
  }

  /** Each of the three changes, driven to its request through the card. */
  const changes = {
    setup: async (user: ReturnType<typeof userEvent.setup>) => {
      renderCard({ twoFactorEnabled: false })
      await user.click(
        screen.getByRole("button", { name: "profile.enableTwoFactor" })
      )
    },
    enable: async (user: ReturnType<typeof userEvent.setup>) => {
      renderCard({ twoFactorEnabled: false })
      await user.click(
        screen.getByRole("button", { name: "profile.enableTwoFactor" })
      )
      await user.type(
        await screen.findByLabelText("auth.twoFactorCode"),
        "123456"
      )
      await user.click(screen.getByRole("button", { name: "auth.verify" }))
    },
    disable: async (user: ReturnType<typeof userEvent.setup>) => {
      renderCard({ twoFactorEnabled: true })
      await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
      await user.click(
        screen.getByRole("button", { name: "profile.disableTwoFactor" })
      )
    },
  }

  it("sends useRecoveryCode with a recovery code, typed as text", async () => {
    post.mockResolvedValue({ data: undefined })
    const user = userEvent.setup()
    renderCard({ twoFactorEnabled: true })

    await user.click(
      screen.getByRole("button", { name: "auth.useRecoveryCode" })
    )
    const field = screen.getByLabelText("auth.recoveryCode")
    // A recovery code is neither numeric nor a code the browser should offer.
    expect(field).not.toHaveAttribute("inputmode")
    expect(field).toHaveAttribute("autocomplete", "off")
    await user.type(field, "ABCD-1234")
    await user.click(
      screen.getByRole("button", { name: "profile.disableTwoFactor" })
    )

    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/disable", {
      body: { code: "ABCD-1234", useRecoveryCode: true },
    })
  }, 15_000)

  it("sends useRecoveryCode false with an authenticator code", async () => {
    post.mockResolvedValue({ data: undefined })
    const user = userEvent.setup()
    renderCard({ twoFactorEnabled: true })

    const field = screen.getByLabelText("auth.twoFactorCode")
    expect(field).toHaveAttribute("inputmode", "numeric")
    expect(field).toHaveAttribute("autocomplete", "one-time-code")
    // The description says what switching off does, before it is done.
    expect(
      screen.getByText("profile.twoFactorDisableSignsOutOthers")
    ).toBeVisible()

    await user.type(field, "123456")
    await user.click(
      screen.getByRole("button", { name: "profile.disableTwoFactor" })
    )

    expect(post).toHaveBeenCalledWith("/api/v1/auth/2fa/disable", {
      body: { code: "123456", useRecoveryCode: false },
    })
  }, 15_000)

  it.each(["setup", "enable", "disable"] as const)(
    "asks to sign in again on Auth.ReauthenticationRequired from %s",
    async (change) => {
      post.mockImplementation(async (path: string) =>
        path === "/api/v1/auth/2fa/setup" && change !== "setup"
          ? { data: SETUP }
          : problem(403, "Auth.ReauthenticationRequired")
      )
      const user = userEvent.setup()

      await changes[change](user)

      const dialog = await screen.findByRole("alertdialog", {
        name: "auth.reauthenticateTitle",
      })
      expect(toastError).not.toHaveBeenCalled()

      // Its way forward is signing out; the route guard and the sign-in
      // completion bring the user back to this tab.
      await user.click(
        within(dialog).getByRole("button", {
          name: "auth.reauthenticateAction",
        })
      )
      expect(logout).toHaveBeenCalledTimes(1)
    },
    15_000
  )

  it("keeps a locked factor a toast: a 403 is not a stale sign-in", async () => {
    post.mockResolvedValue(problem(403, "TwoFactor.LockedOut"))
    const user = userEvent.setup()

    await changes.disable(user)

    expect(toastError).toHaveBeenCalledWith(
      "server sentence for TwoFactor.LockedOut"
    )
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
  }, 15_000)

  it.each([
    [409, "User.TwoFactorAlreadyEnabled"],
    [400, "TwoFactor.SetupRequired"],
  ])(
    "refetches the account and clears the setup when enable answers %s %s",
    async (status, code) => {
      post.mockImplementation(async (path: string) =>
        path === "/api/v1/auth/2fa/setup"
          ? { data: SETUP }
          : problem(status, code)
      )
      const user = userEvent.setup()
      const { invalidate } = renderCard({ twoFactorEnabled: false })

      await user.click(
        screen.getByRole("button", { name: "profile.enableTwoFactor" })
      )
      await user.type(
        await screen.findByLabelText("auth.twoFactorCode"),
        "123456"
      )
      await user.click(screen.getByRole("button", { name: "auth.verify" }))

      // Another tab won (or replaced the secret): its codes are the stored
      // ones, so this card shows none, says why, and reads the account again.
      await vi.waitFor(() =>
        expect(invalidate).toHaveBeenCalledWith({ queryKey: ["me"] })
      )
      expect(toastError).toHaveBeenCalledTimes(1)
      expect(
        screen.queryByLabelText("auth.twoFactorCode")
      ).not.toBeInTheDocument()
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    },
    15_000
  )
})
