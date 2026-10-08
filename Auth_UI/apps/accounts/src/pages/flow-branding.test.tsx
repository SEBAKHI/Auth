import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen } from "@testing-library/react"
import type * as React from "react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import {
  afterEach,
  beforeEach,
  describe,
  expect,
  it,
  type MockInstance,
  vi,
} from "vitest"

import "@authsystem/i18n"

/**
 * Every screen a pending authorize request passes through shows the requesting
 * application, not the platform (OI-105).
 *
 * The sign-in page was the only one that asked for the application's branding:
 * a visitor sent here by an application saw it on the sign-in page and the
 * platform on every screen after, the sign-up screens first among them. Each
 * screen is rendered here with the REAL layout and the REAL branding hook; only
 * the network is answered, at `fetch`, so what is asserted is the header a
 * person sees: the application's name and logo, and the platform's trust marker.
 */

const { get, auth, platform } = vi.hoisted(() => ({
  get: vi.fn(),
  platform: { isPending: false },
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
// The platform's own branding is settled; its mark stands in for the platform
// header, so "no application" is something the test can see.
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({
    name: "AuthSystem",
    logoUrl: null,
    isPending: platform.isPending,
  }),
  BrandingLogo: () => <span data-testid="platform-mark" />,
}))
// Chrome only: the toggles need the theme and language provider stack.
vi.mock("@authsystem/ui/common/language-toggle", () => ({
  LanguageToggle: () => null,
}))
vi.mock("@authsystem/ui/common/theme-toggle", () => ({
  ThemeToggle: () => null,
}))

import { API_BASE_URL } from "@authsystem/api/env"
import { ForcePasswordChangePage } from "@authsystem/auth/pages/force-password-change"
import { ForgotPasswordPage } from "@authsystem/auth/pages/forgot-password"
import { VerifyEmailPage } from "@authsystem/auth/pages/verify-email-page"

import { AccountsLoginPage } from "./auth/login"
import { RegisterPage } from "./auth/register"
import { RegisterCompletePage } from "./auth/register-complete"
import { RegisterVerifyPage } from "./auth/register-verify"
import {
  clearRegistrationFlow,
  savePendingRegistration,
  setVerifiedCode,
} from "./auth/registration-flow"
import { AccountsTwoFactorPage } from "./auth/two-factor"

const LOGO_URL = `${API_BASE_URL}/uploads/images/edis-logo.webp`
const AUTHORIZE = `${API_BASE_URL}/api/v1/auth/authorize?client_id=edis&state=xyz`
// Everything a crafted link could add to the request. Only the client id is
// ever read from it; the name and logo come from the endpoint alone.
const CRAFTED = `${AUTHORIZE}&name=Evil&logo=${encodeURIComponent("https://evil.example/x.png")}`

const query = (returnTo: string | null) =>
  returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : ""
const inFuture = () => new Date(Date.now() + 5 * 60_000).toISOString()

interface FlowPage {
  name: string
  path: string
  element: React.ReactElement
  heading: string
  /** Where this screen reads the pending request from, as the flow hands it over. */
  carries: "query" | "state"
  state?: Record<string, unknown>
  setup?: () => void
  /** What `GET /Users/me` answers; "pending" never answers. */
  me?: Record<string, unknown> | "pending"
}

/**
 * Every screen in scope, as its route reaches it. A screen that cannot hold a
 * pending request today (the S08 second-factor screen, account recovery) is
 * absent on purpose: see the PR's table.
 */
const PAGES: FlowPage[] = [
  {
    name: "sign-in (the reference)",
    path: "/login",
    element: <AccountsLoginPage />,
    heading: "Sign in",
    carries: "query",
  },
  {
    name: "register",
    path: "/register",
    element: <RegisterPage />,
    heading: "Create your account",
    carries: "query",
  },
  {
    name: "register: the code",
    path: "/register/verify",
    element: <RegisterVerifyPage />,
    heading: "Confirm email",
    carries: "query",
    setup: () =>
      savePendingRegistration({
        pendingId: "handle-1",
        email: "jane@one.example",
        maskedEmail: "j***@one.example",
        expiresAt: inFuture(),
      }),
  },
  {
    name: "register: name and password",
    path: "/register/complete",
    element: <RegisterCompletePage />,
    heading: "Set up your account",
    carries: "query",
    setup: () => {
      savePendingRegistration({
        pendingId: "handle-1",
        email: "jane@one.example",
        maskedEmail: "j***@one.example",
        expiresAt: inFuture(),
      })
      setVerifiedCode("123456")
    },
  },
  {
    name: "forgot password",
    path: "/forgot-password",
    element: <ForgotPasswordPage />,
    heading: "Reset your password",
    carries: "query",
  },
  {
    name: "two-factor code",
    path: "/two-factor",
    element: <AccountsTwoFactorPage />,
    heading: "Two-factor authentication",
    carries: "state",
    state: { challengeToken: "challenge-1" },
  },
  {
    name: "email verification",
    path: "/verify-email",
    element: <VerifyEmailPage />,
    heading: "Confirm email",
    carries: "state",
    state: { email: "jane@one.example", expiresAt: inFuture() },
  },
  {
    name: "forced password change",
    path: "/force-password-change",
    element: <ForcePasswordChangePage />,
    heading: "Update your password",
    carries: "state",
  },
  {
    name: "forced password change, while the account loads",
    path: "/force-password-change",
    element: <ForcePasswordChangePage />,
    heading: "Update your password",
    carries: "state",
    me: "pending",
  },
  {
    name: "forced password change, for an account with no password",
    path: "/force-password-change",
    element: <ForcePasswordChangePage />,
    heading: "Set a password",
    carries: "state",
    me: { hasPassword: false, email: "jane@one.example" },
  },
]

