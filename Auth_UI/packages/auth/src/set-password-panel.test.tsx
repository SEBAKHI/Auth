import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The panel that lets an external-only account acquire its first password.
 *
 * What is worth pinning here is not the markup but the two decisions inside it:
 * the link goes to the signed-in user's own address and nowhere else, and the
 * resend is rate-limited client-side. /forgot-password shares the "login"
 * bucket with real sign-ins, so an ungated resend lets a user 429 themselves out
 * of the thing they are trying to reach.
 *
 * @authsystem/api/client is mocked rather than stubbed around: importing it for
 * real pulls in the token store, and this jsdom has no localStorage at all.
 */
const post = vi.fn()
vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))

const toastError = vi.fn()
vi.mock("sonner", () => ({ toast: { error: (m: string) => toastError(m) } }))

vi.mock("@authsystem/api/errors", () => ({
  getErrorMessage: () => "server-said-slow-down",
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
}))

import { SetPasswordPanel } from "./set-password-panel"

describe("SetPasswordPanel", () => {
  beforeEach(() => {
    post.mockReset()
    toastError.mockReset()
  })

  it("emails the link to the signed-in address", async () => {
    post.mockResolvedValue({ data: { maskedEmail: "j***@example.com" } })

    render(<SetPasswordPanel email="john@example.com" />)
    await userEvent.click(
      screen.getByRole("button", { name: "profile.emailSetPasswordLink" })
    )

    expect(post).toHaveBeenCalledWith("/api/v1/Auth/forgot-password", {
      body: { email: "john@example.com" },
    })
  })

  it("reports back with the masked address the server returned", async () => {
    post.mockResolvedValue({ data: { maskedEmail: "j***@example.com" } })

    render(<SetPasswordPanel email="john@example.com" />)
    await userEvent.click(
      screen.getByRole("button", { name: "profile.emailSetPasswordLink" })
    )

    // The masked value, not the address we sent: the response is identical for
    // unknown addresses on purpose and echoing our own input would undo that.
    await waitFor(() =>
      expect(
        screen.getByText(/auth\.resetLinkSentDescription/)
      ).toHaveTextContent("j***@example.com")
    )
  })

  it("locks the resend behind a cooldown so a repeat click cannot 429 the user", async () => {
    post.mockResolvedValue({ data: { maskedEmail: "j***@example.com" } })

    render(<SetPasswordPanel email="john@example.com" />)
    await userEvent.click(
      screen.getByRole("button", { name: "profile.emailSetPasswordLink" })
    )

    const resend = await screen.findByRole("button", {
      name: /auth\.resendAvailableIn/,
    })
    expect(resend).toBeDisabled()

    await userEvent.click(resend)
    expect(post).toHaveBeenCalledTimes(1)
  })

  it("surfaces the server's own message when the request is refused", async () => {
    post.mockResolvedValue({ error: { status: 429 } })

    render(<SetPasswordPanel email="john@example.com" />)
    await userEvent.click(
      screen.getByRole("button", { name: "profile.emailSetPasswordLink" })
    )

    await waitFor(() =>
      expect(toastError).toHaveBeenCalledWith("server-said-slow-down")
    )
    // Still on the initial state, so the user can try again once the window passes.
    expect(
      screen.getByRole("button", { name: "profile.emailSetPasswordLink" })
    ).toBeEnabled()
  })
})
