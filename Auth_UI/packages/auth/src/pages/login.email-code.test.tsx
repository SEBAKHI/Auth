import type * as React from "react"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import type { SignInOptions } from "@authsystem/ui/branding"

/**
 * OI-215 on the sign-in page. Before the code, only what the visitor already
 * knows decides the page: the deployment's options, the method that last
 * worked on this browser, and their own click (D-215-2, D-215-4, D-215-6).
 */

const mocks = vi.hoisted(() => ({
  login: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  post: vi.fn(),
  toastError: vi.fn(),
  flowProps: [] as Array<Record<string, unknown>>,
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
  useAuth: () => ({ login: mocks.login }),
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: null,
    complete: mocks.complete,
    challenge: mocks.challenge,
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
  toast: { error: mocks.toastError, success: vi.fn() },
}))
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
// The code screens are email-code-flow.test.tsx's: here, what the page hands them.
vi.mock("./email-code-flow", () => ({
  EmailCodeFlow: (props: Record<string, unknown>) => {
    mocks.flowProps.push(props)
    return (
      <button
        type="button"
        onClick={() => (props.onPasswordRequired as (email: string) => void)(
          "jane@one.example"
        )}
      >
        code flow
      </button>
    )
  },
}))

import {
  FULL_PROOF_KEY,
  LAST_SIGN_IN_METHOD_KEY,
} from "../sign-in-method"

const { LoginPage } = await import("./login")

const CODE_FIRST: SignInOptions = {
  codeSignInOffered: true,
  primaryMethod: "code",
  selfRegistrationOpen: true,
  passwordAtSignUp: false,
  nameAtSignUp: false,
}
const PASSWORD_FIRST: SignInOptions = {
  ...CODE_FIRST,
  primaryMethod: "password",
  passwordAtSignUp: true,
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <LoginPage footer={<a href="/register">sign up</a>} />
    </MemoryRouter>
  )
}

const passwordField = () => screen.queryByLabelText("auth.password")
const codeLink = () => screen.queryByRole("button", { name: "auth.signInWithEmailCode" })
const passwordLink = () =>
  screen.queryByRole("button", { name: "auth.signInWithPasswordInstead" })

