import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { __resetDataTableStorage } from "@authsystem/ui/data-table/storage"

/**
 * The invite dialog never judges an address while it is being typed (owner,
 * 2026-10-31): the format verdict appears when the field is left or a submit is
 * tried, and goes away on the next edit. Submit stays enabled while the address
 * is merely unchecked, and a malformed one shows the error instead of posting.
 */

const mocks = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: (...args: unknown[]) => mocks.post(...args),
  },
}))

vi.mock("@authsystem/api/ui-preferences", () => ({
  fetchUiPreferences: vi.fn(async () => ({})),
  putUiPreference: vi.fn(async () => true),
  deleteUiPreference: vi.fn(async () => true),
}))

const { InvitationsTab } = await import("./organization-detail-page")

const ORG = "11111111-1111-1111-1111-111111111111"
const ROLE_ID = "22222222-2222-2222-2222-222222222222"

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

async function openDialog() {
  mocks.get.mockImplementation((requestPath: string) => {
    if (requestPath === "/api/v1/Organizations/{id}/membership-roles")
      return ok([{ id: ROLE_ID, code: "org-member", name: "Member" }])
    if (requestPath === "/api/v1/Organizations/{id}/invitations")
      return ok({ invitations: [], totalCount: 0, totalPages: 0 })
    return ok([])
  })
  mocks.post.mockImplementation(() => ok({}))
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      {/* The link chooses the role, so the address is the only open field. */}
      <MemoryRouter
        initialEntries={[`/organizations/${ORG}?invite=1&role=org-member`]}
      >
        <InvitationsTab orgId={ORG} />
      </MemoryRouter>
    </QueryClientProvider>
  )
  const dialog = await screen.findByRole("dialog")
  const email = within(dialog).getByLabelText(
    i18n.t("organizations.inviteEmail")
  )
  const submit = within(dialog).getByRole("button", {
    name: i18n.t("organizations.inviteMember"),
  })
  return { dialog, email, submit }
}

const emailError = () => i18n.t("validation.email")

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

afterEach(() => {
  cleanup()
  __resetDataTableStorage()
  vi.clearAllMocks()
})

describe("the invite dialog's address check", () => {
  it("says nothing while an unfinished address is typed, and keeps submit enabled", async () => {
    const user = userEvent.setup()
    const { dialog, email, submit } = await openDialog()

    await user.type(email, "ada@exam")

    expect(within(dialog).queryByText(emailError())).toBeNull()
    expect(email).toHaveAttribute("aria-invalid", "false")
    expect(submit).toBeEnabled()
  })

  it("shows the error on a submit with a malformed address, posts nothing, and clears it on the next edit", async () => {
    const user = userEvent.setup()
    const { dialog, email, submit } = await openDialog()

    await user.type(email, "ada@exam")
    await user.click(submit)

    expect(within(dialog).getByText(emailError())).toBeInTheDocument()
    expect(email).toHaveAttribute("aria-invalid", "true")
    expect(email).toHaveFocus()
    expect(mocks.post).not.toHaveBeenCalled()

    await user.type(email, "p")

    expect(within(dialog).queryByText(emailError())).toBeNull()
    expect(email).toHaveAttribute("aria-invalid", "false")
  })

  it("shows the error when a typed field is left, but not when an empty one is", async () => {
    const user = userEvent.setup()
    const { dialog, email } = await openDialog()

    await user.click(email)
    await user.tab()
    expect(within(dialog).queryByText(emailError())).toBeNull()

    await user.click(email)
    await user.type(email, "ada@exam")
    await user.tab()
    expect(within(dialog).getByText(emailError())).toBeInTheDocument()
  })

  it("sends a well-formed address", async () => {
    const user = userEvent.setup()
    const { email, submit } = await openDialog()

    await user.type(email, "ada@example.com")
    await user.click(submit)

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    const [, init] = mocks.post.mock.calls[0] as [
      string,
      { body: { email: string; roleId: string } },
    ]
    expect(init.body).toMatchObject({ email: "ada@example.com", roleId: ROLE_ID })
  })
})
