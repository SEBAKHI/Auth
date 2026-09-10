import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

const { post } = vi.hoisted(() => ({ post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    subtitle,
    children,
    footer,
  }: {
    title: string
    subtitle?: string
    children: React.ReactNode
    footer?: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {subtitle ? <p>{subtitle}</p> : null}
      {children}
      {footer}
    </div>
  ),
}))
// The slot geometry is the shared component's own concern; here a plain input
// stands in so the page's contract (value, completion, disabled) is what is
// under test. The constants stay real.
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
      disabled,
    }: {
      value: string
      onChange: (value: string) => void
      onComplete?: (value: string) => void
      label: string
      disabled?: boolean
    }) => (
      <input
        aria-label={label}
        value={value}
        disabled={disabled}
        onChange={(event) => {
          const next = event.target.value
          onChange(next)
          if (next.length === actual.OTP_CODE_LENGTH) onComplete?.(next)
        }}
      />
    ),
  }
})

import { RESEND_COOLDOWN_MS } from "@authsystem/ui/common/otp-input"

import { RegisterVerifyPage } from "./register-verify"
import {
  clearRegistrationFlow,
  getVerifiedCode,
  readPendingRegistration,
  savePendingRegistration,
} from "./registration-flow"

const NOW = new Date("2026-09-10T10:00:00.000Z")
const PENDING = {
  pendingId: "handle-1",
  email: "jane@one.example",
  maskedEmail: "j***@one.example",
  expiresAt: "2026-09-10T10:05:00.000Z",
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

function renderAt(entry: string | { pathname: string; state: unknown }) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/register/verify" element={<RegisterVerifyPage />} />
        <Route path="*" element={<Landing />} />
      </Routes>
    </MemoryRouter>
  )
}

function setup() {
  return userEvent.setup()
}

/**
 * Moves the wall clock only. The countdown re-reads Date.now() on a real
 * 250 ms interval, so the page catches up on its own within a waitFor; faking
 * the timers as well would stall that interval and Testing Library's polling
 * with it.
 */
function clockAt(offsetMs: number) {
  vi.setSystemTime(new Date(NOW.getTime() + offsetMs))
}

const codeField = () => screen.getByLabelText(/verification code/i)
const newCodeButton = () =>
  screen.getByRole("button", { name: /new code|send again/i })

