import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The application dialogs carry the post-logout redirect URIs (OI-151): the
 * edit dialog shows the stored list, edits it one address per line, and saves
 * it beside the redirect URIs without touching them; the create dialog sends
 * it too. The API is answered at the typed client.
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
const SIGNED_OUT = "https://shop.example.com/signed-out"
const LOCAL_SIGNED_OUT = "http://localhost:3000/signed-out"

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
  postLogoutRedirectUris: [SIGNED_OUT, LOCAL_SIGNED_OUT],
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

describe("post-logout redirect URIs in the application dialogs", () => {
  it("the edit dialog shows the stored list, one address per line, with its hint", async () => {
    renderWithClient(
      <ApplicationEditDialog
        open
        onOpenChange={() => {}}
        application={{ id: APP_ID, name: "Shop" }}
      />
    )

    const list = await screen.findByLabelText("Post-logout redirect URIs")
    expect(list).toHaveValue(`${SIGNED_OUT}\n${LOCAL_SIGNED_OUT}`)
    expect(screen.getByLabelText("Redirect URIs")).toHaveValue(CALLBACK)
    expect(
      screen.getByText(
        /post_logout_redirect_uri\), the browser is sent back only to an address listed here/
      )
    ).toBeInTheDocument()
  })

  it("the edit dialog saves the edited list and leaves the redirect URIs as they were", async () => {
    const user = userEvent.setup()
    renderWithClient(
      <ApplicationEditDialog
        open
        onOpenChange={() => {}}
        application={{ id: APP_ID, name: "Shop" }}
      />
    )

    const list = await screen.findByLabelText("Post-logout redirect URIs")
    await user.clear(list)
    await user.type(
      list,
      `  https://shop.example.com/goodbye  {Enter}{Enter}${LOCAL_SIGNED_OUT}`
    )
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    const body = put.mock.calls[0]?.[1]?.body
    expect(body.postLogoutRedirectUris).toEqual([
      "https://shop.example.com/goodbye",
      LOCAL_SIGNED_OUT,
    ])
    expect(body.redirectUris).toEqual([CALLBACK])
  })

  it("the edit dialog clears the list when every line is removed", async () => {
    const user = userEvent.setup()
    renderWithClient(
      <ApplicationEditDialog
        open
        onOpenChange={() => {}}
        application={{ id: APP_ID, name: "Shop" }}
      />
    )

    await user.clear(await screen.findByLabelText("Post-logout redirect URIs"))
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1))
    expect(put.mock.calls[0]?.[1]?.body.postLogoutRedirectUris).toEqual([])
  })

  it("the create dialog sends the list", async () => {
    const user = userEvent.setup()
    renderWithClient(<ApplicationCreateDialog open onOpenChange={() => {}} />)

    await user.type(await screen.findByLabelText("Code"), "SHOP")
    await user.type(screen.getByLabelText("Name"), "Shop")
    await user.type(
      screen.getByLabelText("Post-logout redirect URIs"),
      SIGNED_OUT
    )
    await user.click(screen.getByRole("button", { name: "Create" }))

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
    const body = post.mock.calls[0]?.[1]?.body
    expect(body.postLogoutRedirectUris).toEqual([SIGNED_OUT])
    expect(body.redirectUris).toEqual([])
  })
})
