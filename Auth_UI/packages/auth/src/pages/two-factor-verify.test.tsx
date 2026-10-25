import type * as React from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * X12 PR B: a second factor at the consecutive-failure limit accepts no code
 * until the password is reset and an administrator resets the factor. The page
 * then stops asking for a code and names the first way out, persistently — a
 * toast that disappears over a field that still takes codes would be a dead end.
 */
const mocks = vi.hoisted(() => ({
  completeTwoFactor: vi.fn(),
  complete: vi.fn(),
  toastError: vi.fn(),
  apiPost: vi.fn(),
  returnTo: "https://api.example.com/api/v1/auth/authorize?client_id=app",
}))

// The emailed code's send (S24): the page's one API call of its own.
vi.mock("@authsystem/api/client", () => ({
  api: { POST: mocks.apiPost },
}))

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ completeTwoFactor: mocks.completeTwoFactor }),
}))

vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: mocks.returnTo,
    complete: mocks.complete,
  }),
}))

vi.mock("../flow-branding", () => ({
  useFlowBranding: () => ({
    layout: { appName: null, appLogoUrl: null, securedBy: null },
  }),
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("sonner", () => ({
  toast: { error: mocks.toastError, success: vi.fn() },
}))

// The real layout carries the language and theme toggles, which need their
// providers; the page's own content is what is under test.
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    children,
    footer,
  }: {
    title: string
    children: React.ReactNode
    footer?: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {children}
      {footer}
    </div>
  ),
}))

const { TwoFactorVerifyPage } = await import("./two-factor-verify")
const { clearPendingTwoFactorChallenge, setPendingTwoFactorChallenge } =
  await import("../pending-challenge")

const problem = (status: number, code: string) => ({
  status,
  code,
  detail: `server sentence for ${code}`,
})

function renderPage() {
  return render(
    <MemoryRouter
      initialEntries={[
        { pathname: "/two-factor", state: { challengeToken: "challenge-1" } },
      ]}
    >
      <TwoFactorVerifyPage />
    </MemoryRouter>
  )
}

async function submitRecoveryCode() {
  const user = userEvent.setup()
  await user.click(screen.getByRole("button", { name: "auth.useRecoveryCode" }))
  await user.type(screen.getByLabelText("auth.recoveryCode"), "ABCD-1234")
  await user.click(screen.getByRole("button", { name: "auth.verify" }))
  return user
}

