import type * as React from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

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
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ completeTwoFactor: mocks.completeTwoFactor }),
}))

vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: "/connect/authorize?client_id=app",
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
      `/forgot-password?returnTo=${encodeURIComponent("/connect/authorize?client_id=app")}`
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
