import type * as React from "react"
import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import type { SignInOptions } from "@authsystem/ui/branding"

/**
 * The sign-in page's "Pick an account" list (owner, 2026-11-22, reversing
 * D-215-4; Q1 (a), Q2 (a)). With accounts remembered on this browser, the first
 * step lists them in place of the address field; a row is that address and
 * "Continue": the passkey question while passkeys may run, else the password
 * step with the address chip, or the code. "Use a different account" shows the
 * field, and a link brings the list back. A row's "⋯" menu removes it locally.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  login: vi.fn(),
  loginWithPasskey: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  loadWebAuthn: vi.fn(),
  startAuthentication: vi.fn(),
  flowProps: [] as Array<Record<string, unknown>>,
  branding: {
    staySignedInOffered: true as boolean | null,
    signInOptions: undefined as SignInOptions | null | undefined,
  },
}))

vi.mock("@authsystem/auth/bot-defense/bot-check", () => ({
  useBotCheck: () => ({ field: null, prepare: async () => ({ proceed: true }), spent: () => {} }),
}))
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ name: "AuthSystem", logoUrl: null, isPending: false, ...mocks.branding }),
}))
vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => mocks.post(...args) },
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ login: mocks.login, loginWithPasskey: mocks.loginWithPasskey }),
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: null,
    reason: null,
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
vi.mock("../passkey/webauthn", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../passkey/webauthn")>()),
  loadWebAuthn: mocks.loadWebAuthn,
}))
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: { account?: string }) =>
      values?.account ? `${key} ${values.account}` : key,
    i18n: { language: "en" },
  }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({ title, children }: { title: string; children: React.ReactNode }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
}))
vi.mock("./email-code-flow", () => ({
  EmailCodeFlow: (props: Record<string, unknown>) => {
    mocks.flowProps.push(props)
    return <p>code flow</p>
  },
}))

import { REMEMBERED_ACCOUNTS_KEY } from "../remembered-accounts"
import { LoginPage } from "./login"

const OPTIONS_PATH = "/api/v1/auth/passkeys/assertion/options"
const LOOKUP_PATH = "/api/v1/auth/passkeys/lookup"
const CODE_START_PATH = "/api/v1/Auth/email-code/start"

const ADA = { id: "id-ada", email: "ada@example.com", name: "Ada Lovelace" }
const GRACE = { id: "id-grace", email: "grace@example.com", name: null }

const PASSWORD_ONLY: SignInOptions = {
  codeSignInOffered: false,
  passkeySignInOffered: false,
  primaryMethod: "password",
  selfRegistrationOpen: false,
  passwordAtSignUp: true,
  nameAtSignUp: true,
}
const CODE_FIRST: SignInOptions = { ...PASSWORD_ONLY, codeSignInOffered: true, primaryMethod: "code" }
const PASSKEYS_PASSWORD: SignInOptions = { ...PASSWORD_ONLY, passkeySignInOffered: true }

function response(status: number) {
  return { status, headers: new Headers() } as unknown as Response
}

/** Passkeys run, if the options say so; the lookup answers `passkey` for Ada only. */
function installServer() {
  mocks.post.mockImplementation(async (path: string, init?: { body?: { email?: string } }) => {
    if (path === OPTIONS_PATH) {
      return {
        data: {
          options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        },
        response: response(200),
      }
    }
    if (path === LOOKUP_PATH) {
      return { data: { passkey: init?.body?.email === ADA.email }, response: response(200) }
    }
    if (path === CODE_START_PATH) {
      return {
        data: { pendingId: "p-1", maskedEmail: "g***@example.com", expiresAt: "2099-01-01T00:00:00Z" },
        response: new Response(null, { status: 200 }),
      }
    }
    return { data: undefined, error: { status: 404 }, response: response(404) }
  })
}

/** The paths the page posted, without the page-load options. */
function posted(): string[] {
  return mocks.post.mock.calls.map((call) => call[0] as string).filter((path) => path !== OPTIONS_PATH)
}

function remember(...accounts: Array<{ id: string; email: string; name: string | null }>) {
  window.localStorage.setItem(REMEMBERED_ACCOUNTS_KEY, JSON.stringify(accounts))
}

/** The router state of the entry on screen. */
function RouterState() {
  return <pre data-testid="router-state">{JSON.stringify(useLocation().state ?? null)}</pre>
}

function renderPage(state?: { email?: string; from?: string }) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: "/login", state }]}>
      <Routes>
        <Route
          path="/login"
          element={
            <>
              <LoginPage />
              <RouterState />
            </>
          }
        />
      </Routes>
    </MemoryRouter>
  )
}

