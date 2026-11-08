import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * OI-215: the "sign in again" dialog carries an add-only full-proof reason
 * through the sign-out, so a sign-in by code on the page the guard lands on
 * proves the password too where the account has one.
 */

const mocks = vi.hoisted(() => ({
  logout: vi.fn(),
  /** The reason as the sign-out found it. */
  flagAtSignOut: [] as Array<string | null>,
}))

vi.mock("./auth-context", () => ({
  useAuth: () => ({ logout: mocks.logout }),
}))

import { ReauthenticateDialog } from "./reauthenticate-dialog"
import {
  FULL_PROOF_KEY,
  isFullProofRequested,
  isPasskeySignInRequested,
} from "./sign-in-method"

describe("ReauthenticateDialog", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    mocks.flagAtSignOut = []
    mocks.logout.mockReset().mockImplementation(async () => {
      mocks.flagAtSignOut.push(window.sessionStorage.getItem(FULL_PROOF_KEY))
    })
  })

  it("asks the next sign-in for full proof before it signs out", async () => {
    render(<ReauthenticateDialog open onOpenChange={vi.fn()} />)

    await userEvent.setup().click(screen.getByRole("button", { name: "Sign in again" }))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    // Written before the sign-out, and still there after it: the guard's
    // redirect to the sign-in page happens once the session is gone.
    expect(mocks.flagAtSignOut).toEqual(["1"])
    expect(isFullProofRequested()).toBe(true)
  })

  it("asks nothing of a cancel", async () => {
    const onOpenChange = vi.fn()
    render(<ReauthenticateDialog open onOpenChange={onOpenChange} />)

    await userEvent.setup().click(screen.getByRole("button", { name: "Cancel" }))

    expect(onOpenChange).toHaveBeenCalledWith(false)
    expect(isFullProofRequested()).toBe(false)
    expect(mocks.logout).not.toHaveBeenCalled()
  })

  // S20, T-C7: a passkey is the account's qualifying factor. Neither the password
  // nor the emailed code gives the session the change needs, so the dialog says
  // so, and the sign-in page it leads to offers the passkey first: the person is
  // not sent round again by a password sign-in.
  it("names the passkey for a passkey account, and asks the sign-in page to offer it first", async () => {
    render(
      <ReauthenticateDialog
        open
        onOpenChange={vi.fn()}
        description="Adding a passkey needs a recent sign-in."
        passkey
      />
    )

    expect(screen.getByText(i18n.t("auth.reauthenticatePasskeyBody"))).toBeInTheDocument()
    expect(screen.queryByText("Adding a passkey needs a recent sign-in.")).not.toBeInTheDocument()
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: i18n.t("auth.reauthenticateWithPasskeyAction") }))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    expect(mocks.flagAtSignOut).toEqual(["passkey"])
    expect(isPasskeySignInRequested()).toBe(true)
    // A code sign-in from there still proves the password too.
    expect(isFullProofRequested()).toBe(true)
  })

  it("keeps the usual words and reason for every other account", async () => {
    render(<ReauthenticateDialog open onOpenChange={vi.fn()} description="Why it is needed." />)

    expect(screen.getByText("Why it is needed.")).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole("button", { name: "Sign in again" }))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalledTimes(1))
    expect(isPasskeySignInRequested()).toBe(false)
  })
})
