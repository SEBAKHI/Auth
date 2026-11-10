import type * as React from "react"
import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"
import type { SignInOptions } from "@authsystem/ui/branding"

import type { SignInReason } from "../sign-in-reason"

/**
 * S20 PR C on the sign-in page (PK15, IN 3 and IN 5), as OI-280 PR A changed it
 * (D-S20-10, D-S20-11, D-S20-15, D-S20-16): while passkeys may run here there is
 * no passkey button for every visitor; after the address and "Continue" the page
 * asks the server whether it has a passkey, and only the answer `passkey` leads
 * to the passkey screen, with "Try another way". Every other answer is the next
 * step as before. While passkeys do not run, the page is as it was.
 *
 * Deliberate breaks, each verified red: the code sent before the lookup answers;
 * an `await` before `startAuthentication` in the passkey screen's click; one item
 * of the list made conditional on the account; the hint defaulted to true.
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
const LOOKUP_PATH = "/api/v1/auth/passkeys/lookup"
const CODE_START_PATH = "/api/v1/Auth/email-code/start"
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

/** Passkeys may run here (OI-280): the branding's hint, password first, no code. */
const PASSKEYS_PASSWORD_ONLY: SignInOptions = {
  codeSignInOffered: false,
  passkeySignInOffered: true,
  primaryMethod: "password",
  selfRegistrationOpen: false,
  passwordAtSignUp: true,
  nameAtSignUp: true,
}

const PASSKEYS_CODE_FIRST: SignInOptions = {
  ...PASSKEYS_PASSWORD_ONLY,
  codeSignInOffered: true,
  primaryMethod: "code",
}

const PASSKEYS_CODE_OFFERED_PASSWORD_FIRST: SignInOptions = {
  ...PASSKEYS_CODE_FIRST,
  primaryMethod: "password",
}

const HELPER = {
  startAuthentication: mocks.startAuthentication,
  browserSupportsWebAuthnAutofill: async () => false,
  WebAuthnAbortService: { cancelCeremony: vi.fn() },
}

function response(status: number) {
  return { status, headers: new Headers() } as unknown as Response
}

const OPTIONS_ANSWER = () => ({
  data: {
    options: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
    expiresAt: new Date(Date.now() + 5 * 60_000).toISOString(),
  },
  error: undefined,
  response: response(200),
})

const CODE_STARTED = {
  data: {
    pendingId: "p-1",
    maskedEmail: "a***@example.com",
    expiresAt: "2099-09-10T10:05:00.000Z",
  },
  response: new Response(null, { status: 200 }),
}

function passkeysRun() {
  mocks.post.mockImplementation(async (path: string) =>
    path === OPTIONS_PATH
      ? OPTIONS_ANSWER()
      : { data: undefined, error: { status: 404 }, response: response(404) }
  )
}

function passkeysOff() {
  mocks.post.mockResolvedValue({ data: undefined, error: { status: 404 }, response: response(404) })
}

/**
 * Passkeys run here, and the lookup answers per address: `holders` have one. The
 * code start answers a pending code. Anything else answers 404.
 */
function serverWithLookup(
  answer: (email: string) => unknown = (email) => ({
    data: { passkey: email.startsWith("holder") },
    response: response(200),
  })
) {
  mocks.post.mockImplementation(
    async (path: string, init?: { body?: { email?: string } }) => {
      if (path === OPTIONS_PATH) return OPTIONS_ANSWER()
      if (path === LOOKUP_PATH) return answer(init?.body?.email ?? "")
      if (path === CODE_START_PATH) return CODE_STARTED
      return {
        data: undefined,
        error: { status: 404 },
        response: response(404),
      }
    }
  )
}

