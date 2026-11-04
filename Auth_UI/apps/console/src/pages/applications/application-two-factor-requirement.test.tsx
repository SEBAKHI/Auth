import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import {
  cleanup,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * S23, AM11 (11-3): the edit dialog shows how many of the application's recent
 * users have no authenticator app, under the two-factor switch, and switching
 * the requirement on asks first, naming that number. Switching it off asks
 * nothing, and the create dialog shows no count. The API is answered at the
 * typed client.
 */

const get = vi.fn()
const put = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: vi.fn(),
    PUT: (...args: unknown[]) => put(...args),
    DELETE: vi.fn(),
  },
}))

import {
  ApplicationCreateDialog,
  ApplicationEditDialog,
} from "./application-dialogs"

const APP_ID = "33333333-3333-3333-3333-333333333333"

function detail(requireTwoFactor: boolean) {
  return {
    id: APP_ID,
    code: "CRM",
    name: "Customer Desk",
    baseUrl: "https://crm.example.com",
    isActive: true,
    accessMode: "Everyone",
    allowSelfRegistration: false,
    requireTwoFactor,
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
}

const readiness = {
  activeUsers: 12,
  withQualifyingFactor: 9,
  withoutQualifyingFactor: "3",
  windowDays: 30,
}

function ok<T>(data: T) {
  return Promise.resolve({
    data,
    response: new Response(null, { status: 200 }),
  })
}

function answer(requireTwoFactor: boolean, readinessFails = false) {
  get.mockImplementation((path: string) => {
    if (path === "/api/v1/Applications/{id}/roles") return ok([])
    if (path === "/api/v1/Applications/{id}/mfa-readiness") {
      return readinessFails
        ? Promise.resolve({
            error: { status: 500, code: "Http.InternalServerError" },
            response: new Response(null, { status: 500 }),
          })
        : ok(readiness)
    }
    return ok(detail(requireTwoFactor))
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

const READINESS_LINE =
  "Last 30 days: 12 people signed in to this application; 3 have no authenticator app."

beforeEach(async () => {
  await i18n.changeLanguage("en")
  put.mockImplementation(() => ok(detail(true)))
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe("the two-factor requirement in the application dialogs", () => {
  it("the edit dialog shows the readiness of the application's recent users", async () => {
    answer(false)
    renderEdit()

    expect(await screen.findByText(READINESS_LINE)).toBeInTheDocument()
    expect(get).toHaveBeenCalledWith(
      "/api/v1/Applications/{id}/mfa-readiness",
      {
        params: { path: { id: APP_ID } },
      }
    )
  })

  it("switching the requirement on asks first, naming who it would stop, and only then saves it on", async () => {
    answer(false)
    const user = userEvent.setup()
    renderEdit()

    const toggle = await screen.findByRole("switch", {
      name: "Require two-factor",
    })
    await user.click(toggle)

    const confirm = await screen.findByRole("alertdialog")
    expect(
      within(confirm).getByText(
        "Require two-factor authentication for Customer Desk?"
      )
    ).toBeInTheDocument()
    expect(
      within(confirm).getByText(
        /^3 of the 12 people who signed in during the last 30 days/
      )
    ).toBeInTheDocument()
    expect(toggle).not.toBeChecked()

    await user.click(
      within(confirm).getByRole("button", { name: "Require it" })
    )
    await waitFor(() => expect(toggle).toBeChecked())

    await user.click(screen.getByRole("button", { name: "Save" }))
    await waitFor(() => expect(put).toHaveBeenCalled())
    expect(put.mock.calls[0][1].body.requireTwoFactor).toBe(true)
  })

  it("cancelling the confirmation leaves the requirement off", async () => {
    answer(false)
    const user = userEvent.setup()
    renderEdit()

    const toggle = await screen.findByRole("switch", {
      name: "Require two-factor",
    })
    await user.click(toggle)
    const confirm = await screen.findByRole("alertdialog")
    await user.click(within(confirm).getByRole("button", { name: "Cancel" }))

    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    )
    expect(toggle).not.toBeChecked()
  })

  it("switching it off asks nothing", async () => {
    answer(true)
    const user = userEvent.setup()
    renderEdit()

    const toggle = await screen.findByRole("switch", {
      name: "Require two-factor",
    })
    await waitFor(() => expect(toggle).toBeChecked())
    await user.click(toggle)

    expect(toggle).not.toBeChecked()
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
  })

  it("an unreadable count still asks, and says the number could not be read", async () => {
    answer(false, true)
    const user = userEvent.setup()
    renderEdit()

    const toggle = await screen.findByRole("switch", {
      name: "Require two-factor",
    })
    await user.click(toggle)

    const confirm = await screen.findByRole("alertdialog")
    expect(within(confirm).getByText(/could not be read/)).toBeInTheDocument()
    expect(screen.queryByText(READINESS_LINE)).not.toBeInTheDocument()
  })

  it("the create dialog shows no count and asks nothing", async () => {
    answer(false)
    const user = userEvent.setup()
    renderWithClient(<ApplicationCreateDialog open onOpenChange={() => {}} />)

    const toggle = await screen.findByRole("switch", {
      name: "Require two-factor",
    })
    await user.click(toggle)

    expect(toggle).toBeChecked()
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(get).not.toHaveBeenCalledWith(
      "/api/v1/Applications/{id}/mfa-readiness",
      expect.anything()
    )
  })
})
