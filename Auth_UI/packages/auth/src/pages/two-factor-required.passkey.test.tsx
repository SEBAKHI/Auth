import type * as React from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import type { MfaRequirement } from "@authsystem/api/mfa-requirement"

/**
 * OI-280 PR B (T-B8, V-B10): the set-up page offers a passkey beside the app, in
 * both modes, only while passkeys run here (the deployment's hint, and no 404 met),
 * the browser has WebAuthn and the addition cannot end in N9 (email on). A
 * registration raises no session (D-B5), so the new passkey is used once in place,
 * through the window's own proof, and only then does the page leave as after the
 * app: the request resumes once, or the platform page refreshes and completes.
 * Otherwise the page is exactly as before; its older cases are
 * two-factor-required.test.tsx's, unchanged.
 *
 * Deliberate breaks, each verified red: the in-place use skipped (the page left
 * at the addition); the choice shown with email off. REVIEW 160, each verified red:
 * the sentence shown to everyone (F2); the message back inside the choice's block
 * (F12); the restore hook removed from the application mode.
 */
const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  get: vi.fn(),
  refreshSessionNow: vi.fn(),
  complete: vi.fn(),
  calls: [] as string[],
  loadWebAuthn: vi.fn(),
  startRegistration: vi.fn(),
  startAuthentication: vi.fn(),
  signInOptions: { passkeySignInOffered: true } as { passkeySignInOffered?: boolean } | null,
  auth: {
    loginWithPasskey: vi.fn(),
    user: { id: "u-1", email: "admin@example.com" },
    permissions: [] as string[],
    mfaRequirement: "enroll" as MfaRequirement,
    rereadUser: vi.fn(),
    logout: vi.fn(),
  },
}))

vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/api/client", () => ({
  api: { POST: mocks.post, GET: mocks.get },
  refreshSessionNow: mocks.refreshSessionNow,
}))
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    subtitle,
    children,
    footer,
  }: {
    title: string
    subtitle?: string
    children: React.ReactNode
    footer?: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      {subtitle ? <p>{subtitle}</p> : null}
      {children}
      {footer}
    </div>
  ),
}))
vi.mock("@authsystem/ui/branding", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/ui/branding")>()),
  useSignInOptions: () => mocks.signInOptions,
}))
// A new object each render, as the real context gives one: the platform test changes the
// requirement under the page.
vi.mock("../auth-context", () => ({ useAuth: () => ({ ...mocks.auth }) }))
vi.mock("../recovery-code-notice", () => ({ markRecoveryCodeSignIn: vi.fn() }))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({ complete: mocks.complete }),
}))
vi.mock("../flow-branding", () => ({
  useFlowBranding: () => ({ appBranding: null, layout: {} }),
}))
vi.mock("../passkey/webauthn", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../passkey/webauthn")>()),
  loadWebAuthn: mocks.loadWebAuthn,
}))

import { TwoFactorRequiredPage } from "./two-factor-required"

