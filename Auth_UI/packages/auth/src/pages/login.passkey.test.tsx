import type * as React from "react"
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { SignInOptions } from "@authsystem/ui/branding"

import type { SignInReason } from "../sign-in-reason"

/**
 * S20 PR C on the sign-in page (PK15, D-S20-9 (a), IN 3 and IN 5): the passkey
 * button while passkeys run here, nothing while they do not; a passkey sign-in
 * that ends through the completion rule every sign-in uses; and the doors that
 * answer `Auth.PasskeyRequired`, which keep the API's sentence on the page and
 * put the passkey first.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  login: vi.fn(),
  loginWithPasskey: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  loadWebAuthn: vi.fn(),
  startAuthentication: vi.fn(),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
  flowProps: [] as Array<Record<string, unknown>>,
  completion: { returnTo: null as string | null, reason: null as SignInReason | null },
  branding: {
    staySignedInOffered: true as boolean | null,
    signInOptions: undefined as SignInOptions | null | undefined,
  },
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
    ...mocks.completion,
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
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "en" } }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))
vi.mock("sonner", () => ({
  toast: { error: mocks.toastError, success: mocks.toastSuccess },
}))
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

import { usePasskeyRequiredReporter } from "../passkey/passkey-required"
import {
  FULL_PROOF_KEY,
  isFullProofRequested,
  LAST_SIGN_IN_METHOD_KEY,
  requestPasskeySignIn,
} from "../sign-in-method"
import { LoginPage } from "./login"

const OPTIONS_PATH = "/api/v1/auth/passkeys/assertion/options"
const PASSKEY = "auth.passkey.signIn"
const CREDENTIAL = { id: "cred-1", rawId: "cred-1", type: "public-key" }
const SIGNED_IN = { status: "authenticated", requiresPasswordChange: false }
const PASSKEY_REQUIRED = {
  status: 403,
  code: "Auth.PasskeyRequired",
  detail: "This account signs in with its passkey.",
}

const CODE_OPTIONS: SignInOptions = {
  codeSignInOffered: true,
  primaryMethod: "password",
  selfRegistrationOpen: false,
} as SignInOptions

const HELPER = {
  startAuthentication: mocks.startAuthentication,
  browserSupportsWebAuthnAutofill: async () => false,
  WebAuthnAbortService: { cancelCeremony: vi.fn() },
}

function response(status: number) {
  return { status, headers: new Headers() } as unknown as Response
}

function passkeysRun() {
  mocks.post.mockImplementation(async (path: string) =>
    path === OPTIONS_PATH
      ? {
          data: {
            options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
            expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
          },
          error: undefined,
          response: response(200),
        }
      : { data: undefined, error: { status: 404 }, response: response(404) }
  )
}

function passkeysOff() {
  mocks.post.mockResolvedValue({ data: undefined, error: { status: 404 }, response: response(404) })
}

function Recovery() {
  const location = useLocation()
  const state = location.state as { message?: string } | null
  return <p data-testid="recovery">{state?.message}</p>
}

function renderPage({
  providers,
  recoveryPath,
}: { providers?: React.ReactNode; recoveryPath?: string } = {}) {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <Routes>
        <Route path="/login" element={<LoginPage providers={providers} recoveryPath={recoveryPath} />} />
        <Route path="/account-recovery" element={<Recovery />} />
      </Routes>
    </MemoryRouter>
  )
}

async function passkeyButton() {
  return within(await screen.findByTestId("passkey-sign-in")).getByRole("button", {
    name: PASSKEY,
  })
}

function precedes(first: Element, second: Element) {
  return Boolean(first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_FOLLOWING)
}

async function signInWithPassword() {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText("auth.email"), "admin@example.com")
  await user.type(screen.getByLabelText("auth.password"), "pw")
  await user.click(screen.getByRole("button", { name: "auth.signIn" }))
}

beforeEach(() => {
  vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
  mocks.post.mockReset()
  mocks.login.mockReset()
  mocks.loginWithPasskey.mockReset()
  mocks.complete.mockReset()
  mocks.challenge.mockReset()
  mocks.toastError.mockReset()
  mocks.toastSuccess.mockReset()
  mocks.loadWebAuthn.mockReset().mockResolvedValue(HELPER)
  mocks.startAuthentication.mockReset().mockResolvedValue(CREDENTIAL)
  mocks.flowProps.length = 0
  mocks.completion.returnTo = null
  mocks.completion.reason = null
  mocks.branding.staySignedInOffered = true
  mocks.branding.signInOptions = undefined
  window.localStorage.clear()
  window.sessionStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("the sign-in page while passkeys do not run", () => {
  it("is as before: no passkey control, and the address asks for a username only", async () => {
    passkeysOff()
    renderPage()

    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith(OPTIONS_PATH))
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    expect(screen.getByLabelText("auth.email")).toHaveAttribute("autocomplete", "username")
    expect(screen.getByRole("button", { name: "auth.signIn" })).toHaveAttribute(
      "data-variant",
      "default"
    )
    expect(mocks.loadWebAuthn).not.toHaveBeenCalled()
  })
})

describe("the sign-in page while passkeys run", () => {
  it("offers the passkey under the sign-in button, to every visitor (D-S20-9 (a))", async () => {
    passkeysRun()
    renderPage()

    const passkey = await passkeyButton()
    const submit = screen.getByRole("button", { name: "auth.signIn" })
    expect(precedes(submit, passkey)).toBe(true)
    expect(passkey).toHaveAttribute("data-variant", "secondary")
    expect(submit).toHaveAttribute("data-variant", "default")
  })

  it("signs in through the completion rule, with no proof and no method remembered (T-C6, TS10)", async () => {
    passkeysRun()
    mocks.loginWithPasskey.mockResolvedValue(SIGNED_IN)
    renderPage()

    const passkey = await passkeyButton()
    fireEvent.click(passkey)
    // Inside the click (T-C2).
    expect(mocks.startAuthentication).toHaveBeenCalledTimes(1)

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1))
    expect(mocks.loginWithPasskey).toHaveBeenCalledWith(CREDENTIAL, false)
    // The completion every sign-in uses, without a proof word: the passkey is no
    // password, and must not be remembered as one.
    expect(mocks.complete.mock.calls[0]).toEqual([SIGNED_IN])
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBeNull()
    expect(mocks.toastSuccess).toHaveBeenCalledWith("auth.welcomeBack")
  })

  it("carries the stay-signed-in box as it stands at the click", async () => {
    passkeysRun()
    mocks.loginWithPasskey.mockResolvedValue(SIGNED_IN)
    renderPage()

    const passkey = await passkeyButton()
    await userEvent.setup().click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    fireEvent.click(passkey)

    await waitFor(() => expect(mocks.loginWithPasskey).toHaveBeenCalledWith(CREDENTIAL, true))
  })

  it("sends an account waiting for deletion to the recovery page, as the password does", async () => {
    passkeysRun()
    mocks.loginWithPasskey.mockRejectedValue({
      status: 403,
      code: "User.AccountPendingDeletion",
      detail: "Your account is scheduled for deletion.",
    })
    renderPage({ recoveryPath: "/account-recovery" })

    fireEvent.click(await passkeyButton())

    expect(await screen.findByTestId("recovery")).toHaveTextContent(
      "Your account is scheduled for deletion."
    )
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("says the API's sentence for a passkey it refused, with the way out for a removed one", async () => {
    passkeysRun()
    mocks.loginWithPasskey.mockRejectedValue({
      status: 400,
      code: "Passkey.VerificationFailed",
      detail: "The passkey could not be verified.",
    })
    renderPage()

    fireEvent.click(await passkeyButton())

    const message = await screen.findByTestId("passkey-message")
    expect(message).toHaveTextContent("The passkey could not be verified.")
    expect(message).toHaveTextContent(i18n.t("auth.passkey.removedPasskeyWayOut"))
    expect(mocks.complete).not.toHaveBeenCalled()
  })
})

describe("the doors that ask for the passkey (IN 5, D-S20-4 (ii-s))", () => {
  it("the password: keeps the API's sentence on the page, and the passkey comes first, focused", async () => {
    passkeysRun()
    mocks.login.mockRejectedValue(PASSKEY_REQUIRED)
    renderPage()
    await passkeyButton()

    await signInWithPassword()

    expect(await screen.findByTestId("passkey-required-note")).toHaveTextContent(
      PASSKEY_REQUIRED.detail
    )
    expect(mocks.toastError).not.toHaveBeenCalled()
    const passkey = await passkeyButton()
    expect(precedes(passkey, screen.getByLabelText("auth.email"))).toBe(true)
    expect(passkey).toHaveAttribute("data-variant", "default")
    await waitFor(() => expect(passkey).toHaveFocus())
  })

  it("the password, after the first options failed: the passkey is asked for at once, and comes", async () => {
    let optionsCalls = 0
    mocks.post.mockImplementation(async (path: string) => {
      if (path !== OPTIONS_PATH) return { data: undefined, error: { status: 404 }, response: response(404) }
      optionsCalls += 1
      return optionsCalls === 1
        ? { data: undefined, error: { status: 503 }, response: response(503) }
        : {
            data: {
              options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
              expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
            },
            error: undefined,
            response: response(200),
          }
    })
    mocks.login.mockRejectedValue(PASSKEY_REQUIRED)
    renderPage()
    await waitFor(() => expect(optionsCalls).toBe(1))
    // Nothing said yet that passkeys run here: the page is as it was.
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()

    await signInWithPassword()

    expect(await screen.findByTestId("passkey-required-note")).toHaveTextContent(
      PASSKEY_REQUIRED.detail
    )
    const passkey = await passkeyButton()
    await waitFor(() => expect(passkey).toBeEnabled())
    expect(optionsCalls).toBe(2)
    // It takes the focus once it can be pressed.
    await waitFor(() => expect(passkey).toHaveFocus())
  })

  it("the password, while the first options are still on their way: the button takes the focus when they arrive", async () => {
    let answer!: (value: unknown) => void
    mocks.post.mockImplementation((path: string) =>
      path === OPTIONS_PATH
        ? new Promise((resolve) => {
            answer = resolve
          })
        : Promise.resolve({ data: undefined, error: { status: 404 }, response: response(404) })
    )
    mocks.login.mockRejectedValue(PASSKEY_REQUIRED)
    renderPage()
    await waitFor(() => expect(mocks.post).toHaveBeenCalledWith(OPTIONS_PATH))

    await signInWithPassword()
    expect(await screen.findByTestId("passkey-required-note")).toBeInTheDocument()
    // No button yet: nothing has said passkeys run, and their options are on their way.
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()

    await act(async () =>
      answer({
        data: {
          options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
          expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
        },
        error: undefined,
        response: response(200),
      })
    )
    const passkey = await passkeyButton()
    await waitFor(() => expect(passkey).toHaveFocus())
  })

  it("the password, while passkeys do not run: the sentence is still kept on the page", async () => {
    passkeysOff()
    mocks.login.mockRejectedValue(PASSKEY_REQUIRED)
    renderPage()

    await signInWithPassword()

    expect(await screen.findByTestId("passkey-required-note")).toHaveTextContent(
      PASSKEY_REQUIRED.detail
    )
    expect(mocks.toastError).not.toHaveBeenCalled()
  })

  it("a provider under the form hands the refusal to the page", async () => {
    passkeysRun()
    let handed: boolean | null = null
    function Provider() {
      const report = usePasskeyRequiredReporter()
      return (
        <button type="button" onClick={() => (handed = report(PASSKEY_REQUIRED))}>
          provider
        </button>
      )
    }
    renderPage({ providers: <Provider /> })
    await passkeyButton()

    fireEvent.click(screen.getByRole("button", { name: "provider" }))

    expect(handed).toBe(true)
    expect(await screen.findByTestId("passkey-required-note")).toHaveTextContent(
      PASSKEY_REQUIRED.detail
    )
  })

  it("the emailed code: the page goes back to the address with the sentence", async () => {
    passkeysRun()
    mocks.branding.signInOptions = { ...CODE_OPTIONS, primaryMethod: "code" } as SignInOptions
    mocks.post.mockImplementation(async (path: string) =>
      path === OPTIONS_PATH
        ? {
            data: {
              options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
              expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
            },
            error: undefined,
            response: response(200),
          }
        : {
            data: {
              pendingId: "p-1",
              maskedEmail: "a***@example.com",
              expiresAt: "2099-09-10T10:05:00.000Z",
            },
            response: new Response(null, { status: 200 }),
          }
    )
    renderPage()
    await passkeyButton()

    const user = userEvent.setup()
    await user.type(screen.getByLabelText("auth.email"), "admin@example.com")
    await user.click(screen.getByRole("button", { name: "auth.continueWithEmail" }))
    expect(await screen.findByText("code flow")).toBeInTheDocument()

    const flow = mocks.flowProps.at(-1)!
    act(() => (flow.onPasskeyRequired as (notice: string) => void)(PASSKEY_REQUIRED.detail))

    expect(await screen.findByTestId("passkey-required-note")).toHaveTextContent(
      PASSKEY_REQUIRED.detail
    )
    expect(screen.getByLabelText("auth.email")).toHaveValue("admin@example.com")
  })
})

describe("the sign-in again of a passkey account (T-C7)", () => {
  it("puts the passkey first, and a code sign-in from here still proves the password", async () => {
    passkeysRun()
    requestPasskeySignIn()
    renderPage()

    const passkey = await passkeyButton()
    expect(precedes(passkey, screen.getByLabelText("auth.email"))).toBe(true)
    expect(passkey).toHaveAttribute("data-variant", "default")
    expect(screen.getByRole("button", { name: "auth.signIn" })).toHaveAttribute(
      "data-variant",
      "secondary"
    )
    expect(isFullProofRequested()).toBe(true)
  })

  it("is answered by the passkey sign-in, so the next visit opens as usual", async () => {
    passkeysRun()
    mocks.loginWithPasskey.mockResolvedValue(SIGNED_IN)
    requestPasskeySignIn()
    renderPage()

    fireEvent.click(await passkeyButton())

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledWith(SIGNED_IN))
    expect(window.sessionStorage.getItem(FULL_PROOF_KEY)).toBeNull()
  })

  it("an application's requirement only the passkey meets (mfa_passkey) puts it first too", async () => {
    passkeysRun()
    mocks.completion.reason = { reason: "mfa_passkey", method: null }
    renderPage()

    const passkey = await passkeyButton()
    expect(precedes(passkey, screen.getByLabelText("auth.email"))).toBe(true)
    expect(screen.getByTestId("sign-in-reason")).toHaveTextContent("auth.signInReasonPasskeyTitle")
  })
})
