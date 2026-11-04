import type * as React from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { SignInOptions } from "@authsystem/ui/branding"

import type { SignInReason } from "../sign-in-reason"

/**
 * S23 on the sign-in page: an application that requires two-step verification
 * sent the person back. The page says why, puts the server's method word
 * between the person's click and the remembered word (D-S23-3 (c)), and a code
 * sign-in asks for full proof (T-B2).
 */

const HANDLE = "0123456789abcdef".repeat(4)
const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  flowProps: [] as Array<Record<string, unknown>>,
  completion: {
    returnTo: null as string | null,
    reason: null as SignInReason | null,
  },
  branding: {
    staySignedInOffered: true as boolean | null,
    signInOptions: undefined as SignInOptions | null | undefined,
  },
}))

vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ name: "AuthSystem", logoUrl: null, isPending: false, ...mocks.branding }),
}))
vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => mocks.post(...args) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ login: vi.fn() }),
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    ...mocks.completion,
    complete: vi.fn(),
    challenge: vi.fn(),
    interstitial: vi.fn(),
  }),
}))
vi.mock("../flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: null,
    layout: { appName: null, appLogoUrl: null, appLogoUrlDark: null, securedBy: null },
  }),
}))
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "en" } }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))
vi.mock("sonner", () => ({
  toast: { error: vi.fn(), success: vi.fn() },
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({ title, children }: { title: string; children: React.ReactNode }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
}))
vi.mock("./email-code-flow", () => ({
  EmailCodeFlow: (props: Record<string, unknown>) => {
    mocks.flowProps.push(props)
    return <p>code flow</p>
  },
}))

import { LAST_SIGN_IN_METHOD_KEY } from "../sign-in-method"

const { LoginPage } = await import("./login")

const CODE_FIRST: SignInOptions = {
  codeSignInOffered: true,
  primaryMethod: "code",
  selfRegistrationOpen: true,
  passwordAtSignUp: false,
  nameAtSignUp: false,
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <LoginPage />
    </MemoryRouter>
  )
}

const passwordField = () => screen.queryByLabelText("auth.password")
const codeLink = () => screen.queryByRole("button", { name: "auth.signInWithEmailCode" })
const passwordLink = () =>
  screen.queryByRole("button", { name: "auth.signInWithPasswordInstead" })

function underReason(reason: SignInReason["reason"], method: SignInReason["method"]) {
  mocks.completion.returnTo = RESUME
  mocks.completion.reason = { reason, method }
}

describe("LoginPage under a sign-in reason (S23)", () => {
  beforeEach(() => {
    window.localStorage.clear()
    window.sessionStorage.clear()
    mocks.post.mockReset()
    mocks.flowProps = []
    mocks.completion.returnTo = null
    mocks.completion.reason = null
    mocks.branding.signInOptions = CODE_FIRST
  })

  it.each([
    ["mfa_step_up", "auth.signInReasonStepUpTitle", "auth.signInReasonStepUpBody"],
    ["mfa_enroll", "auth.signInReasonEnrollTitle", "auth.signInReasonEnrollBody"],
    [
      "mfa_reauthenticate",
      "auth.signInReasonReauthenticateTitle",
      "auth.signInReasonReauthenticateBody",
    ],
  ] as const)("says why for %s, first in the form's field group", (reason, title, body) => {
    underReason(reason, "password")
    const { container } = renderPage()

    const alert = screen.getByTestId("sign-in-reason")
    expect(alert).toHaveAttribute("role", "alert")
    expect(alert.querySelector('[data-slot="alert-title"]')).toHaveTextContent(title)
    expect(alert.querySelector('[data-slot="alert-description"]')).toHaveTextContent(body)
    const group = container.querySelector('form [data-slot="field-group"]')
    expect(group?.firstElementChild).toBe(alert)
  })

  it("says nothing without a reason", () => {
    mocks.completion.returnTo = RESUME
    renderPage()

    expect(screen.queryByTestId("sign-in-reason")).toBeNull()
  })

  it("puts the server's code before a remembered password", () => {
    window.localStorage.setItem(LAST_SIGN_IN_METHOD_KEY, "password")
    underReason("mfa_enroll", "code")
    renderPage()

    expect(passwordField()).toBeNull()
    expect(passwordLink()).toBeInTheDocument()
  })

  it("puts the server's password before a remembered code", () => {
    window.localStorage.setItem(LAST_SIGN_IN_METHOD_KEY, "code")
    underReason("mfa_step_up", "password")
    renderPage()

    expect(passwordField()).toBeInTheDocument()
    expect(codeLink()).toBeInTheDocument()
  })

  it("lets the person's click beat the server's word", async () => {
    underReason("mfa_step_up", "password")
    renderPage()

    await userEvent.setup().click(codeLink()!)

    expect(passwordField()).toBeNull()
    expect(passwordLink()).toBeInTheDocument()
  })

  it("ignores the server's code where no code can be sent", () => {
    mocks.branding.signInOptions = { ...CODE_FIRST, codeSignInOffered: false }
    underReason("mfa_enroll", "code")
    renderPage()

    expect(passwordField()).toBeInTheDocument()
    expect(codeLink()).toBeNull()
  })

  it("asks a code sign-in for full proof, though nothing else asked for it", async () => {
    // A password account that takes the code link proves the password too, so it
    // does not come back to the application with the same requirement unmet.
    underReason("mfa_step_up", "password")
    mocks.post.mockResolvedValue({
      data: {
        pendingId: "handle-1",
        maskedEmail: "j***@one.example",
        expiresAt: "2099-09-10T10:05:00.000Z",
      },
      response: new Response(null, { status: 200 }),
    })
    renderPage()
    const user = userEvent.setup()

    await user.click(codeLink()!)
    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.click(screen.getByRole("button", { name: "auth.continueWithEmail" }))

    expect(await screen.findByText("code flow")).toBeInTheDocument()
    expect(mocks.flowProps.at(-1)).toMatchObject({ requireFullProof: true })
    expect(window.sessionStorage.length).toBe(0)
  })

  it("asks no full proof without a reason", async () => {
    mocks.completion.returnTo = RESUME
    mocks.post.mockResolvedValue({
      data: {
        pendingId: "handle-1",
        maskedEmail: "j***@one.example",
        expiresAt: "2099-09-10T10:05:00.000Z",
      },
      response: new Response(null, { status: 200 }),
    })
    renderPage()
    const user = userEvent.setup()

    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.click(screen.getByRole("button", { name: "auth.continueWithEmail" }))

    expect(await screen.findByText("code flow")).toBeInTheDocument()
    expect(mocks.flowProps.at(-1)).toMatchObject({ requireFullProof: false })
  })

  it("carries the request and the reason to the password reset", () => {
    underReason("mfa_step_up", "password")
    renderPage()

    expect(screen.getByRole("link", { name: "auth.forgotPassword" })).toHaveAttribute(
      "href",
      `/forgot-password?client_id=app&auth_request=${HANDLE}&reason=mfa_step_up&method=password`
    )
  })
})