function Elsewhere() {
  const location = useLocation()
  return <div data-testid="elsewhere">{location.pathname}</div>
}

function renderPage(page: FlowPage, returnTo: string | null) {
  page.setup?.()
  const me = page.me ?? { hasPassword: true, email: "jane@one.example" }
  get.mockImplementation((path: string) =>
    path === "/api/v1/Users/me" && me !== "pending"
      ? Promise.resolve({ data: me })
      : new Promise(() => {})
  )
  const entry =
    page.carries === "query"
      ? { pathname: page.path, search: query(returnTo), state: page.state }
      : { pathname: page.path, state: { ...page.state, returnTo } }
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path={page.path} element={page.element} />
          <Route path="*" element={<Elsewhere />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const brandingRequests = (fetch: MockInstance<typeof globalThis.fetch>) =>
  fetch.mock.calls.map(([url]) => String(url))

describe("the application header on every screen of the flow", () => {
  let fetch: MockInstance<typeof globalThis.fetch>

  beforeEach(() => {
    clearRegistrationFlow()
    get.mockReset()
    platform.isPending = false
    fetch = vi.spyOn(globalThis, "fetch").mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ name: "EDIS", logoUrl: LOGO_URL }),
      } as Response)
    )
  })

  afterEach(() => vi.restoreAllMocks())

  it("covers every screen in scope", () => {
    // A guard against an empty table passing every case below vacuously.
    expect(PAGES.map((page) => page.path)).toEqual([
      "/login",
      "/register",
      "/register/verify",
      "/register/complete",
      "/forgot-password",
      "/two-factor",
      "/verify-email",
      "/force-password-change",
      "/force-password-change",
      "/force-password-change",
    ])
  })

  it.each(PAGES)(
    "$name shows the application's name, logo and the trust marker",
    async (page) => {
      renderPage(page, AUTHORIZE)

      const logo = await screen.findByRole("img", { name: "EDIS" })
      expect(logo).toHaveAttribute("src", LOGO_URL)
      expect(screen.getByText("Secured by AuthSystem")).toBeInTheDocument()
      expect(screen.queryByTestId("platform-mark")).toBeNull()
      // The screen keeps its own title; only the header changed.
      expect(
        screen.getByRole("heading", { level: 1, name: page.heading })
      ).toBeInTheDocument()
      expect(screen.queryByTestId("elsewhere")).toBeNull()
      expect(brandingRequests(fetch)).toEqual([
        `${API_BASE_URL}/api/v1/applications/edis/public-branding`,
      ])
    }
  )

  it.each(PAGES)(
    "$name takes the name and logo from the endpoint, never from the link",
    async (page) => {
      const { container } = renderPage(page, CRAFTED)

      const logo = await screen.findByRole("img", { name: "EDIS" })
      expect(logo).toHaveAttribute("src", LOGO_URL)
      // What is shown: one image, the endpoint's, and none of the link's words.
      // (The links on the page still carry the request on unchanged, as they
      // must, so the crafted URL may sit in an href.)
      expect(container.querySelectorAll("img")).toHaveLength(1)
      expect(container.textContent).not.toContain("Evil")
      expect(container.textContent).not.toContain("evil.example")
    }
  )

  it.each(PAGES)(
    "$name shows the platform mark exactly as before without a pending request",
    async (page) => {
      const { container } = renderPage(page, null)

      expect(
        await screen.findByRole("heading", { level: 1, name: page.heading })
      ).toBeInTheDocument()
      // Let anything the page would start on mount settle before asserting
      // that nothing was asked for and nothing changed.
      await new Promise((resolve) => setTimeout(resolve, 50))
      expect(fetch).not.toHaveBeenCalled()
      expect(screen.getByTestId("platform-mark")).toBeInTheDocument()
      expect(container.querySelector("img")).toBeNull()
      expect(screen.queryByText(/Secured by/)).toBeNull()
    }
  )

  it("names no platform in the trust marker until the platform's own branding is known", async () => {
    platform.isPending = true
    renderPage(PAGES[1], AUTHORIZE)

    expect(await screen.findByRole("img", { name: "EDIS" })).toBeInTheDocument()
    expect(screen.queryByText(/Secured by/)).toBeNull()
  })

  it("sends Forgot password? on with the pending request, and with nothing else", async () => {
    const withRequest = renderPage(PAGES[0], AUTHORIZE)
    await screen.findByRole("img", { name: "EDIS" })
    expect(
      screen.getByRole("link", { name: "Forgot password?" })
    ).toHaveAttribute("href", `/forgot-password${query(AUTHORIZE)}`)
    withRequest.unmount()

    // The console's own sign-in has no request: the link is exactly as before.
    renderPage(PAGES[0], null)
    expect(
      screen.getByRole("link", { name: "Forgot password?" })
    ).toHaveAttribute("href", "/forgot-password")
  })
})
