import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
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
})
