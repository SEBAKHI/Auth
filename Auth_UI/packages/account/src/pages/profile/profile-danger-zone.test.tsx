import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it, vi } from "vitest"

import type { Schemas } from "@authsystem/api/types"

/**
 * OI-224: asking for one's own deletion is refused to the only full
 * administrator who can sign in. The profile read says so
 * (`allowedActions.requestDeletion`), and the danger zone then says why instead
 * of offering a form the server would refuse after the emailed code. A profile
 * without the answer (an older API) keeps the form. Deliberate break: ignore
 * the flag.
 */
vi.mock("@authsystem/api/client", () => ({
  api: { POST: vi.fn(), GET: vi.fn(), DELETE: vi.fn() },
}))

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options ? `${key}|${JSON.stringify(options)}` : key,
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ logout: vi.fn() }),
}))

import { ProfileDangerZone } from "./profile-danger-zone"

const ME = { id: "user-1", email: "owner@example.com" } as Schemas["UserDto"]

function renderZone(me: Schemas["UserDto"]) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ProfileDangerZone me={me} />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

describe("the danger zone of the only full administrator", () => {
  it("says why the deletion is not offered, and offers no form", () => {
    renderZone({ ...ME, allowedActions: { requestDeletion: false } })

    expect(
      screen.getByText("accountDeletion.lastFullAdministratorTitle")
    ).toBeVisible()
    expect(
      screen.getByText("accountDeletion.lastFullAdministratorBody")
    ).toBeVisible()
    expect(
      screen.queryByRole("button", { name: "accountDeletion.deleteAccount" })
    ).toBeNull()
  })

  it.each([
    ["allowed", { ...ME, allowedActions: { requestDeletion: true } }],
    ["not said (an older API)", ME],
  ])("offers the deletion when it is %s", (_, me) => {
    renderZone(me)

    expect(
      screen.getByRole("button", { name: "accountDeletion.deleteAccount" })
    ).toBeVisible()
    expect(
      screen.queryByText("accountDeletion.lastFullAdministratorTitle")
    ).toBeNull()
  })
})
