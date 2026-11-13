import type * as React from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import type { SignInOptions } from "@authsystem/ui/branding"

/**
 * S13 (D4): the sign-in page keeps ONE bot check. Its widget is made for the
 * request the page sends next: the code start from the address step, then the
 * password the code sign-in still needs. The real `useBotCheck` runs here, under
 * Enforce, with `window.turnstile` replaced by a recorder.
 */

const mocks = vi.hoisted(() => ({
  login: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  get: vi.fn(),
  post: vi.fn(),
  toastError: vi.fn(),
  branding: { signInOptions: undefined as SignInOptions | undefined },
}))

vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({
    name: "AuthSystem",
    logoUrl: null,
    isPending: false,
    staySignedInOffered: true,
    ...mocks.branding,
  }),
}))
vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => mocks.get(...args),
    POST: (...args: unknown[]) => mocks.post(...args),
  },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ login: mocks.login }),
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: null,
    complete: mocks.complete,
    challenge: mocks.challenge,
    interstitial: vi.fn(),
  }),
}))
vi.mock("../flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: null,
    layout: { appName: null, appLogoUrl: null, appLogoUrlDark: null, securedBy: null },
  }),
}))
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "en" } }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))
vi.mock("sonner", () => ({
  toast: { error: mocks.toastError, success: vi.fn() },
}))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({ title, children }: { title: string; children: React.ReactNode }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
}))
// The code screens are email-code-flow.test.tsx's: here, the way to the password step.
vi.mock("./email-code-flow", () => ({
  EmailCodeFlow: (props: { onPasswordRequired: (email: string) => void }) => (
    <button type="button" onClick={() => props.onPasswordRequired("jane@one.example")}>
      code flow
    </button>
  ),
}))

import type { TurnstileApi, TurnstileRenderOptions } from "../bot-defense/turnstile-script"
import { forgetTurnstileForTests } from "../bot-defense/turnstile-script"

const { LoginPage } = await import("./login")

const SITE_KEY = "1x00000000000000000000AA"

const PASSWORD_FIRST: SignInOptions = {
  codeSignInOffered: true,
  passkeySignInOffered: false,
  primaryMethod: "password",
  selfRegistrationOpen: true,
  passwordAtSignUp: true,
  nameAtSignUp: false,
}

interface FakeWidget {
  id: string
  options: TurnstileRenderOptions
  resets: number
  removed: boolean
}

let widgets: FakeWidget[] = []
let mostAtOnce = 0
const live = () => widgets.filter((widget) => !widget.removed)

const fakeTurnstile: TurnstileApi = {
  render: (_container, options) => {
    const widget = { id: `w${widgets.length + 1}`, options, resets: 0, removed: false }
    widgets.push(widget)
    mostAtOnce = Math.max(mostAtOnce, live().length)
    return widget.id
  },
  reset: (id) => {
    const widget = widgets.find((candidate) => candidate.id === id)
    if (widget) widget.resets += 1
  },
  remove: (id) => {
    const widget = widgets.find((candidate) => candidate.id === id)
    if (widget) widget.removed = true
  },
}

async function liveWidgetFor(action: string) {
  await waitFor(() => {
    expect(live()).toHaveLength(1)
    expect(live()[0].options.action).toBe(action)
  })
  return live()[0]
}

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/login"]}>
        <LoginPage />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const CODE_STARTED = {
  data: { pendingId: "handle-1", maskedEmail: "j***@one.example", expiresAt: "2099-09-10T10:05:00.000Z" },
  response: new Response(null, { status: 200 }),
}

describe("LoginPage: one bot check for the page (S13, D4)", () => {
  beforeEach(() => {
    window.localStorage.clear()
    window.sessionStorage.clear()
    widgets = []
    mostAtOnce = 0
    window.turnstile = fakeTurnstile
    forgetTurnstileForTests()
    for (const mock of [mocks.login, mocks.complete, mocks.challenge, mocks.get, mocks.post, mocks.toastError]) {
      mock.mockReset()
    }
    mocks.branding.signInOptions = PASSWORD_FIRST
    mocks.get.mockImplementation(async (path: string) => {
      if (path !== "/api/v1/Platform/bot-defense") throw new Error(`unexpected GET ${path}`)
      return { data: { mode: "Enforce", siteKey: SITE_KEY }, error: undefined }
    })
  })

  afterEach(() => {
    delete window.turnstile
  })

  it("serves the code start from the address step, then the password step, one widget at a time", async () => {
    mocks.post.mockResolvedValue(CODE_STARTED)
    mocks.login.mockResolvedValue({ status: "authenticated", requiresPasswordChange: false })
    renderPage()
    const user = userEvent.setup()

    // The address step, password first: the next request is a password sign-in.
    await liveWidgetFor("login")
    expect(screen.getAllByTestId("bot-check")).toHaveLength(1)

    // The code instead: the same check, made again for the code start.
    await user.click(screen.getByRole("button", { name: "auth.signInWithEmailCode" }))
    const forCode = await liveWidgetFor("email-code")
    act(() => forCode.options.callback("code-token"))
    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.click(screen.getByRole("button", { name: "auth.continueWithEmail" }))

    await user.click(await screen.findByRole("button", { name: "code flow" }))
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/Auth/email-code/start", {
      body: { email: "jane@one.example", preferredLanguage: "en", botToken: "code-token" },
    })

    // The password the code sign-in still needs: made for a sign-in.
    const forLogin = await liveWidgetFor("login")
    act(() => forLogin.options.callback("login-token"))
    await user.type(screen.getByLabelText("auth.password"), "pw")
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))

    await waitFor(() => expect(mocks.complete).toHaveBeenCalled())
    expect(mocks.login).toHaveBeenCalledWith("jane@one.example", "pw", false, "login-token")
    expect(mostAtOnce).toBe(1)
    expect(screen.getAllByTestId("bot-check")).toHaveLength(1)
  })

  it("never sends a token twice: a refused password waits for the widget's next one", async () => {
    mocks.login.mockRejectedValue({ status: 400, code: "User.InvalidCredentials" })
    renderPage()
    const user = userEvent.setup()
    const widget = await liveWidgetFor("login")
    await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
    await user.type(screen.getByLabelText("auth.password"), "pw")

    act(() => widget.options.callback("token-1"))
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))
    await waitFor(() => expect(mocks.login).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(widget.resets).toBe(1))

    // The second try goes out only with the token the reset made.
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))
    expect(mocks.login).toHaveBeenCalledTimes(1)
    act(() => widget.options.callback("token-2"))

    await waitFor(() => expect(mocks.login).toHaveBeenCalledTimes(2))
    expect(mocks.login.mock.calls.map((call) => call[3])).toEqual(["token-1", "token-2"])
    expect(widget.removed).toBe(false)
  })
})