describe("TwoFactorVerifyPage", () => {
  beforeEach(() => {
    mocks.completeTwoFactor.mockReset()
    mocks.complete.mockReset()
    mocks.toastError.mockReset()
    mocks.returnTo =
      "https://api.example.com/api/v1/auth/authorize?client_id=app"
  })

  it("at the limit, the forgot-password link carries a stored request as client_id and its handle (OI-145)", async () => {
    const handle = "0123456789abcdef".repeat(4)
    mocks.returnTo = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${handle}`
    mocks.completeTwoFactor.mockRejectedValue(
      problem(403, "TwoFactor.LockedAdminResetRequired")
    )
    renderPage()

    await submitRecoveryCode()

    expect(
      await screen.findByRole("link", { name: "auth.forgotPassword" })
    ).toHaveAttribute(
      "href",
      `/forgot-password?client_id=app&auth_request=${handle}`
    )
  })

  it("at the limit, shows the server's sentence with the forgot-password link and takes no further code", async () => {
    mocks.completeTwoFactor.mockRejectedValue(
      problem(403, "TwoFactor.LockedAdminResetRequired")
    )
    renderPage()

    await submitRecoveryCode()

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent("auth.twoFactorLockedTitle")
    expect(alert).toHaveTextContent(
      "server sentence for TwoFactor.LockedAdminResetRequired"
    )

    const link = screen.getByRole("link", { name: "auth.forgotPassword" })
    expect(link).toHaveAttribute(
      "href",
      `/forgot-password?returnTo=${encodeURIComponent("https://api.example.com/api/v1/auth/authorize?client_id=app")}`
    )

    // Nothing left to submit: the field, the button and the mode switch are off.
    expect(screen.getByLabelText("auth.recoveryCode")).toBeDisabled()
    expect(screen.getByRole("button", { name: "auth.verify" })).toBeDisabled()
    expect(
      screen.getByRole("button", { name: "auth.useAuthenticatorCode" })
    ).toBeDisabled()
    expect(mocks.toastError).not.toHaveBeenCalled()
    expect(mocks.completeTwoFactor).toHaveBeenCalledTimes(1)
  })

  it("a timed lock stays a toast, and the code can be tried again later", async () => {
    mocks.completeTwoFactor.mockRejectedValue(
      problem(403, "TwoFactor.LockedOut")
    )
    renderPage()

    await submitRecoveryCode()

    expect(mocks.toastError).toHaveBeenCalledWith(
      "server sentence for TwoFactor.LockedOut"
    )
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
    expect(screen.getByLabelText("auth.recoveryCode")).toBeEnabled()
  })
})

/**
 * S24 PR B: the second step the server names (`twoFactorMethods`). The emailed code
 * is sent only when asked for (D-T30 B), the newest code is the one that works, the
 * resend never outlives the sends left (OI-220 (1)), a sign-in that cannot be
 * emailed a code offers the recovery code and the password sign-in, and every way
 * out keeps the relying party's request.
 */
describe("TwoFactorVerifyPage with the methods the server named", () => {
  const RETURN_TO = "https://api.example.com/api/v1/auth/authorize?client_id=app"
  const LOGIN_SEARCH = `?returnTo=${encodeURIComponent(RETURN_TO)}`

  function LoginProbe() {
    const location = useLocation()
    return <p data-testid="login-search">{location.search}</p>
  }

  function renderWith(methods: string[] | null) {
    return render(
      <MemoryRouter
        initialEntries={[
          { pathname: "/two-factor", state: { challengeToken: "challenge-1", methods } },
        ]}
      >
        <Routes>
          <Route path="/two-factor" element={<TwoFactorVerifyPage />} />
          <Route path="/login" element={<LoginProbe />} />
        </Routes>
      </MemoryRouter>
    )
  }

  const sent = (sendsRemaining: number) => ({
    data: {
      expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
      maskedEmail: "o***r@example.com",
      sendsRemaining,
    },
    error: undefined,
  })

  beforeEach(() => {
    mocks.completeTwoFactor.mockReset()
    mocks.complete.mockReset()
    mocks.toastError.mockReset()
    mocks.apiPost.mockReset()
    mocks.returnTo = RETURN_TO
  })

  describe("when the server emailed the code with the challenge (D-243)", () => {
    const sentWithChallenge = (receivedAt: number, challengeToken = "challenge-1") =>
      setPendingTwoFactorChallenge(challengeToken, false, ["email", "recovery"], {
        expiresAt: new Date(receivedAt + 10 * 60_000).toISOString(),
        maskedEmail: "o***r@example.com",
        sendsRemaining: 2,
        receivedAt,
      })

    afterEach(() => clearPendingTwoFactorChallenge())

    it("asks for the code at once, says where it went, and sends nothing itself", async () => {
      sentWithChallenge(Date.now())
      mocks.completeTwoFactor.mockResolvedValue({ requiresPasswordChange: false })
      const user = userEvent.setup()
      renderWith(["email", "recovery"])

      expect(screen.getByLabelText("auth.emailCode")).toBeInTheDocument()
      expect(screen.getByText(/auth.emailCodeSentTo/)).toBeInTheDocument()
      expect(screen.queryByRole("button", { name: "auth.emailMeACode" })).not.toBeInTheDocument()
      // The resend waits a full minute from the answer's arrival.
      expect(screen.getByRole("button", { name: /accountDeletion\.resend/ })).toBeDisabled()
      expect(mocks.apiPost).not.toHaveBeenCalled()

      await user.type(screen.getByLabelText("auth.emailCode"), "123456")

      expect(mocks.completeTwoFactor).toHaveBeenCalledWith("challenge-1", "123456", "email")
      expect(mocks.apiPost).not.toHaveBeenCalled()
    })

    it("offers the resend once the minute from the answer's arrival has passed", () => {
      sentWithChallenge(Date.now() - 61_000)
      renderWith(["email", "recovery"])

      expect(screen.getByRole("button", { name: /accountDeletion\.resend/ })).toBeEnabled()
      expect(mocks.apiPost).not.toHaveBeenCalled()
    })

    it("ignores a code sent for another challenge: the button sends one", () => {
      sentWithChallenge(Date.now(), "challenge-0")
      renderWith(["email", "recovery"])

      expect(screen.getByRole("button", { name: "auth.emailMeACode" })).toBeInTheDocument()
      expect(screen.queryByLabelText("auth.emailCode")).not.toBeInTheDocument()
    })
  })

  it("offers an explicit button when no code came with the challenge, sends nothing on arrival, and asks for no code before one was sent", () => {
    renderWith(["email", "recovery"])

    expect(screen.getByRole("button", { name: "auth.emailMeACode" })).toBeInTheDocument()
    expect(mocks.apiPost).not.toHaveBeenCalled()
    expect(screen.queryByLabelText("auth.emailCode")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.verify" })).not.toBeInTheDocument()
    // No authenticator to download for an account whose factor is the email.
    expect(screen.getByRole("button", { name: "auth.useRecoveryCode" })).toBeInTheDocument()
  })

  it("sends on request, then asks for the code and completes the sign-in with the email method", async () => {
    mocks.apiPost.mockResolvedValue(sent(2))
    mocks.completeTwoFactor.mockResolvedValue({ requiresPasswordChange: false })
    const user = userEvent.setup()
    renderWith(["email", "recovery"])

    await user.click(screen.getByRole("button", { name: "auth.emailMeACode" }))

    expect(mocks.apiPost).toHaveBeenCalledWith("/api/v1/auth/2fa/verify/email-code", {
      body: { challengeToken: "challenge-1" },
    })
    expect(await screen.findByText(/auth.emailCodeNewestOnly/)).toBeInTheDocument()
    // The resend waits out its cooldown.
    expect(screen.getByRole("button", { name: /accountDeletion\.resend/ })).toBeDisabled()

    await user.type(screen.getByLabelText("auth.emailCode"), "123456")

    expect(mocks.completeTwoFactor).toHaveBeenCalledWith("challenge-1", "123456", "email")
    expect(mocks.complete).toHaveBeenCalled()
  })

  it("recovery codes at the limit leave the emailed code, which counts apart, as the way in", async () => {
    // Review of #107: the shared counter's limit hid the emailed code too.
    mocks.completeTwoFactor.mockRejectedValue(problem(403, "TwoFactor.LockedAdminResetRequired"))
    const user = userEvent.setup()
    renderWith(["email", "recovery"])

    await user.click(screen.getByRole("button", { name: "auth.useRecoveryCode" }))
    await user.type(screen.getByLabelText("auth.recoveryCode"), "ABCD-2345")
    await user.click(screen.getByRole("button", { name: "auth.verify" }))

    expect(
      await screen.findByText("server sentence for TwoFactor.LockedAdminResetRequired")
    ).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "auth.emailMeACode" })).toBeEnabled()
    expect(screen.queryByRole("button", { name: /auth\.use(Email|Recovery)Code/ })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "auth.forgotPassword" })).not.toBeInTheDocument()
  })

  it("with no send left, offers no resend but a new sign-in that keeps the request (OI-220 (1))", async () => {
    mocks.apiPost.mockResolvedValue(sent(0))
    const user = userEvent.setup()
    renderWith(["email", "recovery"])

    await user.click(screen.getByRole("button", { name: "auth.emailMeACode" }))

    expect(await screen.findByText("auth.emailCodeNoMoreSends")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /accountDeletion\.resend/ })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "auth.startSignInAgain" })).toHaveAttribute(
      "href",
      `/login${LOGIN_SEARCH}`
    )
    // The code that was sent can still be typed.
    expect(screen.getByLabelText("auth.emailCode")).toBeEnabled()
  })

  it("at the account's cap, shows the published sentence and the new sign-in, never a resend", async () => {
    mocks.apiPost.mockResolvedValue({
      data: undefined,
      error: problem(403, "TwoFactor.EmailCodeTooManyRequests"),
    })
    const user = userEvent.setup()
    renderWith(["email", "recovery"])

    await user.click(screen.getByRole("button", { name: "auth.emailMeACode" }))

    expect(
      await screen.findByText("server sentence for TwoFactor.EmailCodeTooManyRequests")
    ).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "auth.emailMeACode" })).toBeDisabled()
    expect(screen.getByRole("link", { name: "auth.startSignInAgain" })).toHaveAttribute(
      "href",
      `/login${LOGIN_SEARCH}`
    )
  })

  it("an expired challenge goes back to the sign-in page with the request", async () => {
    mocks.apiPost.mockResolvedValue({
      data: undefined,
      error: problem(401, "TwoFactor.ChallengeInvalid"),
    })
    const user = userEvent.setup()
    renderWith(["email", "recovery"])

    await user.click(screen.getByRole("button", { name: "auth.emailMeACode" }))

    expect(await screen.findByTestId("login-search")).toHaveTextContent(LOGIN_SEARCH)
  })

  it("with the recovery code alone, says why and offers the password sign-in", () => {
    renderWith(["recovery"])

    expect(screen.getByText("auth.recoveryOnlyTitle")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "auth.signInWithPassword" })).toHaveAttribute(
      "href",
      `/login${LOGIN_SEARCH}`
    )
    expect(screen.getByLabelText("auth.recoveryCode")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.emailMeACode" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.useAuthenticatorCode" })).not.toBeInTheDocument()
  })

  it("names no methods: the screen every account always saw", () => {
    renderWith(null)

    expect(screen.getByLabelText("auth.twoFactorCode")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "auth.useRecoveryCode" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.emailMeACode" })).not.toBeInTheDocument()
  })

  // Review of #107 (F7 (b)): every authenticator account gets ["totp","recovery"] from
  // the new API; it must keep the screen it always had, never the emailed code's.
  it("an authenticator account sees the app's field and the recovery switch, never the emailed code", () => {
    renderWith(["totp", "recovery"])

    expect(screen.getByLabelText("auth.twoFactorCode")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "auth.useRecoveryCode" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.emailMeACode" })).not.toBeInTheDocument()
    expect(screen.queryByText("auth.emailCodeIntro")).not.toBeInTheDocument()
  })

  it("the way to another account keeps the request", () => {
    renderWith(["totp", "recovery"])

    expect(screen.getByRole("link", { name: "auth.useDifferentAccount" })).toHaveAttribute(
      "href",
      `/login${LOGIN_SEARCH}`
    )
  })
})