/** The paths the page posted, in order, without the page-load options. */
function posted(): string[] {
  return mocks.post.mock.calls
    .map((call) => call[0] as string)
    .filter((path) => path !== OPTIONS_PATH)
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

/** The first screen has asked for the passkey options: the page knows whether passkeys run. */
async function optionsAsked() {
  await waitFor(() => expect(mocks.post).toHaveBeenCalledWith(OPTIONS_PATH))
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

/** The address, then the first screen's button: "Continue", or "Continue with email" in code mode. */
async function continueWith(email: string, button?: string) {
  const user = userEvent.setup()
  await user.type(screen.getByLabelText("auth.email"), email)
  const name =
    button ??
    (screen.queryByRole("button", { name: "auth.continue" })
      ? "auth.continue"
      : "auth.continueWithEmail")
  await user.click(screen.getByRole("button", { name }))
}

/** The passkey screen for `email`, its options in hand. */
async function onPasskeyScreen(email = "holder@example.com") {
  await optionsAsked()
  await continueWith(email)
  await screen.findByRole("heading", { name: "auth.passkey.screenTitle" })
  const button = await passkeyButton()
  await waitFor(() => expect(button).toBeEnabled())
  return button
}

/** The list "Try another way" opens: its items' names, in order. */
async function otherWays(): Promise<string[]> {
  await userEvent
    .setup()
    .click(screen.getByRole("button", { name: "auth.passkey.tryAnotherWay" }))
  const list = await screen.findByTestId("other-ways")
  return [...list.querySelectorAll("button, a")].map(
    (item) => item.textContent ?? ""
  )
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
  vi.useRealTimers()
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

  // OI-280 (T-A13, V9): off means off, whatever the options answer.
  it("keeps the address and the password on one screen while the hint is false, and asks nothing about the address", async () => {
    passkeysRun()
    mocks.login.mockResolvedValue(SIGNED_IN)
    mocks.branding.signInOptions = {
      ...PASSKEYS_PASSWORD_ONLY,
      passkeySignInOffered: false,
    }
    renderPage()
    await optionsAsked()

    expect(screen.getByLabelText("auth.password")).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "auth.continue" })
    ).not.toBeInTheDocument()
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()

    await signInWithPassword()

    await waitFor(() =>
      expect(mocks.complete).toHaveBeenCalledWith(SIGNED_IN, {
        proof: "password",
      })
    )
    expect(posted()).toEqual([])
  })

  it("sends the code with no other request while the hint is false, in code mode", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = {
      ...PASSKEYS_CODE_FIRST,
      passkeySignInOffered: false,
    }
    renderPage()
    await optionsAsked()

    await continueWith("holder@example.com", "auth.continueWithEmail")

    expect(await screen.findByText("code flow")).toBeInTheDocument()
    expect(posted()).toEqual([CODE_START_PATH])
  })

  it("is as before where the hint is true but the options answer 404", async () => {
    passkeysOff()
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()

    await waitFor(() =>
      expect(screen.getByLabelText("auth.password")).toBeInTheDocument()
    )
    expect(
      screen.getByRole("button", { name: "auth.signIn" })
    ).toBeInTheDocument()
  })
})

