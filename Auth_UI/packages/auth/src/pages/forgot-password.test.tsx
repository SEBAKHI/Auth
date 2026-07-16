import { describe, expect, it, vi, beforeEach } from "vitest"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"

import "@astoom/i18n"

const postMock = vi.fn()
vi.mock("@astoom/api/client", () => ({
  api: { POST: (...args: unknown[]) => postMock(...args) },
}))

// The real AuthLayout is only chrome here, but it drags in the theme/language
// toggles and their whole provider stack. Stub it down to the parts under test.
vi.mock("@astoom/ui/auth-layout", () => ({
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

const { ForgotPasswordPage } = await import("./forgot-password")

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/forgot-password"]}>
      <ForgotPasswordPage />
    </MemoryRouter>
  )
}

function resolveWith(expiresInMinutes: number | null) {
  postMock.mockResolvedValue({
    data: {
      maskedEmail: "j***n@example.com",
      expiresAt:
        expiresInMinutes === null
          ? null
          : new Date(Date.now() + expiresInMinutes * 60_000).toISOString(),
    },
    error: undefined,
  })
}

async function submitEmail(email = "john@example.com") {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText(/email/i), email)
  await user.click(screen.getByRole("button", { name: /send reset link/i }))
  return user
}

describe("ForgotPasswordPage", () => {
  beforeEach(() => {
    postMock.mockReset()
  })

  it("reports that the link was sent instead of asking for a code", async () => {
    // Regression: the page used to navigate straight to the reset form, whose
    // required token field the user had no way of filling - a dead end. The
    // link in the email is the only way onward.
    resolveWith(30)
    renderPage()

    await submitEmail()

    expect(await screen.findByText(/check your email/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/reset code/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/new password/i)).not.toBeInTheDocument()
  })

  it("sends only the email address to the API", async () => {
    resolveWith(30)
    renderPage()

    await submitEmail()

    await screen.findByText(/check your email/i)
    expect(postMock).toHaveBeenCalledWith("/api/v1/Auth/forgot-password", {
      body: { email: "john@example.com" },
    })
  })

  it("shows the masked address the API returned, never the typed one", async () => {
    // The API masks the submitted address even when no such account exists, so
    // echoing its value keeps the response identical either way.
    resolveWith(30)
    renderPage()

    await submitEmail("john@example.com")

    expect(await screen.findByText(/j\*\*\*n@example\.com/)).toBeInTheDocument()
    expect(screen.queryByText(/\bjohn@example\.com\b/)).not.toBeInTheDocument()
  })

  it("counts down to the expiry the API reported", async () => {
    resolveWith(30)
    renderPage()

    await submitEmail()

    expect(await screen.findByText(/29:5\d|30:00/)).toBeInTheDocument()
  })

  it("returns to the form so the user can request another link", async () => {
    resolveWith(30)
    renderPage()

    const user = await submitEmail()
    await screen.findByText(/check your email/i)

    await user.click(screen.getByRole("button", { name: /send again/i }))

    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  })

  it("keeps the user on the form when the request fails", async () => {
    postMock.mockResolvedValue({ data: undefined, error: { title: "boom" } })
    renderPage()

    await submitEmail()

    expect(screen.queryByText(/check your email/i)).not.toBeInTheDocument()
    expect(screen.getByLabelText(/email/i)).toBeInTheDocument()
  })
})
