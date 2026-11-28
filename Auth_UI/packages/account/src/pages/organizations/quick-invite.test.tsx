import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  cleanup,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import { __resetDataTableStorage } from "@authsystem/ui/data-table/storage"

/**
 * The quick invite (owner, 2026-11-27): whether the invite dialog is opened by
 * an application's link or by the Invite member button, it shows the address
 * alone and one sentence saying what the invitation gives, and "Change roles"
 * shows the choices. It folds them away only when nothing there needs a look:
 * the member role, every choice a link asked for matched, no list failed.
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
const ADMIN_ID = "22222222-2222-2222-2222-000000000001"
const MEMBER_ID = "22222222-2222-2222-2222-000000000002"
const APP_ID = "33333333-3333-3333-3333-333333333333"
const REP_ID = "44444444-4444-4444-4444-444444444444"

// The member role is NOT first: the default must be found by its code.
const MEMBERSHIP_ROLES = [
  { id: ADMIN_ID, code: "org-admin", name: "Organization Admin" },
  { id: MEMBER_ID, code: "org-member", name: "Organization Member" },
]
const APPS = [
  {
    applicationId: APP_ID,
    applicationCode: "CRM",
    applicationName: "Customer Desk",
    isActive: true,
  },
]
const APP_ROLES = [{ id: REP_ID, code: "sales_rep", name: "Sales rep" }]

const PATHS = {
  roles: "/api/v1/Organizations/{id}/membership-roles",
  apps: "/api/v1/Organizations/{id}/applications",
  appRoles:
    "/api/v1/Organizations/{orgId}/applications/{applicationId}/assignable-roles",
  invitations: "/api/v1/Organizations/{id}/invitations",
}

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function failed() {
  return Promise.resolve({
    error: { status: 500, code: "General.Failure", detail: "Boom" },
    response: new Response(null, { status: 500 }),
  })
}

type Answer = () => Promise<unknown>

function renderTab(
  search = "",
  answers: Partial<Record<keyof typeof PATHS, Answer>> = {}
) {
  mocks.get.mockImplementation((requestPath: string) => {
    if (requestPath === PATHS.roles)
      return (answers.roles ?? (() => ok(MEMBERSHIP_ROLES)))()
    if (requestPath === PATHS.apps) return (answers.apps ?? (() => ok(APPS)))()
    if (requestPath === PATHS.appRoles)
      return (answers.appRoles ?? (() => ok(APP_ROLES)))()
    if (requestPath === PATHS.invitations)
      return ok({ invitations: [], totalCount: 0, totalPages: 0 })
    return ok([])
  })
  mocks.post.mockImplementation(() => ok({}))
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/organizations/${ORG}${search}`]}>
        <InvitationsTab orgId={ORG} organizationName="Acme" />
      </MemoryRouter>
    </QueryClientProvider>
  )
  return client
}

const LINK =
  "?invite=1&role=org-member&application=CRM&applicationRole=sales_rep"

async function openFromButton() {
  const user = userEvent.setup()
  renderTab()
  await user.click(
    await screen.findByRole("button", {
      name: i18n.t("organizations.inviteMember"),
    })
  )
  return { user, dialog: await screen.findByRole("dialog") }
}

async function openFromLink(search: string, answers = {}) {
  renderTab(search, answers)
  return { user: userEvent.setup(), dialog: await screen.findByRole("dialog") }
}

const email = (dialog: HTMLElement) =>
  within(dialog).getByLabelText(i18n.t("organizations.inviteEmail"))
const submit = (dialog: HTMLElement) =>
  within(dialog).getByRole("button", {
    name: i18n.t("organizations.inviteMember"),
  })
const changeRoles = (dialog: HTMLElement) =>
  within(dialog).queryByRole("button", {
    name: i18n.t("organizations.quickInviteChange"),
  })
const choices = (dialog: HTMLElement) =>
  within(dialog).queryAllByRole("combobox")

function sentBody() {
  const [, init] = mocks.post.mock.calls[0] as [
    string,
    { body: Record<string, unknown> },
  ]
  return init.body
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
})

afterEach(() => {
  cleanup()
  __resetDataTableStorage()
  vi.clearAllMocks()
})

describe("opened by the Invite member button", () => {
  it("shows the address alone, names the organization, and says the member role is given", async () => {
    const { dialog } = await openFromButton()

    expect(
      within(dialog).getByRole("heading", {
        name: "Invite someone to Acme",
      })
    ).toBeInTheDocument()
    await within(dialog).findByText(
      "They will join the organization as “Organization Member”."
    )
    expect(within(dialog).getAllByRole("textbox")).toHaveLength(1)
    expect(choices(dialog)).toHaveLength(0)
    expect(changeRoles(dialog)).not.toBeNull()
    // The sentence is read with the field: it is all that says what is given.
    expect(email(dialog)).toHaveAccessibleDescription(
      "They will join the organization as “Organization Member”."
    )
  })

  it("sends the member role, found by its code, when Enter is pressed", async () => {
    const { user, dialog } = await openFromButton()
    await within(dialog).findByText(/Organization Member/)

    await user.type(email(dialog), "ada@example.com{Enter}")

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    expect(sentBody()).toEqual({
      email: "ada@example.com",
      roleId: MEMBER_ID,
      applicationRole: null,
    })
  })

  it("sends once when Enter is pressed twice", async () => {
    const { user, dialog } = await openFromButton()
    await within(dialog).findByText(/Organization Member/)
    // The first request never settles, so only the latch can stop the second.
    mocks.post.mockImplementation(() => new Promise(() => {}))

    await user.type(email(dialog), "ada@example.com{Enter}{Enter}")

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    await user.keyboard("{Enter}")
    expect(mocks.post).toHaveBeenCalledTimes(1)
  })

  it("holds the sentence and the button until the roles arrive", async () => {
    const user = userEvent.setup()
    let arrive: (value: unknown) => void = () => {}
    renderTab("", {
      roles: () =>
        new Promise((resolve) => {
          arrive = resolve
        }),
    })
    await user.click(
      await screen.findByRole("button", {
        name: i18n.t("organizations.inviteMember"),
      })
    )
    const dialog = await screen.findByRole("dialog")

    expect(email(dialog)).toBeEnabled()
    expect(submit(dialog)).toBeDisabled()
    expect(choices(dialog)).toHaveLength(0)
    expect(
      dialog.querySelector('[data-slot="skeleton"]'),
      "a placeholder where the sentence goes"
    ).not.toBeNull()

    arrive({
      data: MEMBERSHIP_ROLES,
      response: new Response(null, { status: 200 }),
    })
    await within(dialog).findByText(/Organization Member/)
    expect(submit(dialog)).toBeEnabled()
  })

  it("shows the choices on Change roles, with the member role chosen and focus on it", async () => {
    const { user, dialog } = await openFromButton()
    await within(dialog).findByText(/Organization Member/)

    await user.click(changeRoles(dialog)!)

    const role = within(dialog).getByRole("combobox", {
      name: i18n.t("common.role"),
    })
    expect(role).toHaveTextContent("Organization Member")
    expect(role).toHaveFocus()
    expect(changeRoles(dialog)).toBeNull()
    expect(
      within(dialog).queryByText(/They will join the organization/)
    ).toBeNull()
  })

  it("shows the choices, with the error, when the roles fail to load", async () => {
    const user = userEvent.setup()
    renderTab("", { roles: failed })
    await user.click(
      await screen.findByRole("button", {
        name: i18n.t("organizations.inviteMember"),
      })
    )
    const dialog = await screen.findByRole("dialog")

    await waitFor(() => expect(choices(dialog).length).toBeGreaterThan(0))
    expect(changeRoles(dialog)).toBeNull()
    expect(submit(dialog)).toBeDisabled()
  })

  it("keeps the choices on show when the roles load after failing", async () => {
    const user = userEvent.setup()
    let answer: Answer = failed
    const client = renderTab("", { roles: () => answer() })
    await user.click(
      await screen.findByRole("button", {
        name: i18n.t("organizations.inviteMember"),
      })
    )
    const dialog = await screen.findByRole("dialog")
    await waitFor(() => expect(choices(dialog).length).toBeGreaterThan(0))

    answer = () => ok(MEMBERSHIP_ROLES)
    await client.refetchQueries()

    // The default arrives with the list, and the picker shows it: the role
    // on show is the role that will be sent.
    await waitFor(() =>
      expect(
        within(dialog).getByRole("combobox", { name: i18n.t("common.role") })
      ).toHaveTextContent("Organization Member")
    )
    expect(changeRoles(dialog)).toBeNull()
    expect(
      within(dialog).queryByText(/They will join the organization/)
    ).toBeNull()
  })

  it("shows the choices when the list holds no member role", async () => {
    const user = userEvent.setup()
    renderTab("", { roles: () => ok([MEMBERSHIP_ROLES[0]]) })
    await user.click(
      await screen.findByRole("button", {
        name: i18n.t("organizations.inviteMember"),
      })
    )
    const dialog = await screen.findByRole("dialog")

    await waitFor(() => expect(choices(dialog).length).toBeGreaterThan(0))
    // Nothing chosen for the inviter: the admin role is never a default.
    expect(
      within(dialog).getByRole("combobox", { name: i18n.t("common.role") })
    ).not.toHaveTextContent("Organization Admin")
    expect(submit(dialog)).toBeDisabled()
  })
})

describe("opened by an application's link", () => {
  it("says both roles it gives, and sends them", async () => {
    const { user, dialog } = await openFromLink(LINK)

    expect(
      within(dialog).getByText(
        "They will join the organization as “Organization Member”, with the “Sales rep” role in Customer Desk."
      )
    ).toBeInTheDocument()
    expect(choices(dialog)).toHaveLength(0)

    await user.type(email(dialog), "ada@example.com")
    await user.click(submit(dialog))

    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    expect(sentBody()).toEqual({
      email: "ada@example.com",
      roleId: MEMBER_ID,
      applicationRole: { applicationId: APP_ID, roleId: REP_ID },
    })
  })

  it("keeps the choices on Change roles, and sends the same body", async () => {
    const { user, dialog } = await openFromLink(LINK)

    await user.click(changeRoles(dialog)!)
    expect(
      within(dialog).getByRole("combobox", { name: i18n.t("common.role") })
    ).toHaveTextContent("Organization Member")
    expect(
      within(dialog).getByRole("combobox", {
        name: i18n.t("organizations.inviteApplication"),
      })
    ).toHaveTextContent("Customer Desk")
    expect(
      within(dialog).getByRole("combobox", {
        name: i18n.t("organizations.applicationRole"),
      })
    ).toHaveTextContent("Sales rep")

    await user.type(email(dialog), "ada@example.com{Enter}")
    await waitFor(() => expect(mocks.post).toHaveBeenCalledTimes(1))
    expect(sentBody()).toEqual({
      email: "ada@example.com",
      roleId: MEMBER_ID,
      applicationRole: { applicationId: APP_ID, roleId: REP_ID },
    })
  })

  it("shows the choices when it asks for the admin role", async () => {
    const { dialog } = await openFromLink(
      "?invite=1&role=org-admin&application=CRM&applicationRole=sales_rep"
    )

    expect(
      within(dialog).getByRole("combobox", { name: i18n.t("common.role") })
    ).toHaveTextContent("Organization Admin")
    expect(changeRoles(dialog)).toBeNull()
  })

  it("shows the choices when its application role is not one the inviter may give", async () => {
    const { dialog } = await openFromLink(
      "?invite=1&role=org-member&application=CRM&applicationRole=sales_manager"
    )

    expect(
      within(dialog).getByRole("combobox", {
        name: i18n.t("organizations.applicationRole"),
      })
    ).toBeInTheDocument()
    expect(changeRoles(dialog)).toBeNull()
    expect(submit(dialog)).toBeDisabled()
  })

  it("shows the choices when its application is not the organization's", async () => {
    const { dialog } = await openFromLink(
      "?invite=1&role=org-member&application=BILLING&applicationRole=sales_rep"
    )

    expect(choices(dialog).length).toBeGreaterThan(0)
    expect(changeRoles(dialog)).toBeNull()
  })

  it("leaves the role empty, with the choices on show, when its role code matches nothing", async () => {
    const { dialog } = await openFromLink("?invite=1&role=org-owner")

    expect(
      within(dialog).getByRole("combobox", { name: i18n.t("common.role") })
    ).not.toHaveTextContent("Organization Member")
    expect(submit(dialog)).toBeDisabled()
  })

  it("gives the member role when it names no role", async () => {
    const { dialog } = await openFromLink("?invite=1")

    expect(
      within(dialog).getByText(
        "They will join the organization as “Organization Member”."
      )
    ).toBeInTheDocument()
    expect(choices(dialog)).toHaveLength(0)
  })
})