describe("the first screen while passkeys may run (OI-280, D-S20-11)", () => {
  it("has no passkey button for every visitor; password mode asks for the address and Continue", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()
    // The options answered 200: passkeys run, and the helper is in hand.
    await waitFor(() => expect(mocks.loadWebAuthn).toHaveBeenCalled())

    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("auth.password")).not.toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "auth.continue" })
    ).toHaveAttribute("data-variant", "default")
  })

  it("asks the lookup for the address typed, and sends nothing else first", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()

    await continueWith("someone@example.com")

    await waitFor(() =>
      expect(mocks.post).toHaveBeenCalledWith(
        LOOKUP_PATH,
        expect.objectContaining({ body: { email: "someone@example.com" } })
      )
    )
    expect(posted()[0]).toBe(LOOKUP_PATH)
  })

  it("code mode keeps its button and its words, and sends the code only after the answer `none`", async () => {
    let answer!: (value: unknown) => void
    serverWithLookup(
      () =>
        new Promise((resolve) => {
          answer = resolve
        })
    )
    mocks.branding.signInOptions = PASSKEYS_CODE_FIRST
    renderPage()
    await optionsAsked()

    await continueWith("someone@example.com", "auth.continueWithEmail")
    await waitFor(() => expect(posted()).toEqual([LOOKUP_PATH]))
    // While the answer is on its way, no code has been sent.
    expect(posted()).not.toContain(CODE_START_PATH)

    await act(async () =>
      answer({ data: { passkey: false }, response: response(200) })
    )

    expect(await screen.findByText("code flow")).toBeInTheDocument()
    expect(posted()).toEqual([LOOKUP_PATH, CODE_START_PATH])
  })

  it("password mode, `none`: the password step, with the address read-only and the way back", async () => {
    serverWithLookup()
    mocks.login.mockResolvedValue(SIGNED_IN)
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()

    await continueWith("someone@example.com")

    const password = await screen.findByLabelText("auth.password")
    const address = screen.getByLabelText("auth.email")
    expect(address).toHaveAttribute("readonly")
    expect(address).toHaveValue("someone@example.com")
    // Password managers pair the two (T-A5).
    expect(address.getAttribute("autocomplete")).toMatch(/^username/)
    expect(
      screen.getByRole("button", { name: "auth.useDifferentEmail" })
    ).toBeInTheDocument()
    expect(password).toHaveFocus()

    await userEvent.setup().type(password, "pw")
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "auth.signIn" }))
    await waitFor(() =>
      expect(mocks.complete).toHaveBeenCalledWith(SIGNED_IN, {
        proof: "password",
      })
    )
    expect(mocks.login).toHaveBeenCalledWith("someone@example.com", "pw", false)
  })

  it.each([
    [
      "a 500",
      () => ({
        data: undefined,
        error: { status: 500 },
        response: response(500),
      }),
    ],
    [
      "a 429",
      () => ({
        data: undefined,
        error: { status: 429 },
        response: response(429),
      }),
    ],
    [
      "a network failure",
      () => Promise.reject(new TypeError("Failed to fetch")),
    ],
  ])(
    "`unknown` (%s): the next step as before, and nothing said",
    async (_, answer) => {
      serverWithLookup(answer)
      mocks.branding.signInOptions = PASSKEYS_CODE_FIRST
      renderPage()
      await optionsAsked()

      await continueWith("holder@example.com", "auth.continueWithEmail")

      expect(await screen.findByText("code flow")).toBeInTheDocument()
      expect(mocks.toastError).not.toHaveBeenCalled()
    }
  )

  it("`unknown` after three seconds without an answer: the next step as before", async () => {
    serverWithLookup(
      () =>
        new Promise(() => {
          // Never answers: the page's own timer decides.
        })
    )
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()
    await userEvent
      .setup()
      .type(screen.getByLabelText("auth.email"), "holder@example.com")

    vi.useFakeTimers()
    fireEvent.click(screen.getByRole("button", { name: "auth.continue" }))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(2_900)
    })
    expect(screen.queryByLabelText("auth.password")).not.toBeInTheDocument()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(200)
    })
    vi.useRealTimers()

    expect(await screen.findByLabelText("auth.password")).toBeInTheDocument()
    expect(mocks.toastError).not.toHaveBeenCalled()
  })

  it("`off` (404): the next step as before, and the page stops asking for the rest of its life", async () => {
    serverWithLookup(() => ({
      data: undefined,
      error: { status: 404 },
      response: response(404),
    }))
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()

    await continueWith("holder@example.com")
    await screen.findByLabelText("auth.password")
    expect(mocks.toastError).not.toHaveBeenCalled()

    // Back at the address: the page as before, one screen, and no second question.
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "auth.useDifferentEmail" }))
    expect(await screen.findByLabelText("auth.password")).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "auth.signIn" })
    ).toBeInTheDocument()
    expect(posted().filter((path) => path === LOOKUP_PATH)).toHaveLength(1)
  })
})

