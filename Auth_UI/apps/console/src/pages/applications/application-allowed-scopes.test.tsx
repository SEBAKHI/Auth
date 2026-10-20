import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The allowed scopes offer organizations.read (OI-194), the scope that lets an
 * application read its users' organizations: one more checkbox with its hint,
 * saved in the server's canonical order (Auth.Domain OAuthScopes.Optional).
 * The API is answered at the typed client.
 */

const get = vi.fn()
const post = vi.fn()
const put = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: (...args: unknown[]) => post(...args),
    PUT: (...args: unknown[]) => put(...args),
    DELETE: vi.fn(),
  },
}))

import {
  ApplicationCreateDialog,
  ApplicationEditDialog,
} from "./application-dialogs"

const APP_ID = "22222222-2222-2222-2222-222222222222"
const SCOPE = "organizations.read"

const detail = {
  id: APP_ID,
  code: "CRM",
  name: "Customer Desk",
  baseUrl: "https://crm.example.com",
  initiateLoginUri: null,
  isActive: true,
  accessMode: "Everyone",
  allowSelfRegistration: false,
  requireTwoFactor: false,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 60,
  maxConcurrentSessions: 5,
  reauthenticationMaxAgeMinutes: null,
  redirectUris: [],
  postLogoutRedirectUris: [],
  allowedScopes: ["email"],
  allowOrganizationCreation: false,
  organizationCreatorRoleId: null,
}

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

beforeEach(async () => {
  await i18n.changeLanguage("en")
  get.mockImplementation((path: string) =>
    path === "/api/v1/Applications/{id}/roles" ? ok([]) : ok(detail)
  )
  put.mockImplementation(() => ok(detail))
  post.mockImplementation(() => ok(detail))
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe("the organizations.read scope in the application dialogs", () => {
  it("is offered, unticked, with a hint naming what it delegates", async () => {
    renderWithClient(
      <ApplicationEditDialog
        open
        onOpenChange={() => {}}
        application={{ id: APP_ID, name: "Customer Desk" }}
      />
    )

    const box = await screen.findByRole("checkbox", { name: SCOPE })
    expect(box).not.toBeChecked()
    expect(screen.getByRole("checkbox", { name: "email" })).toBeChecked()
    expect(
      screen.getByText(
        /may read, as this user, the members and invitations of the user's organizations that enabled it/
      )
    ).toBeInTheDocument()
  })

  it("the edit dialog saves it after the scopes before it, in canonical order", async () => {
    const user = userEvent.setup()
    renderWithClient(
      <ApplicationEditDialog
        open
        onOpenChange={() => {}}
        application={{ id: APP_ID, name: "Customer Desk" }}
      />
    )

    await user.click(await screen.findByRole("checkbox", { name: SCOPE }))
    await user.click(screen.getByRole("checkbox", { name: "profile" }))
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]?.body.allowedScopes).toEqual([
      "profile",
      "email",
      SCOPE,
    ])
  })

  it("the create dialog sends it when ticked", async () => {
    const user = userEvent.setup()
    renderWithClient(<ApplicationCreateDialog open onOpenChange={() => {}} />)

    await user.type(await screen.findByLabelText("Code"), "CRM")
    await user.type(screen.getByLabelText("Name"), "Customer Desk")
    await user.click(screen.getByRole("checkbox", { name: SCOPE }))
    await user.click(screen.getByRole("button", { name: "Create" }))

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0]?.[1]?.body.allowedScopes).toEqual([SCOPE])
  })
})
