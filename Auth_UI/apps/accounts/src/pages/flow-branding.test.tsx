import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
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
 * application (OI-105): its logo above the title, its name under it, and the
 * platform's mark small in the corner (owner, 2026-10-28). So does every other
 * page that names an application, by the client id in its own address: the
 * sign-out confirmation, the signed-out page and the expired request (owner,
 * 2026-10-29), with the application's appearance and its design too.
 *
 * The sign-in page was the only one that asked for the application's branding:
 * a visitor sent here by an application saw it on the sign-in page and the
 * platform on every screen after, the sign-up screens first among them. Each
 * screen is rendered here with the REAL layout and the REAL branding hook; only
 * the network is answered, at `fetch`, so what is asserted is the header a
 * person sees: the application's name and logo, and the platform's trust marker.
 */

const { get, auth, platform, applicationTheme } = vi.hoisted(() => ({
  get: vi.fn(),
  /** The appearance a page puts on, as the provider would receive it. */
  applicationTheme: vi.fn(),
  platform: {
    isPending: false,
    /** The platform's own design, which its own pages show. */
    loginDesign: undefined as { id: string; contentHash: string; showPlatformLogo: boolean } | undefined,
  },
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
    staySignedInOffered: true,
    loginDesign: platform.loginDesign,
  }),
  BrandingLogo: () => <span data-testid="platform-mark" />,
  // The sign-in options are not yet known: the sign-up page shows its form.
  useSignInOptions: () => null,
  // What the provider then does with it has its own tests (branding.provider.test.tsx).
  useApplicationTheme: (...args: unknown[]) => applicationTheme(...args),
}))
// The panel as the layout fills it: which design, for which application (the
// frame itself has its own tests).
vi.mock("@authsystem/ui/login-design/login-design-panel", () => ({
  default: ({ clientId, design }: { clientId: string | null; design: { id: string } }) => (
    <div data-testid="design-panel" data-client-id={clientId ?? ""} data-design-id={design.id} />
  ),
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
import { toThemeConfig } from "@authsystem/ui/theme/theme-config"
import { ForgotPasswordPage } from "@authsystem/auth/pages/forgot-password"
import { VerifyEmailPage } from "@authsystem/auth/pages/verify-email-page"

import { AccountsLoginPage } from "./auth/login"
import { RegisterPage } from "./auth/register"
import { RegisterVerifyPage } from "./auth/register-verify"
import {
  clearRegistrationFlow,
  savePendingRegistration,
} from "./auth/registration-flow"
import { AccountsTwoFactorPage } from "./auth/two-factor"
import { CreateOrganizationPage } from "./create-organization"
import { LogoutConfirmPage } from "./logout"
import { SignInExpiredPage } from "./sign-in-expired"
import { SignedOutPage } from "./signed-out"

const LOGO_URL = `${API_BASE_URL}/uploads/images/shop-logo.webp`
const AUTHORIZE = `${API_BASE_URL}/api/v1/auth/authorize?client_id=shop&state=xyz`
// Everything a crafted link could add to the request. Only the client id is
// ever read from it; the name and logo come from the endpoint alone.
const CRAFTED = `${AUTHORIZE}&name=Evil&logo=${encodeURIComponent("https://evil.example/x.png")}`

// OI-145: the shape the accounts pages receive since handles. A stored request
// is the resume URL inside the app and `?client_id&auth_request` on the page.
const HANDLE = "0123456789abcdef".repeat(4)
const RESUME = `${API_BASE_URL}/api/v1/auth/authorize/resume?client_id=shop&auth_request=${HANDLE}`
const HANDLE_QUERY = `?client_id=shop&auth_request=${HANDLE}`

/** The application's own appearance, as its public branding sends it. */
const APPEARANCE = {
  base: { preset: "neutral" },
  theme: { preset: "violet" },
  chart: { preset: "cyan" },
  radius: "small",
  menuAccent: "bold",
  controlSize: { preset: "custom", px: 44 },
  tableRowHeight: { preset: "comfortable" },
}
const DESIGN = { id: "app-design", contentHash: "a".repeat(64), showPlatformLogo: true }
const PLATFORM_DESIGN = { id: "platform-design", contentHash: "b".repeat(64), showPlatformLogo: true }

const query = (returnTo: string | null) =>
  !returnTo
    ? ""
    : returnTo === RESUME
      ? HANDLE_QUERY
      : `?returnTo=${encodeURIComponent(returnTo)}`

/**
 * A page that names its application by client id carries what the link would
 * have carried on its own address: the client id, and anything a crafted link
 * adds beside it.
 */
const clientIdQuery = (returnTo: string | null) =>
  returnTo ? `?${new URL(returnTo).searchParams.toString()}` : ""
const inFuture = () => new Date(Date.now() + 5 * 60_000).toISOString()

interface FlowPage {
  name: string
  path: string
  element: React.ReactElement
  heading: string
  /**
   * Where this screen reads the pending request from, as the flow hands it
   * over; `client_id`: a page with no request, naming its application by the
   * client id of its own address.
   */
  carries: "query" | "state" | "client_id"
  state?: Record<string, unknown>
  setup?: () => void
  /** What `GET /Users/me` answers; "pending" never answers. */
  me?: Record<string, unknown> | "pending"
  /** What other `GET`s of the screen answer, by path; anything else never answers. */
  answers?: Record<string, unknown>
  /**
   * The line under the title that names the application: "Continue to SHOP."
   * unless the screen names it in a subtitle of its own, which that line would
   * only repeat. Null: a page with no request, where the visitor continues to
   * nothing, so no such line; its logo names the application.
   */
  namesApp?: string | null
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
    heading: "Check your email",
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
    heading: "2-Step Verification",
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
  {
    // The last step of the flow, open: a signed-in person who may create one.
    name: "create organization",
    path: "/create-organization",
    element: <CreateOrganizationPage />,
    heading: "Create your organization",
    carries: "query",
    answers: {
      "/api/v1/Auth/organization-setup": {
        canCreate: true,
        limit: 1,
        ownedOrganizations: [],
        email: "jane@one.example",
        emailConfirmed: true,
      },
    },
    namesApp:
      "SHOP works with organizations. Name the company or institution you use it for.",
  },
  {
    // A relying party's sign-out, before the person confirms it.
    name: "sign-out confirmation",
    path: "/logout",
    element: <LogoutConfirmPage />,
    heading: "End your sign-in to your account on this browser?",
    carries: "client_id",
    namesApp: null,
  },
  {
    name: "signed out",
    path: "/signed-out",
    element: <SignedOutPage />,
    heading: "Your sign-in on this browser has ended",
    carries: "client_id",
    namesApp: null,
  },
  {
    // The body names the application; there is nothing to continue to.
    name: "sign-in expired",
    path: "/sign-in-expired",
    element: <SignInExpiredPage />,
    heading: "Sign-in request expired",
    carries: "client_id",
    namesApp:
      "The sign-in request from SHOP has expired or is no longer valid. Start again from SHOP: if you have already signed in, it will find you signed in.",
  },
]

/** The screens that can carry a pending request, as opposed to naming a client id. */
const REQUEST_PAGES = PAGES.filter((page) => page.carries !== "client_id")

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
      : page.answers && path in page.answers
        ? Promise.resolve({ data: page.answers[path] })
        : new Promise(() => {})
  )
  const entry =
    page.carries === "client_id"
      ? { pathname: page.path, search: clientIdQuery(returnTo) }
      : page.carries === "query"
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

/** The answer of the application's public branding; null: a 404. */
const answering = (body: object | null) => () =>
  Promise.resolve(
    body
      ? ({ ok: true, json: () => Promise.resolve(body) } as Response)
      : ({ ok: false, status: 404 } as Response)
  )

/** A screen wide enough for the panel (`lg`), which jsdom's own shim never matches. */
function widePanel() {
  vi.spyOn(window, "matchMedia").mockImplementation(
    (media) =>
      ({
        media,
        matches: media === "(min-width: 1024px)",
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        addListener: () => {},
        removeListener: () => {},
        dispatchEvent: () => false,
      }) as MediaQueryList
  )
}

describe("the application header on every screen of the flow", () => {
  let fetch: MockInstance<typeof globalThis.fetch>

  beforeEach(() => {
    clearRegistrationFlow()
    get.mockReset()
    applicationTheme.mockReset()
    platform.isPending = false
    platform.loginDesign = undefined
    fetch = vi.spyOn(globalThis, "fetch").mockImplementation(() =>
      Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ name: "SHOP", logoUrl: LOGO_URL }),
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
      "/forgot-password",
      "/two-factor",
      "/verify-email",
      "/force-password-change",
      "/force-password-change",
      "/force-password-change",
      "/create-organization",
      "/logout",
      "/signed-out",
      "/sign-in-expired",
    ])
  })

  it.each(PAGES)(
    "$name shows the application's name, logo and the trust marker",
    async (page) => {
      renderPage(page, AUTHORIZE)

      const logo = await screen.findByRole("img", { name: "SHOP" })
      expect(logo).toHaveAttribute("src", LOGO_URL)
      expect(screen.getByRole("main")).toContainElement(logo)
      if (page.namesApp !== null) {
        expect(
          screen.getByText(page.namesApp ?? "Continue to SHOP.")
        ).toBeInTheDocument()
      }
      if (page.namesApp !== undefined) {
        expect(screen.queryByText("Continue to SHOP.")).toBeNull()
      }
      expect(screen.getByText("Secured by AuthSystem")).toBeInTheDocument()
      expect(screen.getByRole("banner")).toContainElement(
        screen.getByTestId("platform-mark")
      )
      // The screen keeps its own title; only the header changed.
      expect(
        screen.getByRole("heading", { level: 1, name: page.heading })
      ).toBeInTheDocument()
      expect(screen.queryByTestId("elsewhere")).toBeNull()
      expect(brandingRequests(fetch)).toEqual([
        `${API_BASE_URL}/api/v1/applications/shop/public-branding`,
      ])
    }
  )

  it.each(PAGES)(
    "$name follows the application's design: one that hides the platform's mark gives the corner to the application's logo",
    async (page) => {
      // The design reaches a page only through the flow's header: a screen that
      // builds its header by hand shows the platform's design and corner instead.
      fetch.mockImplementation(() =>
        Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              name: "SHOP",
              logoUrl: LOGO_URL,
              loginDesign: { id: "d1", contentHash: "a".repeat(64), showPlatformLogo: false },
            }),
        } as Response)
      )
      renderPage(page, AUTHORIZE)

      const logo = await screen.findByRole("img", { name: "SHOP" })
      expect(screen.getByRole("banner")).toContainElement(logo)
      expect(screen.queryByTestId("platform-mark")).toBeNull()
      expect(screen.getByText("Secured by AuthSystem")).toBeInTheDocument()
    }
  )

  it.each(PAGES)(
    "$name takes the name and logo from the endpoint, never from the link",
    async (page) => {
      const { container } = renderPage(page, CRAFTED)

      const logo = await screen.findByRole("img", { name: "SHOP" })
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
    "$name takes the application's own appearance",
    async (page) => {
      fetch.mockImplementation(answering({ name: "SHOP", logoUrl: LOGO_URL, theme: APPEARANCE }))
      renderPage(page, AUTHORIZE)

      await screen.findByRole("img", { name: "SHOP" })
      // Registered while the answer is on its way (unknown, so the cached one
      // stands), then the application's whole appearance in place of the
      // platform's; never another application's.
      expect(applicationTheme).toHaveBeenCalledWith("shop", undefined)
      expect(applicationTheme).toHaveBeenLastCalledWith("shop", toThemeConfig(APPEARANCE))
      expect(applicationTheme.mock.calls.map(([clientId]) => clientId)).toEqual(
        applicationTheme.mock.calls.map(() => "shop")
      )
    }
  )

  it.each(PAGES)(
    "$name shows the application's design in its panel, never the platform's own",
    async (page) => {
      widePanel()
      platform.loginDesign = PLATFORM_DESIGN
      fetch.mockImplementation(answering({ name: "SHOP", logoUrl: LOGO_URL, loginDesign: DESIGN }))
      renderPage(page, AUTHORIZE)

      // Rendered for the application, so the server renders it with its name
      // (its own design, else the platform's, as its branding says).
      const panel = await screen.findByTestId("design-panel")
      expect(panel).toHaveAttribute("data-client-id", "shop")
      expect(panel).toHaveAttribute("data-design-id", DESIGN.id)
    }
  )

  it.each(PAGES)(
    "$name is the platform's own for an application the server does not know or has switched off",
    async (page) => {
      widePanel()
      platform.loginDesign = PLATFORM_DESIGN
      fetch.mockImplementation(answering(null))
      const { container } = renderPage(page, AUTHORIZE)

      await waitFor(() => expect(applicationTheme).toHaveBeenLastCalledWith("shop", null))
      const panel = await screen.findByTestId("design-panel")
      expect(panel).toHaveAttribute("data-client-id", "")
      expect(panel).toHaveAttribute("data-design-id", PLATFORM_DESIGN.id)
      expect(screen.getByTestId("platform-mark")).toBeInTheDocument()
      expect(container.querySelector("img")).toBeNull()
      expect(screen.queryByText(/Secured by/)).toBeNull()
      expect(screen.queryByText(/Continue to/)).toBeNull()
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

    expect(await screen.findByRole("img", { name: "SHOP" })).toBeInTheDocument()
    expect(screen.queryByText(/Secured by/)).toBeNull()
  })

  it.each(REQUEST_PAGES)(
    "$name shows the application for a stored request, too (OI-145)",
    async (page) => {
      renderPage(page, RESUME)

      const logo = await screen.findByRole("img", { name: "SHOP" })
      expect(logo).toHaveAttribute("src", LOGO_URL)
      expect(screen.queryByTestId("elsewhere")).toBeNull()
      expect(brandingRequests(fetch)).toEqual([
        `${API_BASE_URL}/api/v1/applications/shop/public-branding`,
      ])
    }
  )

  it("sends Forgot password? and Sign up on with a stored request as client_id and its handle (OI-145)", async () => {
    renderPage(PAGES[0], RESUME)
    await screen.findByRole("img", { name: "SHOP" })

    expect(
      screen.getByRole("link", { name: "Forgot password?" })
    ).toHaveAttribute("href", `/forgot-password${HANDLE_QUERY}`)
    expect(screen.getByRole("link", { name: "Sign up" })).toHaveAttribute(
      "href",
      `/register${HANDLE_QUERY}`
    )
    // No in-app link carries a query value that holds an address.
    const inApp = screen
      .getAllByRole("link")
      .map((link) => link.getAttribute("href") ?? "")
      .filter((href) => href.startsWith("/"))
    expect(inApp.length).toBeGreaterThan(1)
    for (const href of inApp) {
      const values = [
        ...new URL(href, "https://accounts.test").searchParams.values(),
      ]
      expect(values.filter((value) => value.includes("://"))).toEqual([])
    }
  })

  it("sends Forgot password? on with the pending request, and with nothing else", async () => {
    const withRequest = renderPage(PAGES[0], AUTHORIZE)
    await screen.findByRole("img", { name: "SHOP" })
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
