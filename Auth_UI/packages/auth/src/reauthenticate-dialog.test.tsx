import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

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
import { FULL_PROOF_KEY, isFullProofRequested } from "./sign-in-method"

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
})
