import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The application dialogs carry the initiate-login URI (OI-192), the address an
 * invitee is sent to after accepting: the edit dialog shows the stored value and
 * saves an edit, the create dialog sends it, and an emptied field goes out as ""
 * because the API reads null as "leave it as it is". The API is answered at the
 * typed client.
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
const START = "https://crm.example.com/signin/start"

const detail = {
  id: APP_ID,
  code: "CRM",
  name: "Customer Desk",
  baseUrl: "https://crm.example.com",
  initiateLoginUri: START,
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
  allowedScopes: [],
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

function renderEdit() {
  return renderWithClient(
    <ApplicationEditDialog
      open
      onOpenChange={() => {}}
      application={{ id: APP_ID, name: "Customer Desk" }}
    />
  )
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

describe("the initiate login URI in the application dialogs", () => {
  it("the edit dialog shows the stored address, next to the base URL, with its hint", async () => {
    renderEdit()

    const field = await screen.findByLabelText("Initiate login URI")
    expect(field).toHaveValue(START)
    expect(screen.getByLabelText("Base URL")).toHaveValue(
      "https://crm.example.com"
    )
    expect(
      screen.getByText(
        /The address in this application that starts its own sign-in/
      )
    ).toBeInTheDocument()
  })

  it("the edit dialog saves an edited address", async () => {
    const user = userEvent.setup()
    renderEdit()

    const field = await screen.findByLabelText("Initiate login URI")
    await user.clear(field)
    await user.type(field, "  https://crm.example.com/start  ")
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]?.body.initiateLoginUri).toBe(
      "https://crm.example.com/start"
    )
  })

  it("the edit dialog sends the stored address back when it was not touched", async () => {
    const user = userEvent.setup()
    renderEdit()

    await screen.findByLabelText("Initiate login URI")
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]?.body.initiateLoginUri).toBe(START)
  })

  it("the edit dialog removes it with an empty string, never null", async () => {
    const user = userEvent.setup()
    renderEdit()

    await user.clear(await screen.findByLabelText("Initiate login URI"))
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]?.body.initiateLoginUri).toBe("")
  })

  it("the create dialog sends the address", async () => {
    const user = userEvent.setup()
    renderWithClient(<ApplicationCreateDialog open onOpenChange={() => {}} />)

    await user.type(await screen.findByLabelText("Code"), "CRM")
    await user.type(screen.getByLabelText("Name"), "Customer Desk")
    await user.type(screen.getByLabelText("Initiate login URI"), START)
    await user.click(screen.getByRole("button", { name: "Create" }))

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0]?.[1]?.body.initiateLoginUri).toBe(START)
  })

  it("the create dialog sends null when the field is left empty", async () => {
    const user = userEvent.setup()
    renderWithClient(<ApplicationCreateDialog open onOpenChange={() => {}} />)

    await user.type(await screen.findByLabelText("Code"), "CRM")
    await user.type(screen.getByLabelText("Name"), "Customer Desk")
    await user.click(screen.getByRole("button", { name: "Create" }))

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    expect(post.mock.calls[0]?.[1]?.body.initiateLoginUri).toBeNull()
  })
})
