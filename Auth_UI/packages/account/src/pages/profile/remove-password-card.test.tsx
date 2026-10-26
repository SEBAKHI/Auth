import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

/**
 * D-215-3 on the profile: «Remove password» is offered only where the server
 * would allow it as far as the page can tell, always behind a confirmation;
 * where the emailed code is the second step it says what to do first instead of
 * showing a button that would be refused; and a session too old for it is asked
 * to sign in again.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  codeSignInOffered: true,
}))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => mocks.post(...args) },
}))
vi.mock("@authsystem/ui/branding", () => ({
  useSignInOptions: () => ({
    codeSignInOffered: mocks.codeSignInOffered,
    primaryMethod: "password",
    selfRegistrationOpen: true,
    passwordAtSignUp: true,
    nameAtSignUp: true,
  }),
}))
vi.mock("@authsystem/auth/reauthenticate-dialog", () => ({
  ReauthenticateDialog: ({ description }: { description?: string }) => (
    <div role="dialog" aria-label="sign in again">
      {description}
    </div>
  ),
}))

import { RemovePasswordCard } from "./remove-password-card"
import { removePasswordOffer } from "./remove-password-offer"

const ME = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@one.example",
  hasPassword: true,
  emailConfirmed: true,
  emailCodeEnabled: false,
} as never as Parameters<typeof RemovePasswordCard>[0]["me"]

function renderCard(me = ME) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <RemovePasswordCard me={me} />
    </QueryClientProvider>
  )
}

describe("removePasswordOffer", () => {
  it.each([
    ["an account with a password, email on, a confirmed address", ME, true, "offered"],
    ["no password to remove", { ...ME, hasPassword: false }, true, "none"],
    ["an API that does not say", { ...ME, hasPassword: undefined }, true, "none"],
    ["email sending off", ME, false, "none"],
    ["an unconfirmed address", { ...ME, emailConfirmed: false }, true, "none"],
    ["the emailed code as the second step", { ...ME, emailCodeEnabled: true }, true, "blockedByEmailCode"],
    // D-110-1: platform authority keeps the app as a second proof.
    ["platform authority without the app", { ...ME, permissions: ["users:read"] }, true, "blockedByPlatformAuthority"],
    ["platform authority with the app", { ...ME, permissions: ["users:read"], totpEnabled: true }, true, "offered"],
    ["no platform authority", { ...ME, permissions: [] }, true, "offered"],
  ])("%s", (_label, me, codeSignInOffered, expected) => {
    expect(removePasswordOffer(me as typeof ME, codeSignInOffered)).toBe(expected)
  })
})

describe("RemovePasswordCard", () => {
  beforeEach(() => {
    mocks.post.mockReset()
    mocks.codeSignInOffered = true
  })

  it("removes the password only after the confirmation", async () => {
    mocks.post.mockResolvedValue({ response: new Response(null, { status: 204 }) })
    renderCard()
    const user = userEvent.setup()

    await user.click(screen.getByRole("button", { name: "Remove password" }))
    expect(mocks.post).not.toHaveBeenCalled()
    const dialog = await screen.findByRole("alertdialog")
    expect(dialog).toHaveTextContent("jane@one.example")
    expect(dialog).toHaveTextContent(/signed out/i)

    await user.click(
      screen.getAllByRole("button", { name: "Remove password" }).at(-1)!
    )

    await waitFor(() =>
      expect(mocks.post).toHaveBeenCalledWith("/api/v1/Auth/remove-password")
    )
  })

  it("explains the emailed second step instead of a button that would be refused", () => {
    renderCard({ ...ME, emailCodeEnabled: true } as typeof ME)

    expect(screen.queryByRole("button", { name: "Remove password" })).toBeNull()
    expect(
      screen.getByText(/Turn off the code by email, or set up the authenticator app, first/)
    ).toBeInTheDocument()
  })

  it("explains the authenticator app a platform administrator needs first", () => {
    renderCard({ ...ME, permissions: ["users:read"] } as typeof ME)

    expect(screen.queryByRole("button", { name: "Remove password" })).toBeNull()
    expect(screen.getByText(/platform administration rights/)).toBeInTheDocument()
    expect(screen.getByText(/set up the authenticator app first/)).toBeInTheDocument()
  })

  it("shows nothing while email sending is off", () => {
    mocks.codeSignInOffered = false
    const { container } = renderCard()

    expect(container).toBeEmptyDOMElement()
  })

  it("asks a session too old for it to sign in again", async () => {
    mocks.post.mockResolvedValue({
      error: { status: 403, code: "Auth.ReauthenticationRequired", detail: "Sign in again" },
    })
    renderCard()
    const user = userEvent.setup()

    await user.click(screen.getByRole("button", { name: "Remove password" }))
    await user.click(
      (await screen.findAllByRole("button", { name: "Remove password" })).at(-1)!
    )

    expect(
      await screen.findByRole("dialog", { name: "sign in again" })
    ).toHaveTextContent(/recent sign-in with your password/)
  })
})
