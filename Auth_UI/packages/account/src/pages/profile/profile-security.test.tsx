import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"

/**
 * Which password card the Security tab shows.
 *
 * An account created by signing in with Google has no password, so the change
 * form demanded a current password it could never supply - not awkward, but
 * literally unsubmittable, and the only route out of it was a reset link the
 * product never mentioned. `hasPassword` had been on this very query all along
 * with no reader.
 *
 * The direction of the fallback is the part that matters. `hasPassword` is
 * optional in the generated schema, so an API that stopped sending it must land
 * on the change form (harmless for the many, useless for the few) rather than
 * hiding the change form from everybody.
 */
vi.mock("@authsystem/api/client", () => ({
  api: { POST: vi.fn(), GET: vi.fn(), DELETE: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  // @authsystem/api/errors pulls in the i18n singleton, which calls this at
  // import time; without it the whole module graph fails to load.
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

import { api } from "@authsystem/api/client"

import { ProfileSecurity } from "./profile-security"

function renderTab(me: Partial<Schemas["UserDto"]>) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <ProfileSecurity me={me as Schemas["UserDto"]} />
    </QueryClientProvider>
  )
}

const CURRENT_PASSWORD_LABEL = "auth.currentPassword"
const SET_PASSWORD_TITLE = "profile.setPassword"

describe("the Security tab password card", () => {
  it("offers to set one when the account has no password", () => {
    renderTab({ email: "external@example.com", hasPassword: false })

    expect(screen.getByText(SET_PASSWORD_TITLE)).toBeInTheDocument()
    // The field it could never fill is gone, not merely optional.
    expect(screen.queryByText(CURRENT_PASSWORD_LABEL)).not.toBeInTheDocument()
  })

  it("keeps the change form when the account has a password", () => {
    renderTab({ email: "john@example.com", hasPassword: true })

    expect(screen.getByText(CURRENT_PASSWORD_LABEL)).toBeInTheDocument()
    expect(screen.queryByText(SET_PASSWORD_TITLE)).not.toBeInTheDocument()
  })

  it("falls back to the change form when the API omits hasPassword", () => {
    renderTab({ email: "john@example.com" })

    expect(screen.getByText(CURRENT_PASSWORD_LABEL)).toBeInTheDocument()
    expect(screen.queryByText(SET_PASSWORD_TITLE)).not.toBeInTheDocument()
  })

  it("falls back to the change form when there is no address to mail", () => {
    // Nothing usable can be offered without an address, so do not offer a card
    // whose only button cannot work.
    renderTab({ email: undefined, hasPassword: false })

    expect(screen.getByText(CURRENT_PASSWORD_LABEL)).toBeInTheDocument()
    expect(screen.queryByText(SET_PASSWORD_TITLE)).not.toBeInTheDocument()
  })

  it("puts every reason the server refuses the new password under its field", async () => {
    // The API reports every broken rule at once; the card must show them all
    // under the control rather than the first one in a toast.
    const post = api.POST as unknown as ReturnType<typeof vi.fn>
    post.mockResolvedValue({
      error: {
        status: 400,
        title: "Password.TooShort",
        detail: "Password must be at least 12 characters long.",
        errors: [
          {
            code: "Password.TooShort",
            description: "Password must be at least 12 characters long.",
          },
          {
            code: "Password.RequiresDigit",
            description: "Password must contain at least one digit.",
          },
        ],
      },
    })
    const user = userEvent.setup()
    renderTab({ email: "john@example.com", hasPassword: true })

    await user.type(screen.getByLabelText(CURRENT_PASSWORD_LABEL), "OldPass1!")
    await user.type(screen.getByLabelText("auth.newPassword"), "NewPass1!")
    await user.type(screen.getByLabelText("auth.confirmPassword"), "NewPass1!")
    await user.click(
      screen.getByRole("button", { name: "profile.changePassword" })
    )

    expect(
      await screen.findByText("Password must be at least 12 characters long.")
    ).toBeVisible()
    expect(
      screen.getByText("Password must contain at least one digit.")
    ).toBeVisible()
    expect(screen.getByLabelText("auth.newPassword")).toHaveAttribute(
      "aria-invalid",
      "true"
    )
  }, 15_000)
})