function list() {
  return screen.getByRole("list", { name: "auth.accounts.listLabel" })
}

/** The row button of an account, named by what it shows first. */
function row(text: string) {
  return within(list()).getByRole("button", {
    name: new RegExp(`^${text.replace(/[.]/g, "\\.")}`),
  })
}

beforeEach(() => {
  vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
  mocks.post.mockReset()
  mocks.login.mockReset().mockResolvedValue({ status: "authenticated", requiresPasswordChange: false })
  mocks.complete.mockReset()
  mocks.loadWebAuthn.mockReset().mockResolvedValue({
    startAuthentication: mocks.startAuthentication,
    browserSupportsWebAuthnAutofill: async () => false,
    WebAuthnAbortService: { cancelCeremony: vi.fn() },
  })
  mocks.startAuthentication.mockReset().mockRejectedValue(
    Object.assign(new Error("not allowed"), { name: "NotAllowedError" })
  )
  mocks.flowProps.length = 0
  mocks.branding.signInOptions = PASSWORD_ONLY
  window.localStorage.clear()
  window.sessionStorage.clear()
  installServer()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("the first step with remembered accounts", () => {
  it("lists them, newest first, in place of the address field and its button", async () => {
    remember(ADA, GRACE)
    renderPage()

    expect(screen.getByRole("heading", { name: "auth.accounts.pickTitle" })).toBeInTheDocument()
    const rows = within(list()).getAllByRole("listitem")
    expect(rows.map((item) => item.textContent)).toEqual([
      expect.stringContaining("Ada Lovelaceada@example.com"),
      expect.stringContaining("grace@example.com"),
    ])
    // An account without a name shows its address once.
    expect(rows[1].textContent?.match(/grace@example\.com/g)).toHaveLength(1)
    expect(screen.queryByLabelText("auth.email")).toBeNull()
    // Password mode shows the address and the password on one step: the
    // password waits here for the account pressed.
    expect(screen.queryByLabelText("auth.password")).toBeNull()
    expect(screen.queryByRole("button", { name: "auth.signIn" })).toBeNull()
    // The first row takes the focus, as the address field did.
    await waitFor(() => expect(row("Ada Lovelace")).toHaveFocus())
  })

  it("is the address field as before when none is remembered", () => {
    renderPage()

    expect(screen.queryByRole("list", { name: "auth.accounts.listLabel" })).toBeNull()
    expect(screen.getByLabelText("auth.email")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "common.back" })).toBeNull()
  })

  it("is the address field when another screen handed the address over", () => {
    remember(ADA)
    renderPage({ email: "someone@example.com" })

    expect(screen.getByLabelText("auth.email")).toHaveValue("someone@example.com")
    expect(screen.queryByRole("list", { name: "auth.accounts.listLabel" })).toBeNull()
  })

  it("opens the empty address field as a step of the history on 'Use a different account', which Back leaves for the list", async () => {
    const user = userEvent.setup()
    remember(ADA)
    renderPage({ from: "/profile" })

    await user.click(screen.getByRole("button", { name: /auth\.useDifferentAccount/ }))

    const field = screen.getByLabelText("auth.email")
    expect(field).toHaveValue("")
    expect(field).toHaveFocus()
    expect(screen.getByRole("heading", { name: "auth.signInTitle" })).toBeInTheDocument()
    // The page's own buttons, as on the address field without a list, plus Back.
    expect(screen.getByRole("button", { name: "auth.signIn" })).toBeInTheDocument()
    // The router state the page arrived with rides along (where to land after).
    expect(JSON.parse(screen.getByTestId("router-state").textContent ?? "null")).toEqual({
      from: "/profile",
      addressField: true,
    })

    // Back goes one step back in the history: to the list, not off the page.
    await user.click(screen.getByRole("button", { name: "common.back" }))

    expect(row("Ada Lovelace")).toBeInTheDocument()
    expect(JSON.parse(screen.getByTestId("router-state").textContent ?? "null")).toEqual({
      from: "/profile",
    })
    expect(posted()).toEqual([])
  })
})

