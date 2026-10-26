import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { post, toast, branding } = vi.hoisted(() => ({
  post: vi.fn(),
  toast: { error: vi.fn(), success: vi.fn(), info: vi.fn() },
  branding: {
    signInOptions: null as null | {
      codeSignInOffered: boolean
      primaryMethod: "password" | "code"
      selfRegistrationOpen: boolean
      passwordAtSignUp: boolean
      nameAtSignUp: boolean
    },
  },
}))

vi.mock("sonner", () => ({ toast }))
// The deployment's public sign-in options, as the branding answer carries them.
vi.mock("@authsystem/ui/branding", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/ui/branding")>()),
  useSignInOptions: () => branding.signInOptions,
}))
vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))
vi.mock("@authsystem/api/env", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/api/env")>()),
  privacyPolicyUrl: () => "/privacy/en",
}))
// A real button, so the test can ask where it sits relative to the form.
vi.mock("@authsystem/auth/external/external-providers", () => ({
  ExternalProviders: () => <button type="button">Continue with Google</button>,
}))
// Chrome only: the real layout drags in the theme and language toggles and
// their provider stack. Keep the parts the form renders into.
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    children,
    footer,
    pageFooter,
  }: {
    title: string
    children: React.ReactNode
    footer?: React.ReactNode
    pageFooter?: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {children}
      {footer}
      {pageFooter}
    </div>
  ),
}))

// The application header is flow-branding.test.tsx's concern; this page's own
// contract is tested here without the branding providers.
vi.mock("@authsystem/auth/flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: null,
    layout: { appName: null, appLogoUrl: null, securedBy: null },
  }),
}))

import { RegisterPage } from "./register"
import {
  clearRegistrationFlow,
  readPendingRegistration,
} from "./registration-flow"

const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"
const RETURN_TO_QUERY = `?returnTo=${encodeURIComponent(AUTHORIZE)}`

const STARTED = {
  pendingId: "handle-1",
  maskedEmail: "j***@one.example",
  expiresAt: "2026-09-10T10:05:00.000Z",
}

/** Reports where the router ended up. */
function Landing() {
  const location = useLocation()
  return (
    <div data-testid="landing">
      {location.pathname}
      {location.search}
    </div>
  )
}

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
        <Route path="*" element={<Landing />} />
      </Routes>
    </MemoryRouter>
  )
}

async function submitAddress(
  user: ReturnType<typeof userEvent.setup>,
  email: string
) {
  await user.type(screen.getByLabelText(/^email$/i), email)
  await user.click(screen.getByRole("button", { name: /send code/i }))
}

const OPEN = {
  codeSignInOffered: true,
  primaryMethod: "password" as const,
  selfRegistrationOpen: true,
  passwordAtSignUp: true,
  nameAtSignUp: true,
}