describe("LoginPage: the code and the password", () => {
  beforeEach(() => {
    window.localStorage.clear()
    window.sessionStorage.clear()
    mocks.login.mockReset()
    mocks.complete.mockReset()
    mocks.challenge.mockReset()
    mocks.post.mockReset()
    mocks.toastError.mockReset()
    mocks.flowProps = []
    mocks.branding.signInOptions = CODE_FIRST
  })

  it("is today's page where the server publishes no code sign-in", () => {
    mocks.branding.signInOptions = undefined
    renderPage()

    expect(passwordField()).toBeInTheDocument()
    expect(codeLink()).toBeNull()
    expect(passwordLink()).toBeNull()
    expect(screen.getByRole("heading")).toHaveTextContent("auth.signInTitle")
  })

  it("puts the code first, with the password one click away", () => {
    renderPage()

    expect(passwordField()).toBeNull()
    expect(screen.getByRole("button", { name: "auth.continueWithEmail" })).toBeInTheDocument()
    expect(passwordLink()).toBeInTheDocument()
    expect(screen.getByRole("heading")).toHaveTextContent("auth.signInOrCreateTitle")
    // With the code first, the same form creates an account.
    expect(screen.queryByText("sign up")).toBeNull()
  })

  it("titles the code-first page as a sign-in only while sign-up is closed", () => {
    mocks.branding.signInOptions = { ...CODE_FIRST, selfRegistrationOpen: false }
    renderPage()

    expect(screen.getByRole("heading")).toHaveTextContent("auth.signInTitle")
  })

  it("puts the password first, with the code one click away", async () => {
    mocks.branding.signInOptions = PASSWORD_FIRST
    renderPage()

    expect(passwordField()).toBeInTheDocument()
    expect(screen.getByText("sign up")).toBeInTheDocument()
    await userEvent.setup().click(codeLink()!)

    expect(passwordField()).toBeNull()
    expect(passwordLink()).toBeInTheDocument()
  })

  it("opens with the method that last worked on this browser", () => {
    window.localStorage.setItem(LAST_SIGN_IN_METHOD_KEY, "password")
    renderPage()

    expect(passwordField()).toBeInTheDocument()
    expect(codeLink()).toBeInTheDocument()
  })

  it("ignores a remembered code where no code can be sent", () => {
    window.localStorage.setItem(LAST_SIGN_IN_METHOD_KEY, "code")
    mocks.branding.signInOptions = { ...PASSWORD_FIRST, codeSignInOffered: false }
    renderPage()

    expect(passwordField()).toBeInTheDocument()
    expect(codeLink()).toBeNull()
  })

  it("sends a code to the typed address, then hands the code screens the add-only reason", async () => {
    window.sessionStorage.setItem(FULL_PROOF_KEY, "1")
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

    expect(await screen.findByRole("button", { name: "code flow" })).toBeInTheDocument()
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/Auth/email-code/start", {
      body: { email: "jane@one.example", preferredLanguage: "en" },
    })
    expect(mocks.flowProps.at(-1)).toMatchObject({
      pending: { pendingId: "handle-1", email: "jane@one.example" },
      requireFullProof: true,
      staySignedIn: false,
    })
  })

  it("asks only the password when the code sign-in needs it, and remembers the password", async () => {
    mocks.post.mockResolvedValue({
      data: {
        pendingId: "handle-1",
        maskedEmail: "j***@one.example",
        expiresAt: "2099-09-10T10:05:00.000Z",
      },
      response: new Response(null, { status: 200 }),
    })
    mocks.login.mockResolvedValue({ status: "authenticated", requiresPasswordChange: false })
    renderPage()
    const user = userEvent.setup()
    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.click(screen.getByRole("button", { name: "auth.continueWithEmail" }))

    await user.click(await screen.findByRole("button", { name: "code flow" }))

    expect(screen.getByRole("heading")).toHaveTextContent("auth.passwordToFinishTitle")
    expect(screen.getByLabelText("auth.email")).toHaveAttribute("readonly")
    expect(codeLink()).toBeNull()
    await user.type(passwordField()!, "pw")
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    expect(mocks.login).toHaveBeenCalledWith("jane@one.example", "pw", false)
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBe("password")
  })

  it("says one thing for every wrong pair, and where an account without a password goes", async () => {
    mocks.branding.signInOptions = PASSWORD_FIRST
    mocks.login.mockRejectedValue({
      status: 400,
      code: "User.InvalidCredentials",
      detail: "Invalid email or password",
    })
    renderPage()
    const user = userEvent.setup()

    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.type(passwordField()!, "wrong")
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))

    await waitFor(() =>
      expect(mocks.toastError).toHaveBeenCalledWith("auth.invalidCredentialsOrCode")
    )
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBeNull()
  })

  it("opens password-first when another screen's code sign-in needs the password", () => {
    // The sign-up and the invitation send the address they just proved here;
    // the code first would cost a second email before the password step.
    render(
      <MemoryRouter
        initialEntries={[
          { pathname: "/login", state: { email: "jane@one.example", method: "password" } },
        ]}
      >
        <LoginPage />
      </MemoryRouter>
    )

    expect(passwordField()).toBeInTheDocument()
    expect(screen.getByLabelText("auth.email")).toHaveValue("jane@one.example")
    expect(codeLink()).toBeInTheDocument()
  })

  it("still requires the password where it is shown", async () => {
    mocks.branding.signInOptions = PASSWORD_FIRST
    renderPage()
    const user = userEvent.setup()

    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))

    expect(await screen.findByText("validation.required")).toBeInTheDocument()
    expect(mocks.login).not.toHaveBeenCalled()
  })
})
