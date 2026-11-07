import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type * as React from "react"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

/**
 * REVIEW 151 F1, owner decision D-151-1 (b): a proven recovery of an account
 * whose state refuses it a session (deactivated, not yet activated, locked by
 * an administrator) cancels the deletion and issues no session. The page shows
 * the API's sentence as the outcome and offers the recovery no more: a second
 * press would find nothing waiting for deletion and be refused, a dead end.
 * Any other failure keeps the form, as before.
 */

const mocks = vi.hoisted(() => ({
  recoverAccount: vi.fn(),
  recoverAccountExternal: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({ api: { POST: vi.fn() } }))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    recoverAccount: mocks.recoverAccount,
    recoverAccountExternal: mocks.recoverAccountExternal,
  }),
}))
vi.mock("@authsystem/auth/login-completion", () => ({
  useLoginCompletion: () => ({
    complete: mocks.complete,
    challenge: mocks.challenge,
  }),
}))
// The provider buttons have their own tests: here, only whether they are offered.
vi.mock("@authsystem/auth/external/external-providers", () => ({
  ExternalProviders: () => <div data-testid="external-providers" />,
}))
vi.mock("sonner", () => ({
  toast: { error: mocks.toastError, success: vi.fn(), info: vi.fn() },
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    footer,
    children,
  }: {
    title: React.ReactNode
    footer?: React.ReactNode
    children: React.ReactNode
  }) => (
    <main>
      <h1>{title}</h1>
      {children}
      <footer>{footer}</footer>
    </main>
  ),
}))

import { AccountRecoveryPage } from "./account-recovery"

const CODES = [
  "User.DeletionCancelledAccountInactive",
  "User.DeletionCancelledAccountPending",
  "User.DeletionCancelledAccountLocked",
] as const

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/account-recovery"]}>
      <AccountRecoveryPage />
    </MemoryRouter>
  )
}

async function submitCredentials() {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText(/^email$/i), "owner@example.com")
  await user.type(screen.getByLabelText(/^password$/i), "the-password")
  await user.click(screen.getByRole("button", { name: /restore my account/i }))
}

describe("AccountRecoveryPage", () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it.each(CODES)(
    "ends the recovery on %s: the sentence is the outcome, and the form is gone",
    async (code) => {
      mocks.recoverAccount.mockRejectedValueOnce({
        status: 403,
        code,
        detail: `cancelled: ${code}`,
      })
      renderPage()

      await submitCredentials()

      const outcome = await screen.findByText(`cancelled: ${code}`)
      expect(
        outcome.closest('[data-slot="alert"]'),
        "information, not an error"
      ).toHaveAttribute("data-variant", "default")
      expect(
        screen.queryByRole("button", { name: /restore my account/i })
      ).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/^password$/i)).not.toBeInTheDocument()
      expect(screen.queryByTestId("external-providers")).not.toBeInTheDocument()
      expect(
        screen.queryByText(/scheduled for deletion/i),
        "the deletion is cancelled: the notice no longer holds"
      ).not.toBeInTheDocument()
      expect(
        screen.getByRole("link", { name: /back to sign in/i })
      ).toBeInTheDocument()
      expect(mocks.toastError).not.toHaveBeenCalled()
      expect(mocks.complete).not.toHaveBeenCalled()
    }
  )

  it("keeps the form for any other failure", async () => {
    mocks.recoverAccount.mockRejectedValueOnce({
      status: 400,
      code: "User.InvalidCredentials",
      detail: "The provided credentials are invalid.",
    })
    renderPage()

    await submitCredentials()

    await vi.waitFor(() => expect(mocks.toastError).toHaveBeenCalled())
    expect(
      screen.getByRole("button", { name: /restore my account/i })
    ).toBeInTheDocument()
    expect(screen.getByTestId("external-providers")).toBeInTheDocument()
  })
})
