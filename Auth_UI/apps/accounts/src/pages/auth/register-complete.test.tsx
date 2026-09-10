import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { get, auth, completion, toast } = vi.hoisted(() => ({
  get: vi.fn(),
  toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() },
  auth: {
    status: "unauthenticated" as "loading" | "authenticated" | "unauthenticated",
    completeRegistration: vi.fn(),
  },
  completion: {
    returnTo: null as string | null,
    complete: vi.fn(),
    challenge: vi.fn(),
  },
}))

vi.mock("sonner", () => ({ toast }))
vi.mock("@authsystem/api/client", () => ({
  api: { GET: (...args: unknown[]) => get(...args) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => auth,
}))
vi.mock("@authsystem/auth/login-completion", () => ({
  useLoginCompletion: () => completion,
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

import { RegisterCompletePage } from "./register-complete"
import {
  clearRegistrationFlow,
  getVerifiedCode,
  readPendingRegistration,
  savePendingRegistration,
  setVerifiedCode,
} from "./registration-flow"

const PENDING = {
  pendingId: "handle-1",
  email: "jane@one.example",
  maskedEmail: "j***@one.example",
  expiresAt: "2026-09-10T10:05:00.000Z",
}
const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"
const RETURN_TO_QUERY = `?returnTo=${encodeURIComponent(AUTHORIZE)}`
const POLICY = {
  minimumLength: 8,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireSpecialCharacter: true,
}
const SIGNED_IN = { status: "authenticated", requiresPasswordChange: false }

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
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/register/complete" element={<RegisterCompletePage />} />
          <Route path="*" element={<Landing />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/^first name$/i), "Jane")
  await user.type(screen.getByLabelText(/^last name$/i), "Doe")
  await user.type(screen.getByLabelText(/^password$/i), "NewPass1!")
  await user.click(screen.getByRole("button", { name: /create account/i }))
}

describe("RegisterCompletePage", () => {
  beforeEach(() => {
    get.mockReset()
    get.mockResolvedValue({ data: POLICY })
    auth.status = "unauthenticated"
    auth.completeRegistration.mockReset()
    toast.error.mockReset()
    toast.info.mockReset()
    toast.success.mockReset()
    completion.returnTo = null
    completion.complete.mockReset()
    completion.challenge.mockReset()
    clearRegistrationFlow()
    savePendingRegistration(PENDING)
    setVerifiedCode("123456")
  })

  it("returns to the code screen without a verified code in memory", () => {
    clearRegistrationFlow()
    savePendingRegistration(PENDING)

    renderAt(`/register/complete${RETURN_TO_QUERY}`)

    expect(screen.getByTestId("landing")).toHaveTextContent(
      `/register/verify${RETURN_TO_QUERY}`
    )
  })

  it("returns to the start without a pending sign-up", () => {
    clearRegistrationFlow()

    renderAt("/register/complete")

    expect(screen.getByTestId("landing")).toHaveTextContent("/register|")
  })

  it("shows the real address read-only with autocomplete=username, and a change link", async () => {
    const user = userEvent.setup()
    renderAt(`/register/complete${RETURN_TO_QUERY}`)

    const email = screen.getByLabelText(/^email$/i)
    expect(email).toHaveValue("jane@one.example")
    expect(email).toHaveAttribute("readonly")
    expect(email).toHaveAttribute("autocomplete", "username")
    expect(email).toHaveAttribute("tabindex", "-1")

    const change = screen.getByRole("link", { name: /change email/i })
    expect(change).toHaveAttribute("href", `/register${RETURN_TO_QUERY}`)
    await user.click(change)
    expect(readPendingRegistration()).toBeNull()
    expect(getVerifiedCode()).toBeNull()
  })

  it("has no confirm-password field", () => {
    renderAt("/register/complete")

    expect(screen.getByLabelText(/^password$/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/confirm/i)).not.toBeInTheDocument()
  })

  it("sends pendingId and otp, never an email field, then completes the sign-in", async () => {
    // The real context flips the status to authenticated before the promise
    // settles; the self-guard must let that pass while the submission is on.
    auth.completeRegistration.mockImplementation(async () => {
      auth.status = "authenticated"
      return SIGNED_IN
    })
    const user = userEvent.setup()
    renderAt("/register/complete")
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    await waitFor(() => expect(completion.complete).toHaveBeenCalledWith(SIGNED_IN))
    expect(auth.completeRegistration).toHaveBeenCalledTimes(1)
    const input = auth.completeRegistration.mock.calls[0][0] as Record<string, unknown>
    expect(input).toMatchObject({
      pendingId: "handle-1",
      otp: "123456",
      password: "NewPass1!",
      firstName: "Jane",
      lastName: "Doe",
    })
    expect(input).not.toHaveProperty("email")
    expect(Object.keys(input).sort()).toEqual(
      ["firstName", "lastName", "otp", "password", "pendingId", "timeZone"].sort()
    )
    // The flow is over: nothing of it remains, and the form is gone before
    // the navigation, so no password manager can pick the values off a
    // re-rendered field.
    expect(readPendingRegistration()).toBeNull()
    expect(getVerifiedCode()).toBeNull()
    expect(screen.queryByLabelText(/^password$/i)).not.toBeInTheDocument()
    expect(toast.success).toHaveBeenCalledWith("Your account is ready.")
    // Not bounced by the self-guard on the way out.
    expect(screen.queryByTestId("landing")).not.toBeInTheDocument()
  })

  it("a code error on the password screen returns to the code screen and does not resubmit", async () => {
    auth.completeRegistration.mockRejectedValue({
      status: 400,
      title: "EmailVerification.InvalidOrExpiredOtp",
      detail: "The code is wrong or has expired.",
    })
    const user = userEvent.setup()
    renderAt(`/register/complete${RETURN_TO_QUERY}`)
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    await waitFor(() =>
      expect(screen.getByTestId("landing")).toHaveTextContent(
        `/register/verify${RETURN_TO_QUERY}`
      )
    )
    expect(screen.getByTestId("landing")).toHaveTextContent(
      '"notice":"The code is wrong or has expired."'
    )
    expect(auth.completeRegistration).toHaveBeenCalledTimes(1)
    expect(completion.complete).not.toHaveBeenCalled()
    // The proof is withdrawn, the identity kept: the same sign-up continues.
    expect(getVerifiedCode()).toBeNull()
    expect(readPendingRegistration()).toEqual(PENDING)
  })

  it.each([
    ["User.DuplicateEmail", 409],
    ["User.AccountCreatedSignInRequired", 409],
  ])("sends an existing account (%s) to sign in with the address prefilled", async (code, status) => {
    auth.completeRegistration.mockRejectedValue({
      status,
      title: code,
      detail: "Please sign in.",
    })
    const user = userEvent.setup()
    renderAt(`/register/complete${RETURN_TO_QUERY}`)
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    await waitFor(() =>
      expect(screen.getByTestId("landing")).toHaveTextContent(
        `/login${RETURN_TO_QUERY}`
      )
    )
    expect(screen.getByTestId("landing")).toHaveTextContent(
      '"email":"jane@one.example"'
    )
    expect(readPendingRegistration()).toBeNull()
    expect(getVerifiedCode()).toBeNull()
    expect(toast.info).toHaveBeenCalledWith("Please sign in.")
  })

  it("says why when the server refuses for a reason no field owns", async () => {
    auth.completeRegistration.mockRejectedValue({
      status: 403,
      title: "User.SelfRegistrationClosed",
      detail: "Sign-up is closed.",
    })
    const user = userEvent.setup()
    renderAt("/register/complete")
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Sign-up is closed."))
    expect(screen.queryByTestId("landing")).not.toBeInTheDocument()
    expect(getVerifiedCode()).toBe("123456")
  })

  it("puts every reason the server refuses the password under the field", async () => {
    auth.completeRegistration.mockRejectedValue({
      status: 400,
      title: "Password.CommonPattern",
      detail: "Password contains a common pattern that is easy to guess.",
      errors: [
        {
          code: "Password.CommonPattern",
          description: "Password contains a common pattern that is easy to guess.",
        },
        {
          code: "Password.TooShort",
          description: "Password must be at least 12 characters long.",
        },
      ],
    })
    const user = userEvent.setup()
    renderAt("/register/complete")
    await screen.findByRole("list", { name: /password requirements/i })

    await fillAndSubmit(user)

    expect(
      await screen.findByText(
        "Password contains a common pattern that is easy to guess."
      )
    ).toBeVisible()
    expect(
      screen.getByText("Password must be at least 12 characters long.")
    ).toBeVisible()
    expect(screen.queryByTestId("landing")).not.toBeInTheDocument()
    // The proof survives a refused password: only the code errors withdraw it.
    expect(getVerifiedCode()).toBe("123456")
  })

  it("a signed-in visitor is bounced from the completion screen unless a returnTo is pending", () => {
    auth.status = "authenticated"

    const bounced = renderAt("/register/complete")
    expect(screen.getByTestId("landing")).toHaveTextContent("/|")
    bounced.unmount()

    completion.returnTo = AUTHORIZE
    renderAt(`/register/complete${RETURN_TO_QUERY}`)
    expect(screen.getByLabelText(/^first name$/i)).toBeInTheDocument()
    expect(screen.queryByTestId("landing")).not.toBeInTheDocument()
  })
})