describe("RegisterPage", () => {
  beforeEach(() => {
    post.mockReset()
    toast.error.mockReset()
    clearRegistrationFlow()
    branding.signInOptions = null
  })

  // REVIEW 110 F10: a closed sign-up is said before an address is typed.
  it("says sign-up is closed, points to the sign-in, and starts nothing", async () => {
    branding.signInOptions = { ...OPEN, selfRegistrationOpen: false }
    const user = userEvent.setup()
    renderAt(`/register${RETURN_TO_QUERY}`)

    expect(screen.getByRole("heading", { name: "Sign-up is closed" })).toBeVisible()
    expect(screen.getByTestId("register-unavailable")).toHaveTextContent(
      "New accounts can't be created here with an email address."
    )
    expect(screen.queryByLabelText(/^email$/i)).not.toBeInTheDocument()

    expect(
      screen.getByRole("button", { name: /google/i }),
      "a provider sign-up has its own switch and needs no email"
    ).toBeVisible()

    await user.click(screen.getByRole("link", { name: /sign in/i }))
    expect(screen.getByTestId("landing")).toHaveTextContent(`/login${RETURN_TO_QUERY}`)
    expect(post).not.toHaveBeenCalled()
  })

  // REVIEW 110 F1 (c): with email off no code can be sent, so no sign-up starts;
  // the page says so once the server refuses, never from a payload that is silent.
  it("says a new account needs email once the server refuses for that reason", async () => {
    post.mockResolvedValue({
      error: { status: 403, code: "User.EmailCodeSignInUnavailable" },
    })
    const user = userEvent.setup()
    renderAt("/register")

    await submitAddress(user, "jane@one.example")

    expect(await screen.findByTestId("register-unavailable")).toHaveTextContent(
      "this server isn't sending email"
    )
    expect(screen.queryByLabelText(/^email$/i)).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: /sign in/i })).toBeVisible()
    expect(screen.getByRole("button", { name: /google/i })).toBeVisible()
    expect(toast.error).not.toHaveBeenCalled()
    expect(readPendingRegistration()).toBeNull()
  })

  it("keeps the form for options that say nothing of email", () => {
    branding.signInOptions = { ...OPEN, codeSignInOffered: false }
    renderAt("/register")

    expect(screen.queryByTestId("register-unavailable")).not.toBeInTheDocument()
    expect(screen.getByLabelText(/^email$/i)).toBeVisible()
  })

  it("shows the form while sign-up is open and email is on", () => {
    branding.signInOptions = OPEN
    renderAt("/register")

    expect(screen.queryByTestId("register-unavailable")).not.toBeInTheDocument()
    expect(screen.getByLabelText(/^email$/i)).toBeVisible()
  })

  it("asks for an email and nothing else", () => {
    renderAt("/register")

    expect(screen.getByLabelText(/^email$/i)).toHaveAttribute("type", "email")
    expect(screen.getAllByRole("textbox")).toHaveLength(1)
    expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/name/i)).not.toBeInTheDocument()
    expect(screen.getByText(/6-digit code/i)).toBeVisible()
  })

  it("keeps the provider buttons outside the form", () => {
    renderAt("/register")

    const google = screen.getByRole("button", { name: /google/i })
    expect(google.closest("form")).toBeNull()
    expect(screen.getByRole("button", { name: /send code/i }).closest("form")).not.toBeNull()
  })

  it("sends the address and the language, and moves on to the code with the identity stored", async () => {
    post.mockResolvedValue({ data: STARTED })
    const user = userEvent.setup()
    renderAt("/register")

    await submitAddress(user, "jane@one.example")

    await waitFor(() =>
      expect(screen.getByTestId("landing")).toHaveTextContent("/register/verify")
    )
    // The sign-in page's start (OI-215): one request for both doors.
    expect(post).toHaveBeenCalledWith("/api/v1/Auth/email-code/start", {
      body: { email: "jane@one.example", preferredLanguage: "en" },
    })
    expect(readPendingRegistration()).toEqual({
      ...STARTED,
      email: "jane@one.example",
    })
  })

  it("says the same thing for an address that already has an account", async () => {
    // The server's answer is byte-identical for a free and a taken address;
    // this screen has no branch that could tell them apart, so the two
    // journeys must end in the same place with the same stored shape.
    const journeys: Array<{ landing: string; stored: string }> = []
    for (const email of ["free@one.example", "taken@one.example"]) {
      post.mockReset()
      post.mockResolvedValue({ data: STARTED })
      clearRegistrationFlow()
      const user = userEvent.setup()
      const view = renderAt("/register")

      await submitAddress(user, email)
      await waitFor(() =>
        expect(screen.getByTestId("landing")).toHaveTextContent("/register/verify")
      )

      const stored = readPendingRegistration()!
      journeys.push({
        landing: screen.getByTestId("landing").textContent ?? "",
        stored: JSON.stringify({ ...stored, email: "<address>" }),
      })
      view.unmount()
    }

    expect(journeys[0]).toEqual(journeys[1])
  })

  it("carries a pending authorize request to the code screen and the sign-in link", async () => {
    post.mockResolvedValue({ data: STARTED })
    const user = userEvent.setup()
    renderAt(`/register${RETURN_TO_QUERY}`)

    expect(screen.getByRole("link", { name: /sign in/i })).toHaveAttribute(
      "href",
      `/login${RETURN_TO_QUERY}`
    )

    await submitAddress(user, "jane@one.example")

    await waitFor(() =>
      expect(screen.getByTestId("landing")).toHaveTextContent(
        `/register/verify${RETURN_TO_QUERY}`
      )
    )
  })

  it("stays put and stores nothing when the server refuses", async () => {
    post.mockResolvedValue({
      error: {
        status: 403,
        code: "User.SelfRegistrationClosed",
        detail: "Sign-up is closed.",
      },
    })
    const user = userEvent.setup()
    renderAt("/register")

    await submitAddress(user, "jane@one.example")

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(screen.queryByTestId("landing")).not.toBeInTheDocument()
    expect(readPendingRegistration()).toBeNull()
    // Said out loud: a button that merely comes back to rest is a silent failure.
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Sign-up is closed."))
  })

  it("does not call the server for an address that is not one", async () => {
    const user = userEvent.setup()
    renderAt("/register")

    // Past the browser's own check (it accepts "a@b") and short of the page's.
    await submitAddress(user, "jane@one")

    expect(await screen.findByText(/valid email/i)).toBeVisible()
    expect(post).not.toHaveBeenCalled()
  })
})