describe("the passkey screen (OI-280, T-A7)", () => {
  it("shows the address read-only, the passkey button focused, and one link; nothing is sent", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = PASSKEYS_CODE_OFFERED_PASSWORD_FIRST
    renderPage()

    const button = await onPasskeyScreen()

    const address = screen.getByLabelText("auth.email")
    expect(address).toHaveAttribute("readonly")
    expect(address).toHaveValue("holder@example.com")
    expect(address).toHaveAttribute("dir", "ltr")
    expect(
      screen.getByRole("button", { name: "auth.useDifferentEmail" })
    ).toBeInTheDocument()
    expect(button).toHaveAttribute("data-variant", "default")
    await waitFor(() => expect(button).toHaveFocus())
    expect(
      screen.getByRole("button", { name: "auth.passkey.tryAnotherWay" })
    ).toHaveAttribute("aria-expanded", "false")
    // No code, no password field, no ceremony before the person chooses (D8).
    expect(posted()).toEqual([LOOKUP_PATH])
    expect(screen.queryByLabelText("auth.password")).not.toBeInTheDocument()
    expect(mocks.startAuthentication).not.toHaveBeenCalled()
  })

  it("signs in through the completion rule from the button's click, with no proof and no method remembered (T-C2, T-C6)", async () => {
    serverWithLookup()
    mocks.loginWithPasskey.mockResolvedValue(SIGNED_IN)
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    const button = await onPasskeyScreen()

    fireEvent.click(button)
    // Inside the click: nothing is awaited before the ceremony (T-C2).
    expect(mocks.startAuthentication).toHaveBeenCalledTimes(1)
    expect(mocks.startAuthentication).toHaveBeenCalledWith({
      optionsJSON: { challenge: "c1", rpId: "localhost", allowCredentials: [] },
    })

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1))
    expect(mocks.loginWithPasskey).toHaveBeenCalledWith(CREDENTIAL, false)
    expect(mocks.complete.mock.calls[0]).toEqual([SIGNED_IN])
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBeNull()
    expect(mocks.toastSuccess).toHaveBeenCalledWith("auth.welcomeBack")
  })

  it("carries the stay-signed-in box as it stands at the click", async () => {
    serverWithLookup()
    mocks.loginWithPasskey.mockResolvedValue(SIGNED_IN)
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    const button = await onPasskeyScreen()

    await userEvent.setup().click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    fireEvent.click(button)

    await waitFor(() => expect(mocks.loginWithPasskey).toHaveBeenCalledWith(CREDENTIAL, true))
  })

  it("sends an account waiting for deletion to the recovery page, as the password does", async () => {
    serverWithLookup()
    mocks.loginWithPasskey.mockRejectedValue({
      status: 403,
      code: "User.AccountPendingDeletion",
      detail: "Your account is scheduled for deletion.",
    })
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage({ recoveryPath: "/account-recovery" })

    fireEvent.click(await onPasskeyScreen())

    expect(await screen.findByTestId("recovery")).toHaveTextContent(
      "Your account is scheduled for deletion."
    )
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("says the API's sentence for a passkey it refused, with the way out for a removed one", async () => {
    serverWithLookup()
    mocks.loginWithPasskey.mockRejectedValue({
      status: 400,
      code: "Passkey.VerificationFailed",
      detail: "The passkey could not be verified.",
    })
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()

    fireEvent.click(await onPasskeyScreen())

    const message = await screen.findByTestId("passkey-message")
    expect(message).toHaveTextContent("The passkey could not be verified.")
    expect(message).toHaveTextContent(i18n.t("auth.passkey.removedPasskeyWayOut"))
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("goes back to the address with what was typed", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await onPasskeyScreen()

    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "auth.useDifferentEmail" }))

    const address = await screen.findByLabelText("auth.email")
    expect(address).not.toHaveAttribute("readonly")
    expect(address).toHaveValue("holder@example.com")
    expect(
      screen.getByRole("button", { name: "auth.continue" })
    ).toBeInTheDocument()
  })

  it("never leaves an empty screen: passkeys switched off since the answer show the list", async () => {
    let optionsRun = true
    mocks.post.mockImplementation(
      async (path: string, init?: { body?: { email?: string } }) => {
        if (path === OPTIONS_PATH) {
          return optionsRun
            ? OPTIONS_ANSWER()
            : {
                data: undefined,
                error: { status: 404 },
                response: response(404),
              }
        }
        if (path === LOOKUP_PATH)
          return {
            data: { passkey: init?.body?.email === "holder@example.com" },
            response: response(200),
          }
        return {
          data: undefined,
          error: { status: 404 },
          response: response(404),
        }
      }
    )
    // Switched off between the answer and the click: the sign-in's 404, then the options'.
    mocks.loginWithPasskey.mockImplementation(() => {
      optionsRun = false
      return Promise.reject({ status: 404, code: "Http.NotFound" })
    })
    mocks.branding.signInOptions = PASSKEYS_CODE_OFFERED_PASSWORD_FIRST
    renderPage()

    fireEvent.click(await onPasskeyScreen())

    await waitFor(() =>
      expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    )
    const list = await screen.findByTestId("other-ways")
    expect(
      within(list).getByRole("button", {
        name: "auth.signInWithPasswordInstead",
      })
    ).toBeInTheDocument()
    expect(
      within(list).getByRole("button", { name: "auth.signInWithEmailCode" })
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "auth.passkey.tryAnotherWay" })
    ).not.toBeInTheDocument()
  })
})

