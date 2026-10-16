import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The application dialogs carry the server-side redirect URIs (OI-171): the
 * list a Backend-for-Frontend's callbacks go in, whose codes only the
 * application's sign-in key may redeem. The edit dialog shows the stored list
 * and saves it beside the public list without touching it; the create dialog
 * sends it too. The API is answered at the typed client.
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

const APP_ID = "11111111-1111-1111-1111-111111111111"
const CALLBACK = "https://shop.example.com/callback"
const SERVER_SIDE = "https://shop.example.com/bff/callback"
const LOCAL_SERVER_SIDE = "http://localhost:5000/bff/callback"

const detail = {
  id: APP_ID,
  code: "SHOP",
  name: "Shop",
  isActive: true,
  accessMode: "Everyone",
  allowSelfRegistration: false,
  requireTwoFactor: false,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 60,
  maxConcurrentSessions: 5,
  reauthenticationMaxAgeMinutes: null,
  redirectUris: [CALLBACK],
  postLogoutRedirectUris: [],
  confidentialRedirectUris: [SERVER_SIDE, LOCAL_SERVER_SIDE],
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

function renderEdit() {
  return renderWithClient(
    <ApplicationEditDialog
      open
      onOpenChange={() => {}}
      application={{ id: APP_ID, name: "Shop" }}
    />
  )
}

describe("server-side redirect URIs in the application dialogs", () => {
  it("the edit dialog shows the stored list, one address per line, and says what the list is for", async () => {
    renderEdit()

    const list = await screen.findByLabelText("Server-side redirect URIs")
    expect(list).toHaveValue(`${SERVER_SIDE}\n${LOCAL_SERVER_SIDE}`)
    expect(screen.getByLabelText("Redirect URIs")).toHaveValue(CALLBACK)
    expect(
      screen.getByText(/can be completed only by that server.*\(private_key_jwt\)/)
    ).toBeInTheDocument()
  })

  it("the edit dialog saves the edited list and leaves the public list as it was", async () => {
    const user = userEvent.setup()
    renderEdit()

    const list = await screen.findByLabelText("Server-side redirect URIs")
    await user.clear(list)
    await user.type(list, `  https://shop.example.com/bff/next  {Enter}{Enter}${LOCAL_SERVER_SIDE}`)
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    const body = put.mock.calls[0]?.[1]?.body
    expect(body.confidentialRedirectUris).toEqual([
      "https://shop.example.com/bff/next",
      LOCAL_SERVER_SIDE,
    ])
    expect(body.redirectUris).toEqual([CALLBACK])
  })

  it("the edit dialog clears the list when every line is removed", async () => {
    const user = userEvent.setup()
    renderEdit()

    await user.clear(await screen.findByLabelText("Server-side redirect URIs"))
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]?.body.confidentialRedirectUris).toEqual([])
  })

  it("the create dialog sends the list", async () => {
    const user = userEvent.setup()
    renderWithClient(<ApplicationCreateDialog open onOpenChange={() => {}} />)

    await user.type(await screen.findByLabelText("Code"), "SHOP")
    await user.type(screen.getByLabelText("Name"), "Shop")
    await user.type(screen.getByLabelText("Server-side redirect URIs"), SERVER_SIDE)
    await user.click(screen.getByRole("button", { name: "Create" }))

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    const body = post.mock.calls[0]?.[1]?.body
    expect(body.confidentialRedirectUris).toEqual([SERVER_SIDE])
    expect(body.redirectUris).toEqual([])
  })
})
