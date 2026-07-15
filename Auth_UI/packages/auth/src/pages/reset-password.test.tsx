import { describe, expect, it, vi, beforeEach } from "vitest"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"

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

const { ResetPasswordPage } = await import("./reset-password")

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/login" element={<div>signed-in screen</div>} />
        <Route path="/forgot-password" element={<div>request form</div>} />
      </Routes>
    </MemoryRouter>
  )
}

async function fillNewPassword(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/^new password$/i), "NewPass1!")
  await user.type(screen.getByLabelText(/confirm password/i), "NewPass1!")
  await user.click(screen.getByRole("button", { name: /reset password/i }))
}

describe("ResetPasswordPage", () => {
  beforeEach(() => {
    postMock.mockReset()
    window.history.replaceState({}, "", "/")
  })

  it("asks only for the new password - the link carries the credential", () => {
    renderAt("/reset-password?token=abc123")

    expect(screen.getByLabelText(/^new password$/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/confirm password/i)).toBeInTheDocument()
    // Regression: the page used to demand the address and a "reset code" that
    // was really a 43-character token nobody would ever retype.
    expect(screen.queryByLabelText(/email/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/reset code/i)).not.toBeInTheDocument()
  })

  it("submits the token from the link and never an email", async () => {
    postMock.mockResolvedValue({ error: undefined })
    const user = userEvent.setup()
    renderAt("/reset-password?token=abc123")

    await fillNewPassword(user)

    expect(postMock).toHaveBeenCalledWith("/api/v1/Auth/reset-password", {
      body: {
        token: "abc123",
        newPassword: "NewPass1!",
        confirmNewPassword: "NewPass1!",
      },
    })
  })

  it("offers a fresh link instead of an unusable form when the token is absent", () => {
    renderAt("/reset-password")

    expect(screen.getByText(/no longer valid/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/^new password$/i)).not.toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: /request a new link/i })
    ).toBeInTheDocument()
  })

  it("rejects mismatched confirmation without calling the API", async () => {
    const user = userEvent.setup()
    renderAt("/reset-password?token=abc123")

    await user.type(screen.getByLabelText(/^new password$/i), "NewPass1!")
    await user.type(screen.getByLabelText(/confirm password/i), "Different1!")
    await user.click(screen.getByRole("button", { name: /reset password/i }))

    expect(postMock).not.toHaveBeenCalled()
  })

  it("sends the user to sign in once the password is set", async () => {
    postMock.mockResolvedValue({ error: undefined })
    const user = userEvent.setup()
    renderAt("/reset-password?token=abc123")

    await fillNewPassword(user)

    expect(await screen.findByText("signed-in screen")).toBeInTheDocument()
  })

  it("keeps the form up when the token turns out to be spent", async () => {
    postMock.mockResolvedValue({ error: { title: "Invalid or expired token" } })
    const user = userEvent.setup()
    renderAt("/reset-password?token=abc123")

    await fillNewPassword(user)

    expect(screen.getByLabelText(/^new password$/i)).toBeInTheDocument()
    expect(screen.queryByText("signed-in screen")).not.toBeInTheDocument()
  })
})