const HANDLE = "0123456789abcdef".repeat(4)
const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`
const APPLICATION = { mode: "application", returnTo: RESUME, from: "/" }
const ME = { id: "u-1", qualifyingMfa: false, emailCodeAvailable: true }
const REGISTRATION = {
  options: { challenge: "r1", rp: { id: "localhost", name: "Example" }, user: { id: "aGFuZGxl" } },
  expiresAt: "2099-01-01T00:00:00Z",
  emailCodeRequired: false,
}
const ASSERTION = {
  options: { challenge: "a1", rpId: "localhost", allowCredentials: [] },
  expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
}
const CREATED = { id: "cred-new", rawId: "cred-new", type: "public-key" }
const SIGNED = { id: "cred-new", rawId: "cred-new", type: "public-key", response: {} }

function response(status: number) {
  return { status, ok: status >= 200 && status < 300, headers: new Headers() } as unknown as Response
}

function server() {
  mocks.post.mockImplementation(async (path: string) => {
    mocks.calls.push(path)
    switch (path) {
      case "/api/v1/auth/passkeys/registration/options":
        return { data: REGISTRATION, error: undefined, response: response(200) }
      case "/api/v1/auth/passkeys/registration/verify":
        return { data: { id: "p-1", name: "Laptop" }, error: undefined, response: response(200) }
      case "/api/v1/auth/passkeys/assertion/options":
        return { data: ASSERTION, error: undefined, response: response(200) }
      case "/api/v1/auth/reauthentication/passkey":
        return { data: undefined, error: undefined, response: response(204) }
      default:
        return { data: undefined, error: { status: 404 }, response: response(404) }
    }
  })
}

function renderPage(state?: unknown) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[{ pathname: "/two-factor/required", state }]}>
        <Routes>
          <Route path="/two-factor/required" element={<TwoFactorRequiredPage />} />
          <Route path="/login" element={<p>sign-in page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

/** Adds the passkey through the shared addition: Add, then the dialog's confirm. */
async function addThePasskey() {
  await userEvent.setup().click(await screen.findByRole("button", { name: "auth.mfaRequiredAddPasskey" }))
  const dialog = await screen.findByRole("dialog", { name: "profile.passkeys.addTitle" })
  fireEvent.click(within(dialog).getByRole("button", { name: "profile.passkeys.create" }))
  await screen.findByTestId("use-passkey-now")
}

/** The «use it now» button, once its options are in hand. */
async function useItNow() {
  const button = within(await screen.findByTestId("passkey-sign-in")).getByRole("button", {
    name: "auth.mfaRequiredUsePasskeyAction",
  })
  await waitFor(() => expect(button).toBeEnabled())
  return button
}

beforeEach(() => {
  vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
  mocks.calls.length = 0
  mocks.post.mockReset()
  mocks.get.mockReset().mockResolvedValue({ data: ME, error: undefined })
  mocks.complete.mockReset()
  mocks.auth.logout.mockReset()
  mocks.auth.permissions = []
  mocks.auth.mfaRequirement = "enroll"
  mocks.signInOptions = { passkeySignInOffered: true }
  mocks.startRegistration.mockReset().mockResolvedValue(CREATED)
  mocks.startAuthentication.mockReset().mockResolvedValue(SIGNED)
  mocks.loadWebAuthn.mockReset().mockResolvedValue({
    startRegistration: mocks.startRegistration,
    startAuthentication: mocks.startAuthentication,
    browserSupportsWebAuthnAutofill: async () => false,
    WebAuthnAbortService: { cancelCeremony: vi.fn() },
  })
  mocks.refreshSessionNow.mockReset().mockImplementation(async () => {
    mocks.calls.push("refresh")
    return true
  })
  mocks.auth.rereadUser.mockReset().mockImplementation(async () => {
    mocks.calls.push("me")
    return true
  })
  window.sessionStorage.clear()
  server()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("the set-up page's passkey beside the app (T-B8), application mode", () => {
  it("offers a passkey beside the app while passkeys run here, and its words name both", async () => {
    renderPage(APPLICATION)

    expect(await screen.findByText("auth.mfaRequiredEnrollWithPasskey")).toBeInTheDocument()
    expect(screen.getByText("auth.mfaAppRequiredSubtitleWithPasskey")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "auth.mfaRequiredAddPasskey" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "profile.enableTwoFactor" })).toBeInTheDocument()
    expect(screen.queryByText("auth.mfaRequiredEnroll")).not.toBeInTheDocument()
  })

  it.each([
    ["the deployment says passkeys do not run", () => {
      mocks.signInOptions = { passkeySignInOffered: false }
    }],
    ["there is no word from the deployment", () => {
      mocks.signInOptions = null
    }],
    ["email sending is off (N9)", () => {
      mocks.get.mockResolvedValue({ data: { ...ME, emailCodeAvailable: false }, error: undefined })
    }],
    ["the browser has no WebAuthn", () => {
      vi.stubGlobal("PublicKeyCredential", undefined)
    }],
  ])("is the app's page as before when %s", async (_label, given) => {
    given()
    renderPage(APPLICATION)

    expect(await screen.findByText("auth.mfaRequiredEnroll")).toBeInTheDocument()
    expect(screen.getByText("auth.mfaAppRequiredSubtitle")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.mfaRequiredAddPasskey" })).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "profile.enableTwoFactor" })).toBeInTheDocument()
  })

  it("adds the passkey, has it used once in place, then resumes the request once", async () => {
    renderPage(APPLICATION)

    await addThePasskey()

    // Added, not yet used: nothing resumes, and the step asks for one touch.
    expect(screen.getByText("auth.mfaRequiredUsePasskey")).toBeInTheDocument()
    expect(mocks.complete).not.toHaveBeenCalled()
    fireEvent.click(await useItNow())

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1))
    expect(mocks.complete).toHaveBeenCalledWith({}, { endsSignIn: false })
    expect(mocks.post).toHaveBeenCalledWith("/api/v1/auth/reauthentication/passkey", {
      body: { credential: SIGNED },
    })
    expect(mocks.calls.indexOf("/api/v1/auth/reauthentication/passkey")).toBeGreaterThan(
      mocks.calls.indexOf("/api/v1/auth/passkeys/registration/verify")
    )
  })

  it("a cancelled second touch keeps the passkey and the step, with its button and the page's Cancel", async () => {
    mocks.startAuthentication.mockRejectedValueOnce(
      Object.assign(new Error("cancelled"), { name: "NotAllowedError" })
    )
    renderPage(APPLICATION)
    await addThePasskey()

    fireEvent.click(await useItNow())

    await waitFor(() => expect(mocks.startAuthentication).toHaveBeenCalledTimes(1))
    expect(await useItNow()).toBeEnabled()
    expect(screen.getByTestId("use-passkey-now")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "common.cancel" })).toBeInTheDocument()
    expect(mocks.complete).not.toHaveBeenCalled()
    expect(mocks.calls).not.toContain("/api/v1/auth/reauthentication/passkey")
  })
})

describe("what a first passkey costs an administrator with no app (REVIEW 160 F2, D-160-2 (b))", () => {
  const addButton = () => screen.findByRole("button", { name: "auth.mfaRequiredAddPasskey" })

  it("says so above «Add a passkey» on the platform page", async () => {
    mocks.get.mockResolvedValue({ data: { ...ME, totpEnabled: false }, error: undefined })
    renderPage()

    const add = await addButton()
    const notice = screen.getByTestId("passkey-only-notice")
    expect(notice).toHaveTextContent("auth.passkeyOffer.onlyWayIn")
    expect(notice.compareDocumentPosition(add) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it("says so in the application mode to an account with platform permissions", async () => {
    mocks.auth.mfaRequirement = "none"
    mocks.auth.permissions = ["users:read"]
    mocks.get.mockResolvedValue({ data: { ...ME, totpEnabled: false }, error: undefined })
    renderPage(APPLICATION)

    await addButton()
    expect(screen.getByTestId("passkey-only-notice")).toBeInTheDocument()
  })

  it("says nothing in the application mode to a person without platform permissions", async () => {
    mocks.auth.mfaRequirement = "none"
    mocks.get.mockResolvedValue({ data: { ...ME, totpEnabled: false }, error: undefined })
    renderPage(APPLICATION)

    await addButton()
    expect(screen.queryByTestId("passkey-only-notice")).not.toBeInTheDocument()
  })

  it("says nothing to an administrator with the app", async () => {
    mocks.get.mockResolvedValue({ data: { ...ME, totpEnabled: true }, error: undefined })
    renderPage()

    await addButton()
    expect(screen.queryByTestId("passkey-only-notice")).not.toBeInTheDocument()
  })
})

describe("REVIEW 160: the set-up page's message, and its life", () => {
  it("keeps saying why when the account, read again, drops the passkey choice (F12)", async () => {
    let emailOff = false
    mocks.get.mockImplementation(async () => ({
      data: emailOff ? { ...ME, emailCodeAvailable: false } : ME,
      error: undefined,
    }))
    mocks.post.mockImplementation(async () => {
      emailOff = true
      return {
        data: undefined,
        error: { status: 409, code: "Passkey.EmailRequired" },
        response: response(409),
      }
    })
    renderPage(APPLICATION)

    await userEvent.setup().click(await screen.findByRole("button", { name: "auth.mfaRequiredAddPasskey" }))

    await waitFor(() =>
      expect(screen.queryByRole("button", { name: "auth.mfaRequiredAddPasskey" })).not.toBeInTheDocument()
    )
    expect(screen.getByTestId("set-up-passkey-message")).toHaveTextContent("profile.passkeys.needsAppFirst")
  })

  it("asks again when the Back button brings the application mode back: its one resume was spent", async () => {
    renderPage(APPLICATION)
    await screen.findByRole("button", { name: "auth.mfaRequiredAddPasskey" })
    const reload = vi.fn()
    vi.stubGlobal("location", {
      href: "https://accounts.example.com/two-factor/required",
      origin: "https://accounts.example.com",
      assign: vi.fn(),
      replace: vi.fn(),
      reload,
    })

    window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true }))

    expect(reload).toHaveBeenCalledTimes(1)
  })
})

describe("the set-up page's passkey beside the app (T-B8), platform mode", () => {
  it("adds the passkey, has it used once in place, then refreshes the session and reads the account again", async () => {
    // As after the app: the page's own check, whose refreshed token carries the authority the
    // proof raised; the page completes once the account says nothing is left (the S08 cases).
    renderPage()

    expect(await screen.findByText("auth.mfaRequiredEnrollWithPasskey")).toBeInTheDocument()
    await addThePasskey()
    expect(mocks.calls).toEqual([
      "refresh",
      "me",
      "/api/v1/auth/passkeys/registration/options",
      "/api/v1/auth/passkeys/registration/verify",
      "/api/v1/auth/passkeys/assertion/options",
    ])
    fireEvent.click(await useItNow())

    await waitFor(() =>
      expect(mocks.calls.slice(5)).toEqual(["/api/v1/auth/reauthentication/passkey", "refresh", "me"])
    )
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("is the app's page as before while passkeys do not run", async () => {
    mocks.signInOptions = { passkeySignInOffered: false }
    renderPage()

    expect(await screen.findByText("auth.mfaRequiredEnroll")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "auth.mfaRequiredAddPasskey" })).not.toBeInTheDocument()
  })
})
