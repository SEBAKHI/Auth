import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { cleanup, render, screen, waitFor } from "@testing-library/react"
import type { ReactNode } from "react"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The application page saves a logo with a full update that resends every
 * other setting. Two saves can overlap: a dark-mode logo still uploading while
 * the light one is removed. The later save must build on the earlier one's
 * result, not on the application as the page rendered it before either, or it
 * puts the removed logo back (reported by the phase 2 review, on the platform
 * page and here alike).
 */

const APP_ID = "11111111-1111-1111-1111-111111111111"
const application = {
  id: APP_ID,
  code: "crm",
  name: "Example CRM",
  description: "Customer records",
  baseUrl: "https://crm.example.test",
  logoUrl: "uploads/old-light.png",
  logoUrlDark: "uploads/old-dark.png",
  contactEmail: "it@example.test",
  isActive: true,
  accessMode: "Everyone",
  allowSelfRegistration: false,
  requireTwoFactor: true,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 90,
  maxConcurrentSessions: 3,
  reauthenticationMaxAgeMinutes: 30,
}

const put = vi.fn()
const logo: Record<string, unknown> = {}

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (path: string) =>
      Promise.resolve({
        data: path === "/api/v1/Applications/{id}" ? application : { items: [], users: [], totalCount: 0 },
        error: undefined,
      }),
    PUT: (...args: unknown[]) => put(...args),
    POST: vi.fn(),
    DELETE: vi.fn(),
  },
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ hasPermission: () => true, user: { id: "99999999-9999-9999-9999-999999999999" } }),
}))

vi.mock("@authsystem/ui/crumbs", () => ({ usePageBreadcrumb: () => undefined }))
vi.mock("@authsystem/ui/data-table/data-table", () => ({ DataTable: () => null }))
vi.mock("@authsystem/ui/common/detail-list", () => ({ DetailList: () => null }))

// The logo circle's own behaviour has its tests; here it hands over the page's save functions.
vi.mock("@authsystem/ui/common/themed-logo", () => ({
  ThemedLogo: (props: Record<string, unknown>) => {
    Object.assign(logo, props)
    return <div data-testid="themed-logo" />
  },
}))

import { ApplicationDetailPage } from "./application-detail-page"

function renderPage(children?: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/applications/${APP_ID}`]}>
        <Routes>
          <Route path="/applications/:id" element={<ApplicationDetailPage />} />
        </Routes>
        {children}
      </MemoryRouter>
    </QueryClientProvider>
  )
}

type Persist = (logoKey: string | null) => Promise<void>

beforeEach(async () => {
  await i18n.changeLanguage("en")
  // The server stores what it is sent, and answers with the application.
  let stored = { ...application }
  put.mockImplementation((_path: string, init: { body: Record<string, unknown> }) => {
    const { logoUrlDark, ...rest } = init.body
    stored = {
      ...stored,
      ...rest,
      // null leaves the dark-mode logo as it is; "" removes it.
      ...(logoUrlDark === undefined || logoUrlDark === null ? {} : { logoUrlDark: logoUrlDark || null }),
    } as typeof stored
    return Promise.resolve({ data: stored, error: undefined })
  })
})

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
  for (const key of Object.keys(logo)) delete logo[key]
})

describe("application page logo saves", () => {
  it("names the logo circle", async () => {
    renderPage()

    await screen.findByTestId("themed-logo")
    expect(logo.label).toBe("Application logo")
  })

  it("a dark-mode logo still uploading when the light logo is removed does not bring the light logo back", async () => {
    renderPage()
    await screen.findByTestId("themed-logo")

    // The dark-mode upload began here: its save is the one the page offered then.
    const saveDarkWhenUploaded = logo.persistDark as Persist
    // The light logo is removed meanwhile, and that save lands.
    await (logo.persistLight as Persist)(null)
    expect(put).toHaveBeenCalledTimes(1)
    expect(put.mock.calls[0]![1].body.logoUrl).toBeNull()

    // Then the upload finishes.
    await saveDarkWhenUploaded("uploads/new-dark.png")

    await waitFor(() => expect(put).toHaveBeenCalledTimes(2))
    expect(put.mock.calls[1]![1]).toEqual({
      params: { path: { id: APP_ID } },
      body: {
        name: application.name,
        description: application.description,
        baseUrl: application.baseUrl,
        logoUrl: null,
        logoUrlDark: "uploads/new-dark.png",
        contactEmail: application.contactEmail,
        accessMode: "Everyone",
        allowSelfRegistration: false,
        requireTwoFactor: true,
        requireEmailVerification: false,
        sessionTimeoutMinutes: 90,
        maxConcurrentSessions: 3,
        reauthenticationMaxAgeMinutes: 30,
      },
    })
  })

  it("removing the dark-mode logo sends an empty value, which the API reads as removal", async () => {
    renderPage()
    await screen.findByTestId("themed-logo")

    await (logo.persistDark as Persist)(null)

    expect(put.mock.calls[0]![1].body).toMatchObject({
      logoUrl: application.logoUrl,
      logoUrlDark: "",
    })
  })
})
