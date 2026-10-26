import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type * as React from "react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

/**
 * The sign-up's code screen is the sign-in page's code flow (OI-215): these
 * tests hold what the sign-up adds to it — the identity kept across a reload,
 * the flow forgotten once it ends, the start and the address change — and the
 * old third screen's address sending a reload back to the code. The flow's own
 * steps are email-code-flow.test.tsx's.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  signInWithEmailCode: vi.fn(),
  completeRegistration: vi.fn(),
  recoverWithEmailCode: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => mocks.post(...args) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    signInWithEmailCode: mocks.signInWithEmailCode,
    completeRegistration: mocks.completeRegistration,
    recoverWithEmailCode: mocks.recoverWithEmailCode,
  }),
}))
vi.mock("@authsystem/auth/login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: null,
    complete: mocks.complete,
    challenge: mocks.challenge,
  }),
}))
vi.mock("@authsystem/api/password-policy", () => ({
  usePasswordPolicy: () => ({ policy: undefined }),
}))
vi.mock("@authsystem/auth/flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: null,
    layout: { appName: null, appLogoUrl: null, securedBy: null },
  }),
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    subtitle,
    children,
  }: {
    title: string
    subtitle?: string
    children: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {subtitle ? <p>{subtitle}</p> : null}
      {children}
    </div>
  ),
}))
vi.mock("@authsystem/ui/common/otp-input", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@authsystem/ui/common/otp-input")>()
  return {
    ...actual,
    OtpInput: ({
      value,
      onChange,
      onComplete,
      label,
    }: {
      value: string
      onChange: (value: string) => void
      onComplete?: (value: string) => void
      label: string
    }) => (
      <input
        aria-label={label}
        value={value}
        onChange={(event) => {
          onChange(event.target.value)
          if (event.target.value.length === actual.OTP_CODE_LENGTH) {
            onComplete?.(event.target.value)
          }
        }}
      />
    ),
  }
})

import { RegisterCompleteRedirect, RegisterVerifyPage } from "./register-verify"
import {
  PENDING_REGISTRATION_STORAGE_KEY,
  readPendingRegistration,
  savePendingRegistration,
} from "./registration-flow"

const PENDING = {
  pendingId: "handle-1",
  email: "jane@one.example",
  maskedEmail: "j***@one.example",
  expiresAt: "2099-09-10T10:05:00.000Z",
}
const RETURN_TO_QUERY = `?returnTo=${encodeURIComponent(
  "https://api.example.com/api/v1/auth/authorize?client_id=app"
)}`

function Landing() {
  const location = useLocation()
  return (
    <div data-testid="landing">
      {location.pathname}
      {location.search}
      {"|"}
      {JSON.stringify(location.state ?? null)}
    </div>
  )
}

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/register/verify" element={<RegisterVerifyPage />} />
        <Route path="/register/complete" element={<RegisterCompleteRedirect />} />
        <Route path="*" element={<Landing />} />
      </Routes>
    </MemoryRouter>
  )
}

const codeField = () => screen.getByLabelText(/verification code/i)

function answerVerify(body: object) {
  mocks.post.mockImplementation(async (path: string) =>
    path === "/api/v1/Auth/email-code/verify"
      ? { data: body, response: new Response(null, { status: 200 }) }
      : { error: { status: 500 } }
  )
}

describe("RegisterVerifyPage", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    for (const mock of Object.values(mocks)) mock.mockReset()
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it("sends a visitor without a pending sign-up to the start, with the pending request", async () => {
    renderAt(`/register/verify${RETURN_TO_QUERY}`)

    expect(await screen.findByTestId("landing")).toHaveTextContent(
      `/register${RETURN_TO_QUERY}`
    )
    expect(mocks.post).not.toHaveBeenCalled()
  })

  it("asks for nothing on arrival: the code is already on its way", () => {
    savePendingRegistration(PENDING)
    renderAt("/register/verify")

    expect(codeField()).toBeInTheDocument()
    expect(
      screen.getByText(/We sent a message to jane@one\.example/)
    ).toBeInTheDocument()
    expect(mocks.post).not.toHaveBeenCalled()
  })

  it("signs in an address that already has an account, and forgets the sign-up", async () => {
    savePendingRegistration(PENDING)
    answerVerify({ step: "signIn" })
    mocks.signInWithEmailCode.mockResolvedValue({
      status: "authenticated",
      requiresPasswordChange: false,
    })
    renderAt("/register/verify")

    await userEvent.setup().type(codeField(), "123456")

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    expect(mocks.signInWithEmailCode).toHaveBeenCalledWith({
      pendingId: "handle-1",
      otp: "123456",
      requireFullProof: false,
      staySignedIn: false,
    })
    expect(readPendingRegistration()).toBeNull()
  })

  it("creates the account at once when the deployment asks for nothing", async () => {
    savePendingRegistration(PENDING)
    answerVerify({ step: "completeAccount", passwordRequired: false, nameRequired: false })
    mocks.completeRegistration.mockResolvedValue({
      status: "authenticated",
      requiresPasswordChange: false,
    })
    renderAt("/register/verify")

    await userEvent.setup().type(codeField(), "123456")

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    const input = mocks.completeRegistration.mock.calls[0][0]
    expect(input).toMatchObject({ pendingId: "handle-1", otp: "123456" })
    expect(input.password).toBeUndefined()
    expect(input.firstName).toBeUndefined()
    expect(window.sessionStorage.getItem(PENDING_REGISTRATION_STORAGE_KEY)).toBeNull()
  })

  it("asks a new account for the fields the server names", async () => {
    savePendingRegistration(PENDING)
    answerVerify({ step: "completeAccount", passwordRequired: true, nameRequired: false })
    renderAt("/register/verify")

    await userEvent.setup().type(codeField(), "123456")

    expect(await screen.findByLabelText(/^password$/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/first name/i)).toBeNull()
    expect(mocks.completeRegistration).not.toHaveBeenCalled()
    // The flow is not over: a reload comes back to the code.
    expect(readPendingRegistration()).toEqual(PENDING)
  })

  it("keeps a new code across a reload", async () => {
    savePendingRegistration(PENDING)
    const next = { ...PENDING, pendingId: "handle-2", expiresAt: "2099-09-10T10:10:00.000Z" }
    mocks.post.mockResolvedValue({
      data: { pendingId: "handle-2", maskedEmail: PENDING.maskedEmail, expiresAt: next.expiresAt },
      response: new Response(null, { status: 200 }),
    })
    vi.useFakeTimers({ toFake: ["Date"] })
    renderAt("/register/verify")
    vi.setSystemTime(Date.now() + 61_000)

    const resend = await screen.findByRole("button", { name: /send a new code/i })
    await userEvent.setup().click(resend)

    await waitFor(() => expect(readPendingRegistration()).toEqual(next))
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/Auth/email-code/start", {
      body: { email: "jane@one.example", preferredLanguage: expect.any(String) },
    })
  })

  it("starts over at the address, with the pending request", async () => {
    savePendingRegistration(PENDING)
    renderAt(`/register/verify${RETURN_TO_QUERY}`)

    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: /use a different email/i }))

    expect(await screen.findByTestId("landing")).toHaveTextContent(
      `/register${RETURN_TO_QUERY}`
    )
    expect(readPendingRegistration()).toBeNull()
  })

  it("sends the former third screen back to the code, with the pending request", async () => {
    renderAt(`/register/complete${RETURN_TO_QUERY}`)

    // No pending sign-up here, so the code screen in turn sends it to the start.
    expect(await screen.findByTestId("landing")).toHaveTextContent(
      `/register${RETURN_TO_QUERY}`
    )
  })
})