describe("RegisterVerifyPage", () => {
  beforeEach(() => {
    vi.setSystemTime(NOW)
    post.mockReset()
    clearRegistrationFlow()
    savePendingRegistration(PENDING)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it("never requests a code on mount", () => {
    renderAt("/register/verify")

    expect(screen.getByText(/j\*\*\*@one\.example/)).toBeVisible()
    expect(screen.getByText(/code expires in 05:00/i)).toBeVisible()
    expect(post).not.toHaveBeenCalled()
  })

  it("goes back to the start when there is no pending sign-up", () => {
    clearRegistrationFlow()

    renderAt(`/register/verify${RETURN_TO_QUERY}`)

    expect(screen.getByTestId("landing")).toHaveTextContent(
      `/register${RETURN_TO_QUERY}`
    )
  })

  it("sends the handle and the code, keeps the code in memory, and moves on", async () => {
    post.mockResolvedValue({ data: undefined, error: undefined })
    const user = setup()
    renderAt(`/register/verify${RETURN_TO_QUERY}`)

    await user.type(codeField(), "123456")

    await waitFor(() =>
      expect(screen.getByTestId("landing")).toHaveTextContent(
        `/register/complete${RETURN_TO_QUERY}`
      )
    )
    expect(post).toHaveBeenCalledWith("/api/v1/Auth/registration/verify", {
      body: { pendingId: "handle-1", otp: "123456" },
    })
    expect(getVerifiedCode()).toBe("123456")
    // The proof went nowhere near storage, nor into the history entry the
    // router writes (Landing prints the state), and the identity is untouched.
    expect(JSON.stringify(window.sessionStorage)).not.toContain("123456")
    expect(screen.getByTestId("landing")).toHaveTextContent(
      `/register/complete${RETURN_TO_QUERY}|null`
    )
    expect(readPendingRegistration()).toEqual(PENDING)
  })

  it("still sends an expired code to the server, which decides", async () => {
    // The local clock only reports. A fast browser clock would otherwise
    // refuse a code the server still accepts, and nothing but waiting for a
    // new code could get past that refusal.
    post.mockResolvedValue({ data: undefined, error: undefined })
    const user = setup()
    renderAt("/register/verify")
    clockAt(5 * 60_000 + 500)
    await waitFor(() => expect(screen.getByText(/code has expired/i)).toBeVisible())

    await user.type(codeField(), "123456")

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith("/api/v1/Auth/registration/verify", {
        body: { pendingId: "handle-1", otp: "123456" },
      })
    )
    expect(screen.getByTestId("landing")).toHaveTextContent("/register/complete")
  })

  it("shows a wrong code inline, clears the field, and keeps the code screen", async () => {
    post.mockResolvedValue({
      error: {
        status: 400,
        title: "EmailVerification.InvalidOrExpiredOtp",
        detail: "The code is wrong or has expired.",
      },
    })
    const user = setup()
    renderAt("/register/verify")

    await user.type(codeField(), "000000")

    expect(
      await screen.findByText("The code is wrong or has expired.")
    ).toBeVisible()
    expect(codeField()).toHaveValue("")
    expect(codeField()).toBeEnabled()
    expect(getVerifiedCode()).toBeNull()
    expect(screen.queryByTestId("landing")).not.toBeInTheDocument()
    // Still live: a wrong guess does not open the new-code button.
    expect(newCodeButton()).toBeDisabled()
  })

  it("holds the new-code button until the code expires and the cooldown passes", async () => {
    post.mockResolvedValue({
      error: { status: 429, title: "RateLimit.Exceeded", detail: "Slow down." },
    })
    const user = setup()
    renderAt("/register/verify")

    expect(newCodeButton()).toBeDisabled()

    // Past the expiry: the button opens, and says so.
    clockAt(5 * 60_000 + 500)
    await waitFor(() => expect(newCodeButton()).toBeEnabled())
    expect(screen.getByText(/code has expired/i)).toBeVisible()

    // A refused request starts the cooldown all the same, so a 429 cannot be
    // answered with a second 429.
    await user.click(newCodeButton())
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(
      await screen.findByRole("button", { name: /send again in 00:59|send again in 01:00/i })
    ).toBeDisabled()
    // And the refusal is said, not just a greyed button for a minute.
    expect(screen.getByRole("alert")).toHaveTextContent(
      i18n.t("errors.feedback.rateLimit")
    )

    clockAt(5 * 60_000 + 500 + RESEND_COOLDOWN_MS + 500)
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /send a new code/i })).toBeEnabled()
    )
  })

  it("asks for a new code with the stored address and adopts the rotated identity", async () => {
    post.mockResolvedValue({
      data: {
        pendingId: "handle-1",
        maskedEmail: "j***@one.example",
        expiresAt: "2026-09-10T10:11:00.000Z",
      },
    })
    const user = setup()
    renderAt("/register/verify")
    clockAt(5 * 60_000 + 500)
    await waitFor(() => expect(newCodeButton()).toBeEnabled())

    await user.click(newCodeButton())

    expect(post).toHaveBeenCalledWith("/api/v1/Auth/registration/start", {
      body: { email: "jane@one.example", preferredLanguage: "en" },
    })
    // The new code is live, so the button closes again and the clock restarts.
    await waitFor(() => expect(newCodeButton()).toBeDisabled())
    // Six minutes from a clock that has moved half a second past the old expiry.
    expect(screen.getByText(/code expires in 0(6:00|5:5\d)/i)).toBeVisible()
    expect(readPendingRegistration()?.expiresAt).toBe("2026-09-10T10:11:00.000Z")
  })

  it("opens the new-code button at once when the attempts are spent", async () => {
    post.mockResolvedValue({
      error: {
        status: 400,
        title: "EmailVerification.TooManyAttempts",
        detail: "Too many attempts. Request a new code.",
      },
    })
    const user = setup()
    renderAt("/register/verify")

    await user.type(codeField(), "000000")

    expect(
      await screen.findByText("Too many attempts. Request a new code.")
    ).toBeVisible()
    expect(newCodeButton()).toBeEnabled()
    // Spent is the server's word, so this one closes the verify button.
    expect(screen.getByRole("button", { name: /^verify$/i })).toBeDisabled()
  })

  it("shows the notice the completion screen sent back", () => {
    renderAt({
      pathname: "/register/verify",
      state: { notice: "The code is wrong or has expired." },
    })

    expect(screen.getByText("The code is wrong or has expired.")).toBeVisible()
    expect(post).not.toHaveBeenCalled()
  })

  it("lets the person start over with another address", async () => {
    const user = setup()
    renderAt(`/register/verify${RETURN_TO_QUERY}`)

    await user.click(screen.getByRole("button", { name: /different email/i }))

    expect(screen.getByTestId("landing")).toHaveTextContent(
      `/register${RETURN_TO_QUERY}`
    )
    expect(readPendingRegistration()).toBeNull()
  })
})