describe("Try another way (OI-280, T-A8, D-S20-16)", () => {
  it("opens the list in place, under the passkey button, which stays", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = PASSKEYS_CODE_OFFERED_PASSWORD_FIRST
    renderPage()
    const button = await onPasskeyScreen()

    const items = await otherWays()

    expect(items).toEqual([
      "auth.signInWithEmailCode",
      "auth.signInWithPasswordInstead",
    ])
    expect(
      screen.getByRole("button", { name: "auth.passkey.tryAnotherWay" })
    ).toHaveAttribute("aria-expanded", "true")
    expect(precedes(button, screen.getByTestId("other-ways"))).toBe(true)
    expect(
      screen.getByText("auth.passkey.otherWaysHeading")
    ).toBeInTheDocument()
  })

  it.each([
    ["the code offered", PASSKEYS_CODE_OFFERED_PASSWORD_FIRST],
    ["code first", PASSKEYS_CODE_FIRST],
    ["no code", PASSKEYS_PASSWORD_ONLY],
  ])("is the same list for every account, with %s", async (_, options) => {
    function Provider() {
      return <button type="button">provider</button>
    }
    serverWithLookup(() => ({
      data: { passkey: true },
      response: response(200),
    }))
    mocks.branding.signInOptions = options
    const lists: string[][] = []
    for (const email of ["holder.one@example.com", "holder.two@example.org"]) {
      const { unmount } = renderPage({ providers: <Provider /> })
      await onPasskeyScreen(email)
      lists.push(await otherWays())
      unmount()
    }

    expect(lists[0]).toEqual(lists[1])
    // The deployment's options decide it: the code where it is offered, then the
    // password, then the providers.
    expect(lists[0]).toEqual([
      ...(options.codeSignInOffered ? ["auth.signInWithEmailCode"] : []),
      "auth.signInWithPasswordInstead",
      "provider",
    ])
  })

  it("the code: today's code, sent for the address on the screen, then the code step", async () => {
    serverWithLookup()
    mocks.branding.signInOptions = PASSKEYS_CODE_OFFERED_PASSWORD_FIRST
    renderPage()
    await onPasskeyScreen()
    await otherWays()

    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "auth.signInWithEmailCode" }))

    expect(await screen.findByText("code flow")).toBeInTheDocument()
    expect(mocks.post).toHaveBeenCalledWith(
      CODE_START_PATH,
      expect.objectContaining({
        body: expect.objectContaining({ email: "holder@example.com" }),
      })
    )
  })

  it("the password: the password step for the address on the screen", async () => {
    serverWithLookup()
    mocks.login.mockResolvedValue(SIGNED_IN)
    mocks.branding.signInOptions = PASSKEYS_CODE_OFFERED_PASSWORD_FIRST
    renderPage()
    await onPasskeyScreen()
    await otherWays()

    await userEvent
      .setup()
      .click(
        screen.getByRole("button", { name: "auth.signInWithPasswordInstead" })
      )

    const password = await screen.findByLabelText("auth.password")
    expect(screen.getByLabelText("auth.email")).toHaveValue(
      "holder@example.com"
    )
    await userEvent.setup().type(password, "pw")
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "auth.signIn" }))
    await waitFor(() =>
      expect(mocks.login).toHaveBeenCalledWith(
        "holder@example.com",
        "pw",
        false
      )
    )
  })

  it("a provider runs its own flow, and hands a refusal that asks for the passkey to the page", async () => {
    serverWithLookup()
    let handed: boolean | null = null
    function Provider() {
      const report = usePasskeyRequiredReporter()
      return (
        <button
          type="button"
          onClick={() => (handed = report(PASSKEY_REQUIRED))}
        >
          provider
        </button>
      )
    }
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage({ providers: <Provider /> })
    await onPasskeyScreen()
    await otherWays()

    fireEvent.click(screen.getByRole("button", { name: "provider" }))

    expect(handed).toBe(true)
  })

  it("under mfa_passkey holds the way out for a lost passkey alone (D-152-2)", async () => {
    serverWithLookup()
    mocks.completion.reason = { reason: "mfa_passkey", method: null }
    mocks.branding.signInOptions = PASSKEYS_CODE_OFFERED_PASSWORD_FIRST
    renderPage({ providers: <button type="button">provider</button> })
    // The reason keeps its own link, and the passkey leads, on the first screen (T-A6, T-A9).
    expect(
      within(await screen.findByTestId("sign-in-reason")).getByTestId(
        "passkey-lost-link"
      )
    ).toBeInTheDocument()

    await onPasskeyScreen()
    const items = await otherWays()

    expect(items).toEqual(["auth.passkey.lostIt"])
    expect(
      within(screen.getByTestId("other-ways")).getByTestId("passkey-lost-link")
    ).toHaveAttribute("href", "/login")
  })
})