describe("a row pressed is that address and 'Continue'", () => {
  it("asks whether it has a passkey while passkeys may run, and opens the passkey screen on yes", async () => {
    mocks.branding.signInOptions = PASSKEYS_PASSWORD
    remember(ADA)
    renderPage()
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith(OPTIONS_PATH))

    await userEvent.setup().click(row("Ada Lovelace"))

    await screen.findByRole("heading", { name: "auth.passkey.screenTitle" })
    expect(mocks.post).toHaveBeenCalledWith(LOOKUP_PATH, expect.objectContaining({ body: { email: ADA.email } }))
    expect(screen.getByTestId("address-chip")).toHaveTextContent(ADA.email)
  })

  it("goes to the password step with the address chip on no, and signs in that address", async () => {
    const user = userEvent.setup()
    mocks.branding.signInOptions = PASSKEYS_PASSWORD
    remember(GRACE)
    renderPage()
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith(OPTIONS_PATH))

    await user.click(row("grace@example.com"))

    const password = await screen.findByLabelText("auth.password")
    expect(posted()).toEqual([LOOKUP_PATH])
    expect(screen.getByTestId("address-chip")).toHaveTextContent(GRACE.email)
    // The password manager's pair: the hidden username beside the chip (PR 171).
    expect(screen.getByTestId("address-chip-username")).toHaveValue(GRACE.email)

    await user.type(password, "pw")
    await user.click(screen.getByRole("button", { name: "auth.signIn" }))

    await waitFor(() =>
      expect(mocks.login).toHaveBeenCalledWith(GRACE.email, "pw", false, undefined)
    )
  })

  it("goes straight to the password step with the chip where passkeys do not run", async () => {
    remember(GRACE)
    renderPage()

    await userEvent.setup().click(row("grace@example.com"))

    expect(await screen.findByLabelText("auth.password")).toHaveFocus()
    expect(screen.getByTestId("address-chip")).toHaveTextContent(GRACE.email)
    expect(posted()).toEqual([])
  })

  it("sends the code to that address in code mode", async () => {
    mocks.branding.signInOptions = CODE_FIRST
    remember(GRACE)
    renderPage()

    await userEvent.setup().click(row("grace@example.com"))

    await screen.findByText("code flow")
    expect(mocks.post).toHaveBeenCalledWith(
      CODE_START_PATH,
      expect.objectContaining({ body: expect.objectContaining({ email: GRACE.email }) })
    )
  })

  it("asks once, and sends one code, for a second press while the first is on its way", async () => {
    const user = userEvent.setup()
    mocks.branding.signInOptions = { ...CODE_FIRST, passkeySignInOffered: true }
    remember(GRACE)
    renderPage()
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith(OPTIONS_PATH))
    // The question's answer is held until both presses are in.
    let answer: (value: unknown) => void = () => {}
    const answered = new Promise((resolve) => (answer = resolve))
    const server = mocks.post.getMockImplementation()!
    mocks.post.mockImplementation(async (path: string, init?: unknown) => {
      if (path === LOOKUP_PATH) {
        await answered
        return { data: { passkey: false }, response: response(200) }
      }
      return server(path, init)
    })

    await user.click(row("grace@example.com"))
    await user.click(row("grace@example.com"))
    answer(undefined)

    await screen.findByText("code flow")
    expect(posted()).toEqual([LOOKUP_PATH, CODE_START_PATH])
  })

  it("comes back to the list from the address chip", async () => {
    const user = userEvent.setup()
    remember(ADA, GRACE)
    renderPage()

    await user.click(row("grace@example.com"))
    await user.click(await screen.findByTestId("address-chip"))

    expect(row("Ada Lovelace")).toBeInTheDocument()
    expect(screen.queryByLabelText("auth.password")).toBeNull()
  })
})

describe("removing an account from the list", () => {
  async function remove(email: string) {
    const user = userEvent.setup()
    await user.click(screen.getByRole("button", { name: `auth.accounts.moreOptions ${email}` }))
    await user.click(await screen.findByRole("menuitem", { name: "auth.accounts.remove" }))
  }

  it("forgets it on this browser, sends nothing, and leaves the others", async () => {
    remember(ADA, GRACE)
    renderPage()

    await remove(ADA.email)

    await waitFor(() => expect(within(list()).getAllByRole("listitem")).toHaveLength(1))
    expect(row("grace@example.com")).toHaveFocus()
    expect(JSON.parse(window.localStorage.getItem(REMEMBERED_ACCOUNTS_KEY) ?? "[]")).toEqual([GRACE])
    expect(posted()).toEqual([])
  })

  it("shows the address field once the last one is gone", async () => {
    remember(GRACE)
    renderPage()

    await remove(GRACE.email)

    expect(await screen.findByLabelText("auth.email")).toHaveFocus()
    expect(window.localStorage.getItem(REMEMBERED_ACCOUNTS_KEY)).toBeNull()
    // Nothing to go back to: no Back, and no step added to the history.
    expect(screen.queryByRole("button", { name: "common.back" })).toBeNull()
    expect(screen.getByTestId("router-state")).toHaveTextContent("null")
  })
})
