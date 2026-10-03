import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen, within } from "@testing-library/react"
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

  it("refetches the account when disable answers User.TwoFactorNotEnabled", async () => {
    // Another tab switched it off, or the reconcile cleared the flag: the
    // card must stop offering a disable that can only fail (OI-45 (1), C-F3).
    post.mockResolvedValue(problem(400, "User.TwoFactorNotEnabled"))
    const user = userEvent.setup()
    const { invalidate } = renderCard({ twoFactorEnabled: true })

    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(
      screen.getByRole("button", { name: "profile.disableTwoFactor" })
    )

    await vi.waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ["me"] })
    )
  }, 15_000)
})

/**
 * The FIRST second factor, while email is on, also needs a code emailed to the
 * account's confirmed address (X02 PR B). Setup says so in `emailCodeRequired`;
 * an API that does not send the member needs no step.
 */
describe("the email code before the first second factor", () => {
  const post = api.POST as unknown as ReturnType<typeof vi.fn>

  const SETUP = {
    secret: "JBSWY3DPEHPK3PXP",
    qrCodeUri:
      "otpauth://totp/Example:john@example.com?secret=JBSWY3DPEHPK3PXP",
    manualEntryKey: "JBSW Y3DP EHPK 3PXP",
  }

  const SENT = {
    emailCodeRequired: true,
    sentTo: "j***n@example.com",
    expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  }

  const EMAIL_CODE_LABEL = "profile.twoFactorEmailCode"
  const SENT_TEXT =
    'profile.twoFactorEmailCodeSentTo|{"email":"j***n@example.com"} profile.twoFactorEmailCodeExpiresIn|{"minutes":15}'

  const problem = (status: number, code: string) => ({
    error: { status, code, detail: `server sentence for ${code}` },
  })

  beforeEach(() => {
    post.mockReset()
    toastError.mockReset()
  })

  function renderCard(me: Partial<Schemas["UserDto"]> = {}) {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    render(
      <QueryClientProvider client={client}>
        <ProfileSecurity
          me={
            {
              id: "11111111-1111-1111-1111-111111111111",
              email: "john@example.com",
              hasPassword: true,
              twoFactorEnabled: false,
              ...me,
            } as Schemas["UserDto"]
          }
        />
      </QueryClientProvider>
    )
  }

  /** Answers per path; the enable answer can be swapped per test. */
  function givenApi(
    setup: Record<string, unknown>,
    enable: () => unknown = () => ({ data: { recoveryCodes: ["AAAA-1111"] } }),
    emailCode: () => unknown = () => ({ data: SENT })
  ) {
    post.mockImplementation(async (path: string) => {
      if (path === "/api/v1/auth/2fa/setup") return { data: setup }
      if (path === "/api/v1/auth/2fa/email-code") return emailCode()
      if (path === "/api/v1/auth/2fa/enable") return enable()
      return { data: undefined }
    })
  }

  async function startSetup(
    user: ReturnType<typeof userEvent.setup>,
    me: Partial<Schemas["UserDto"]> = {}
  ) {
    renderCard(me)
    await user.click(
      screen.getByRole("button", { name: "profile.enableTwoFactor" })
    )
    await screen.findByLabelText("auth.twoFactorCode")
  }

  const enableBodies = () =>
    post.mock.calls
      .filter(([path]) => path === "/api/v1/auth/2fa/enable")
      .map(([, options]) => (options as { body: unknown }).body)

  it.each([
    ["absent", SETUP],
    ["false", { ...SETUP, emailCodeRequired: false }],
  ])(
    "shows no step when the flag is %s, and enables with the app code alone",
    async (_, setup) => {
      givenApi(setup)
      const user = userEvent.setup()
      await startSetup(user)

      expect(screen.queryByLabelText(EMAIL_CODE_LABEL)).not.toBeInTheDocument()
      await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
      await user.click(screen.getByRole("button", { name: "auth.verify" }))

      await vi.waitFor(() =>
        expect(enableBodies()).toEqual([{ code: "123456" }])
      )
      expect(post).not.toHaveBeenCalledWith(
        "/api/v1/auth/2fa/email-code",
        expect.anything()
      )
    },
    15_000
  )

  it("sends the code, names the address it went to, and enables with both codes", async () => {
    givenApi({ ...SETUP, emailCodeRequired: true })
    const user = userEvent.setup()
    await startSetup(user)

    const field = screen.getByLabelText(EMAIL_CODE_LABEL)
    expect(field).toHaveAttribute("inputmode", "numeric")
    // One "one-time-code" field: a password manager filling the app code
    // takes the first one, and the emailed one comes first.
    expect(field).toHaveAttribute("autocomplete", "off")
    expect(screen.getByLabelText("auth.twoFactorCode")).toHaveAttribute(
      "autocomplete",
      "one-time-code"
    )
    expect(screen.getByText("profile.twoFactorEmailCodeHint")).toBeVisible()

    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    // Both codes are needed: the app code alone cannot be submitted.
    expect(screen.getByRole("button", { name: "auth.verify" })).toBeDisabled()

    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
    )
    expect(await screen.findByText(SENT_TEXT)).toBeVisible()
    // The address is never pinned left-to-right; the page direction orders it.
    expect(screen.getByText(SENT_TEXT)).not.toHaveAttribute("dir")
    expect(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeResend" })
    ).toBeEnabled()

    await user.type(field, "654321")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    await vi.waitFor(() =>
      expect(enableBodies()).toEqual([{ code: "123456", emailCode: "654321" }])
    )
  }, 15_000)

  it("sends once for a double click: a second send would retire the first code", async () => {
    // The first send is still in flight when the second click lands.
    const pending: ((value: unknown) => void)[] = []
    givenApi(
      { ...SETUP, emailCodeRequired: true },
      undefined,
      () =>
        new Promise((resolve) => {
          pending.push(resolve)
        })
    )
    const user = userEvent.setup()
    await startSetup(user)

    // Two clicks in one tick: the button has not re-rendered disabled yet, the
    // frame a real double click lands in.
    const send = screen.getByRole("button", {
      name: "profile.twoFactorEmailCodeSend",
    })
    fireEvent.click(send)
    fireEvent.click(send)
    await vi.waitFor(() => expect(pending.length).toBeGreaterThan(0))
    for (const answer of pending) answer({ data: SENT })
    await screen.findByText(SENT_TEXT)

    expect(
      post.mock.calls.filter(([path]) => path === "/api/v1/auth/2fa/email-code")
    ).toHaveLength(1)
  }, 15_000)

  it("shows the step when enable answers TwoFactor.EmailCodeRequired", async () => {
    // A setting changed after setup, or setup came from an older API.
    givenApi(SETUP, () => problem(400, "TwoFactor.EmailCodeRequired"))
    const user = userEvent.setup()
    await startSetup(user)

    expect(screen.queryByLabelText(EMAIL_CODE_LABEL)).not.toBeInTheDocument()
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    expect(await screen.findByLabelText(EMAIL_CODE_LABEL)).toBeVisible()
    expect(toastError).toHaveBeenCalledWith(
      "server sentence for TwoFactor.EmailCodeRequired"
    )
    // The setup survives: the user carries on from where they were.
    expect(screen.getByLabelText("auth.twoFactorCode")).toHaveValue("123456")
  }, 15_000)

  it.each([
    [400, "TwoFactor.EmailCodeInvalid"],
    [403, "TwoFactor.LockedOut"],
  ])(
    "keeps the step and the address when enable answers %s %s",
    async (status, code) => {
      givenApi({ ...SETUP, emailCodeRequired: true }, () =>
        problem(status, code)
      )
      const user = userEvent.setup()
      await startSetup(user)

      await user.click(
        screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
      )
      await screen.findByText(SENT_TEXT)
      await user.type(screen.getByLabelText(EMAIL_CODE_LABEL), "111111")
      await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
      await user.click(screen.getByRole("button", { name: "auth.verify" }))

      await vi.waitFor(() =>
        expect(toastError).toHaveBeenCalledWith(`server sentence for ${code}`)
      )
      expect(screen.getByLabelText(EMAIL_CODE_LABEL)).toBeVisible()
      expect(screen.getByText(SENT_TEXT)).toBeVisible()
      expect(
        screen.getByRole("button", { name: "profile.twoFactorEmailCodeResend" })
      ).toBeEnabled()
    },
    15_000
  )

  it.each([
    [403, "TwoFactor.EmailCodeTooManyRequests"],
    [500, "TwoFactor.EmailCodeSendFailed"],
  ])(
    "says why a send failed (%s %s) and keeps the step",
    async (status, code) => {
      givenApi({ ...SETUP, emailCodeRequired: true }, undefined, () =>
        problem(status, code)
      )
      const user = userEvent.setup()
      await startSetup(user)

      await user.click(
        screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
      )

      await vi.waitFor(() =>
        expect(toastError).toHaveBeenCalledWith(`server sentence for ${code}`)
      )
      expect(screen.getByLabelText(EMAIL_CODE_LABEL)).toBeVisible()
      expect(
        screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
      ).toBeEnabled()
    },
    15_000
  )

  it("drops the step when the send answers that no code is needed", async () => {
    // Email was switched off after setup: enable then needs the app code alone.
    givenApi({ ...SETUP, emailCodeRequired: true }, undefined, () => ({
      data: { emailCodeRequired: false, sentTo: null, expiresAt: null },
    }))
    const user = userEvent.setup()
    await startSetup(user)

    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
    )

    await vi.waitFor(() =>
      expect(screen.queryByLabelText(EMAIL_CODE_LABEL)).not.toBeInTheDocument()
    )
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    expect(screen.getByRole("button", { name: "auth.verify" })).toBeEnabled()
  }, 15_000)

  it("offers to confirm an unconfirmed address instead of a send that must fail", async () => {
    // A provider sign-in can link an account whose own address was never
    // confirmed; the code goes only to a confirmed one (no dead end).
    givenApi({ ...SETUP, emailCodeRequired: true })
    const user = userEvent.setup()
    await startSetup(user, { emailConfirmed: false })

    expect(
      screen.getByText("profile.twoFactorEmailUnconfirmedTitle")
    ).toBeVisible()
    expect(screen.queryByLabelText(EMAIL_CODE_LABEL)).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "profile.twoFactorEmailCodeSend" })
    ).not.toBeInTheDocument()
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")
    expect(screen.getByRole("button", { name: "auth.verify" })).toBeDisabled()

    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailConfirm" })
    )

    // The shared confirmation dialog, for the account's own address.
    expect(await screen.findByText("auth.verifyEmailTitle")).toBeVisible()
    await vi.waitFor(() =>
      expect(post).toHaveBeenCalledWith(
        "/api/v1/Auth/resend-verification-email",
        { body: { email: "john@example.com" } }
      )
    )
    expect(post).not.toHaveBeenCalledWith(
      "/api/v1/auth/2fa/email-code",
      expect.anything()
    )
  }, 15_000)

  it("offers the confirmation when a send answers TwoFactor.EmailCodeRecipientUnavailable", async () => {
    // The profile said confirmed, the server knows better.
    givenApi({ ...SETUP, emailCodeRequired: true }, undefined, () =>
      problem(409, "TwoFactor.EmailCodeRecipientUnavailable")
    )
    const user = userEvent.setup()
    await startSetup(user)

    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
    )

    expect(
      await screen.findByRole("button", {
        name: "profile.twoFactorEmailConfirm",
      })
    ).toBeVisible()
    expect(toastError).toHaveBeenCalledWith(
      "server sentence for TwoFactor.EmailCodeRecipientUnavailable"
    )
    expect(screen.queryByLabelText(EMAIL_CODE_LABEL)).not.toBeInTheDocument()
  }, 15_000)

  it("enables once for a double click: a second enable finds the code spent", async () => {
    const pending: ((value: unknown) => void)[] = []
    givenApi(
      { ...SETUP, emailCodeRequired: true },
      () =>
        new Promise((resolve) => {
          pending.push(resolve)
        })
    )
    const user = userEvent.setup()
    await startSetup(user)
    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
    )
    await screen.findByText(SENT_TEXT)
    await user.type(screen.getByLabelText(EMAIL_CODE_LABEL), "654321")
    await user.type(screen.getByLabelText("auth.twoFactorCode"), "123456")

    // Two clicks in one tick, before the button re-renders disabled.
    const verify = screen.getByRole("button", { name: "auth.verify" })
    fireEvent.click(verify)
    fireEvent.click(verify)
    await vi.waitFor(() => expect(pending.length).toBeGreaterThan(0))
    for (const answer of pending) answer({ data: { recoveryCodes: ["A"] } })

    await vi.waitFor(() => expect(enableBodies()).toHaveLength(1))
    expect(toastError).not.toHaveBeenCalled()
  }, 15_000)

  it("clears a typed email code when a new one is sent: the old one is retired", async () => {
    givenApi({ ...SETUP, emailCodeRequired: true })
    const user = userEvent.setup()
    await startSetup(user)
    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeSend" })
    )
    await screen.findByText(SENT_TEXT)
    await user.type(screen.getByLabelText(EMAIL_CODE_LABEL), "111111")

    await user.click(
      screen.getByRole("button", { name: "profile.twoFactorEmailCodeResend" })
    )

    await vi.waitFor(() =>
      expect(screen.getByLabelText(EMAIL_CODE_LABEL)).toHaveValue("")
    )
  }, 15_000)
})
