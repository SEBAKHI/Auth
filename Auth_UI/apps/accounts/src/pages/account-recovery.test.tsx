import { fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import type * as React from "react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * REVIEW 151 F1, owner decision D-151-1 (b): a proven recovery of an account
 * whose state refuses it a session (deactivated, not yet activated, locked by
 * an administrator) cancels the deletion and issues no session. The page shows
 * the API's sentence as the outcome and offers the recovery no more: a second
 * press would find nothing waiting for deletion and be refused, a dead end.
 * Any other failure keeps the form, as before.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  recoverWithPasskey: vi.fn(),
  loadWebAuthn: vi.fn(),
  startAuthentication: vi.fn(),
  recoverAccount: vi.fn(),
  recoverAccountExternal: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({ api: { POST: mocks.post } }))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({
    recoverAccount: mocks.recoverAccount,
    recoverAccountExternal: mocks.recoverAccountExternal,
    recoverWithPasskey: mocks.recoverWithPasskey,
  }),
}))
// S20: the passkey choice's helper, in hand without the package (T-C1's loader).
vi.mock("@authsystem/auth/passkey/webauthn", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/auth/passkey/webauthn")>()),
  loadWebAuthn: mocks.loadWebAuthn,
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

/**
 * S20 PR C, IN 6 (D-S20-6 (a)): «cancel the deletion with your passkey», with the
 * same options as the sign-in, then the recovery. It ends as the other entries
 * do: a session resumes; D-151-1's codes are the outcome (C15); an account not
 * waiting for deletion hears today's answer. And an entry that answers
 * `Auth.PasskeyRequired` points to this choice (IN 5).
 */
describe("AccountRecoveryPage: the passkey choice", () => {
  const CREDENTIAL = { id: "cred-1", rawId: "cred-1", type: "public-key" }
  const CHOICE = i18n.t("accountDeletion.recoverWithPasskey")

  function passkeysRun() {
    vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
    mocks.loadWebAuthn.mockResolvedValue({
      startAuthentication: mocks.startAuthentication,
      browserSupportsWebAuthnAutofill: async () => false,
      WebAuthnAbortService: { cancelCeremony: vi.fn() },
    })
    mocks.startAuthentication.mockResolvedValue(CREDENTIAL)
    mocks.post.mockResolvedValue({
      data: {
        options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
        expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
      },
      error: undefined,
      response: { status: 200, headers: new Headers() },
    })
  }

  async function choice() {
    return within(await screen.findByTestId("passkey-sign-in")).getByRole("button", { name: CHOICE })
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it("is not offered while passkeys do not run", () => {
    renderPage()

    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    expect(mocks.post).not.toHaveBeenCalled()
  })

  it("cancels the deletion with the passkey, and resumes like the other entries", async () => {
    passkeysRun()
    const signedIn = { status: "authenticated", requiresPasswordChange: false }
    mocks.recoverWithPasskey.mockResolvedValue(signedIn)
    renderPage()

    fireEvent.click(await choice())

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledWith(signedIn))
    expect(mocks.recoverWithPasskey).toHaveBeenCalledWith(CREDENTIAL)
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/auth/passkeys/assertion/options")
  })

  it.each(CODES)("ends on %s as the outcome, not an error", async (code) => {
    passkeysRun()
    mocks.recoverWithPasskey.mockRejectedValue({ status: 403, code, detail: `cancelled: ${code}` })
    renderPage()

    fireEvent.click(await choice())

    const outcome = await screen.findByText(`cancelled: ${code}`)
    expect(outcome.closest('[data-slot="alert"]')).toHaveAttribute("data-variant", "default")
    expect(screen.queryByRole("button", { name: CHOICE })).not.toBeInTheDocument()
    expect(mocks.toastError).not.toHaveBeenCalled()
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("keeps today's answer for an account that is not waiting for deletion", async () => {
    passkeysRun()
    mocks.recoverWithPasskey.mockRejectedValue({
      status: 400,
      code: "User.InvalidCredentials",
      detail: "The provided credentials are invalid.",
    })
    renderPage()

    fireEvent.click(await choice())

    await waitFor(() =>
      expect(mocks.toastError).toHaveBeenCalledWith("The provided credentials are invalid.")
    )
    expect(screen.getByRole("button", { name: /restore my account/i })).toBeInTheDocument()
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("an entry that asks for the passkey points to the choice, which takes the focus", async () => {
    passkeysRun()
    mocks.recoverAccount.mockRejectedValueOnce({
      status: 403,
      code: "Auth.PasskeyRequired",
      detail: "This account recovers with its passkey.",
    })
    renderPage()
    const button = await choice()

    await submitCredentials()

    expect(await screen.findByTestId("passkey-required-note")).toHaveTextContent(
      "This account recovers with its passkey."
    )
    expect(mocks.toastError).not.toHaveBeenCalled()
    await waitFor(() => expect(button).toHaveFocus())
  })
})
