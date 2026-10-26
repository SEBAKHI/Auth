import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import type * as React from "react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

/**
 * OI-150: every accounts page that holds a step of a sign-in reloads when the
 * browser brings it back from the back/forward cache, so the server decides
 * again instead of the frozen page. Each page is rendered as its route reaches
 * it, then shown again the way the cache shows it: a `pageshow` event with
 * `persisted` set.
 */

const { get, auth } = vi.hoisted(() => ({
  get: vi.fn(),
  auth: {
    status: "unauthenticated" as const,
    login: vi.fn(),
    completeRegistration: vi.fn(),
    completeTwoFactor: vi.fn(),
    completeEmailVerification: vi.fn(),
  },
}))

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    POST: vi.fn(() => new Promise(() => {})),
  },
}))
vi.mock("@authsystem/auth/auth-context", () => ({ useAuth: () => auth }))
vi.mock("@authsystem/auth/external/external-providers", () => ({
  ExternalProviders: () => null,
}))
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ name: "AuthSystem", logoUrl: null, isPending: false, staySignedInOffered: true }),
  BrandingLogo: () => null,
  // The sign-in options are not yet known: the sign-up page shows its form.
  useSignInOptions: () => null,
}))
vi.mock("@authsystem/ui/common/language-toggle", () => ({
  LanguageToggle: () => null,
}))
vi.mock("@authsystem/ui/common/theme-toggle", () => ({
  ThemeToggle: () => null,
}))

import { CreateOrganizationPage } from "./create-organization"
import { AccountsLoginPage } from "./auth/login"
import { RegisterPage } from "./auth/register"
import { RegisterVerifyPage } from "./auth/register-verify"
import {
  clearRegistrationFlow,
  savePendingRegistration,
} from "./auth/registration-flow"
import { AccountsTwoFactorPage } from "./auth/two-factor"

const HANDLE_QUERY = `?client_id=crm&auth_request=${"0123456789abcdef".repeat(4)}`

interface StepPage {
  name: string
  path: string
  element: React.ReactElement
  search?: string
  state?: Record<string, unknown>
  setup?: () => void
}

/** The pages design item 8 lists: sign-in, sign-up and its code, the second factor, the organization. */
const PAGES: StepPage[] = [
  {
    name: "sign-in",
    path: "/login",
    element: <AccountsLoginPage />,
    search: HANDLE_QUERY,
  },
  {
    name: "sign-up",
    path: "/register",
    element: <RegisterPage />,
    search: HANDLE_QUERY,
  },
  {
    name: "sign-up: the code",
    path: "/register/verify",
    element: <RegisterVerifyPage />,
    search: HANDLE_QUERY,
    setup: () =>
      savePendingRegistration({
        pendingId: "pending-1",
        email: "jane@one.example",
        maskedEmail: "j***@one.example",
        expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
      }),
  },
  {
    name: "second factor",
    path: "/two-factor",
    element: <AccountsTwoFactorPage />,
    state: { challengeToken: "challenge-1" },
  },
  {
    name: "organization",
    path: "/create-organization",
    element: <CreateOrganizationPage />,
    search: HANDLE_QUERY,
  },
]

function Elsewhere() {
  const location = useLocation()
  return <div data-testid="elsewhere">{location.pathname}</div>
}

function renderPage(page: StepPage) {
  page.setup?.()
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter
        initialEntries={[
          { pathname: page.path, search: page.search ?? "", state: page.state },
        ]}
      >
        <Routes>
          <Route path={page.path} element={page.element} />
          <Route path="*" element={<Elsewhere />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const show = (persisted: boolean) =>
  window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted }))

describe("a sign-in step brought back by the Back button", () => {
  const reload = vi.fn()

  beforeEach(() => {
    clearRegistrationFlow()
    reload.mockReset()
    get.mockReset()
    get.mockImplementation(() => new Promise(() => {}))
    vi.stubGlobal("location", {
      href: "https://accounts.example.com/",
      origin: "https://accounts.example.com",
      assign: vi.fn(),
      replace: vi.fn(),
      reload,
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it("covers every listed page", () => {
    // A guard against an empty table passing every case below vacuously.
    expect(PAGES.map((page) => page.path)).toEqual([
      "/login",
      "/register",
      "/register/verify",
      "/two-factor",
      "/create-organization",
    ])
  })

  it.each(PAGES)(
    "$name reloads when restored from the back/forward cache",
    (page) => {
      renderPage(page)
      // Still on the page: a redirect would take the listener with it.
      expect(screen.queryByTestId("elsewhere")).toBeNull()

      show(true)

      expect(reload).toHaveBeenCalledTimes(1)
    }
  )

  it.each(PAGES)("$name does not reload on an ordinary load", (page) => {
    renderPage(page)

    show(false)

    expect(reload).not.toHaveBeenCalled()
  })

  it.each(PAGES)("$name stops listening once it is gone", (page) => {
    const { unmount } = renderPage(page)
    unmount()

    show(true)

    expect(reload).not.toHaveBeenCalled()
  })
})