describe("the doors that ask for the passkey (IN 5, D-S20-4 (ii-s))", () => {
  it("the password: keeps the API's sentence on the page, and the passkey comes first, focused", async () => {
    passkeysRun()
    mocks.login.mockRejectedValue(PASSKEY_REQUIRED)
    renderPage()
    await optionsAsked()
    await waitFor(() => expect(mocks.loadWebAuthn).toHaveBeenCalled())

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

  it("the password step: back to the address, with the sentence, and the passkey first", async () => {
    serverWithLookup()
    mocks.login.mockRejectedValue(PASSKEY_REQUIRED)
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()
    await optionsAsked()

    await continueWith("someone@example.com")
    await userEvent
      .setup()
      .type(await screen.findByLabelText("auth.password"), "pw")
    await userEvent
      .setup()
      .click(screen.getByRole("button", { name: "auth.signIn" }))

    expect(
      await screen.findByTestId("passkey-required-note")
    ).toHaveTextContent(PASSKEY_REQUIRED.detail)
    const passkey = await passkeyButton()
    expect(precedes(passkey, screen.getByLabelText("auth.email"))).toBe(true)
    expect(screen.getByLabelText("auth.email")).toHaveValue(
      "someone@example.com"
    )
    await waitFor(() => expect(passkey).toHaveFocus())
  })

  it("the password, after the first options failed: the passkey is asked for at once, and comes", async () => {
    let optionsCalls = 0
    mocks.post.mockImplementation(async (path: string) => {
      if (path !== OPTIONS_PATH) return { data: undefined, error: { status: 404 }, response: response(404) }
      optionsCalls += 1
      return optionsCalls === 1
        ? { data: undefined, error: { status: 503 }, response: response(503) }
        : OPTIONS_ANSWER()
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

    await act(async () => answer(OPTIONS_ANSWER()))
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
    await optionsAsked()

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
      path === OPTIONS_PATH ? OPTIONS_ANSWER() : CODE_STARTED
    )
    renderPage()
    await optionsAsked()

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

describe("the three passkey-first entries keep the leading button (T-A6)", () => {
  it("the sign-in again of a passkey account (T-C7): the passkey first, and a code sign-in from here still proves the password", async () => {
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

  it("a password sign-in from there clears the hint, so nothing keeps the page on the passkey by itself", async () => {
    // T-C7's third line, as far as a page can hold it: if the change then asks
    // again, the dialog opens again with the same words. That the person does not
    // go round by a password is the dialog's sentence, and only the sentence.
    passkeysRun()
    mocks.login.mockResolvedValue(SIGNED_IN)
    requestPasskeySignIn()
    renderPage()
    await passkeyButton()

    await signInWithPassword()

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1))
    expect(mocks.complete).toHaveBeenCalledWith(SIGNED_IN, { proof: "password" })
    expect(window.sessionStorage.getItem(FULL_PROOF_KEY)).toBeNull()
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBe("password")
  })

  it("an application's requirement only the passkey meets (mfa_passkey) puts it first too", async () => {
    passkeysRun()
    mocks.completion.reason = { reason: "mfa_passkey", method: null }
    renderPage()

    const passkey = await passkeyButton()
    expect(precedes(passkey, screen.getByLabelText("auth.email"))).toBe(true)
    expect(screen.getByTestId("sign-in-reason")).toHaveTextContent("auth.signInReasonPasskeyTitle")
  })

  it("under mfa_passkey offers the way out for a lost passkey; no other reason does (D-152-2)", async () => {
    passkeysRun()
    mocks.completion.reason = { reason: "mfa_passkey", method: null }
    const { unmount } = renderPage()

    expect(within(await screen.findByTestId("sign-in-reason")).getByTestId("passkey-lost-link")).toHaveAttribute(
      "href",
      "/login"
    )
    unmount()

    mocks.completion.reason = { reason: "mfa_step_up", method: null }
    renderPage()
    expect(await screen.findByTestId("sign-in-reason")).toBeInTheDocument()
    expect(screen.queryByTestId("passkey-lost-link")).not.toBeInTheDocument()
  })

  it("with passkeys on, the leading button stays above the address, and Continue below it still asks", async () => {
    serverWithLookup()
    mocks.completion.reason = { reason: "mfa_passkey", method: null }
    mocks.branding.signInOptions = PASSKEYS_PASSWORD_ONLY
    renderPage()

    const passkey = await passkeyButton()
    expect(precedes(passkey, screen.getByLabelText("auth.email"))).toBe(true)
    expect(
      screen.getByRole("button", { name: "auth.continue" })
    ).toHaveAttribute("data-variant", "secondary")

    await continueWith("someone@example.com")
    await waitFor(() => expect(posted()).toEqual([LOOKUP_PATH]))
  })
})
