import type * as React from "react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes, useNavigate } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * OI-280 PR B (T-B9, V-B8): the offer's page decides before it shows anything, and
 * while it decides there is a spinner and nothing else. This device must be able to
 * make a passkey of its own (D-B6), and the account must hold none and be able to
 * add one (N9). Otherwise the sign-in goes on at once. «Add» runs the one addition
 * every page shares; «Not now» stores nothing; «Don't ask on this device» stores one
 * flag. Every way out is the completion, with the sign-in's own proof.
 *
 * Deliberate breaks, each verified red: an `await` before `startRegistration` in the
 * click; the screen shown before the account answered; the way out left with
 * `endsSignIn: false`.
 */

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  complete: vi.fn(),
  platformAvailable: vi.fn(),
  loadWebAuthn: vi.fn(),
  startRegistration: vi.fn(),
  cancelCeremony: vi.fn(),
  logout: vi.fn(),
  toastSuccess: vi.fn(),
  signInOptions: { passkeySignInOffered: true } as { passkeySignInOffered?: boolean } | null,
  completion: { returnTo: null as string | null, reason: null as unknown },
}))

vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/api/client", () => ({
  api: { GET: mocks.get, POST: mocks.post },
  refreshSessionNow: () => Promise.resolve(true),
}))
vi.mock("@authsystem/ui/branding", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/ui/branding")>()),
  useSignInOptions: () => mocks.signInOptions,
}))
vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({ ...mocks.completion, complete: mocks.complete }),
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
  platformAuthenticatorAvailable: mocks.platformAvailable,
}))
vi.mock("../auth-context", () => ({ useAuth: () => ({ logout: mocks.logout }) }))
// «Passkeys answered off» is kept for the page's life, so a test that sets it would leak into the
// next: the page's view of it goes through a store each test resets.
const answeredOff = vi.hoisted(() => {
  let off = false
  const listeners = new Set<() => void>()
  return {
    note: () => {
      if (off) return
      off = true
      for (const listener of listeners) listener()
    },
    reset: () => {
      off = false
    },
    get: () => off,
    subscribe: (listener: () => void) => {
      listeners.add(listener)
      return () => {
        listeners.delete(listener)
      }
    },
  }
})
vi.mock("../passkey/passkey-offer", async (importOriginal) => {
  const { useSyncExternalStore } = await import("react")
  return {
    ...(await importOriginal<typeof import("../passkey/passkey-offer")>()),
    notePasskeysOff: answeredOff.note,
    usePasskeysAnsweredOff: () => useSyncExternalStore(answeredOff.subscribe, answeredOff.get, () => false),
  }
})
vi.mock("sonner", () => ({ toast: { success: mocks.toastSuccess, error: vi.fn() } }))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({
    title,
    subtitle,
    children,
  }: {
    title: string
    subtitle?: string
    children: React.ReactNode
  }) => (
    <div>
      <h1>{title}</h1>
      <p>{subtitle}</p>
      {children}
    </div>
  ),
}))

import {
  PASSKEY_OFFER_OFF_KEY,
  notePasskeyOfferNavigation,
  pendingPasskeyOfferNavigation,
} from "../passkey/passkey-offer"
import { PasskeyOfferPage } from "./passkey-offer"

const OPTIONS_PATH = "/api/v1/auth/passkeys/registration/options"
const VERIFY_PATH = "/api/v1/auth/passkeys/registration/verify"
const ME = { id: "u-1", email: "person@example.test", passkeyCount: 0, emailCodeAvailable: true, qualifyingMfa: false }
const REGISTRATION = {
  options: { challenge: "r1", rp: { id: "localhost", name: "Example" }, user: { id: "aGFuZGxl" } },
  expiresAt: "2099-01-01T00:00:00Z",
  emailCodeRequired: false,
}
const CREATED = { id: "cred-new", rawId: "cred-new", type: "public-key" }
const EXIT = { proof: "code", launchUrl: null }

function response(status: number) {
  return { status, ok: status >= 200 && status < 300, headers: new Headers() } as unknown as Response
}
const ok = <T,>(data: T) => ({ data, error: undefined, response: response(200) })
const problem = (status: number, code: string) => ({
  data: undefined,
  error: { status, code, detail: `server sentence for ${code}` },
  response: response(status),
})

function givenAccount(account: Record<string, unknown> | "fails" | "pending" = ME) {
  mocks.get.mockImplementation((path: string) => {
    if (path === "/api/v1/Users/me") {
      if (account === "pending") return new Promise(() => {})
      return Promise.resolve(account === "fails" ? problem(500, "Http.InternalServerError") : ok(account))
    }
    if (path === "/api/v1/auth/reauthentication") {
      return Promise.resolve(ok({ passkey: false, password: true, emailCode: false, secondStep: "None", canProveInPlace: true }))
    }
    return Promise.resolve(problem(404, "Http.NotFound"))
  })
}

function renderOffer(state: unknown = { from: "/users", passkeyOffer: EXIT }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[{ pathname: "/passkey/offer", state }]}>
        <Routes>
          <Route path="/passkey/offer" element={<PasskeyOfferPage />} />
          <Route path="/login" element={<p>sign-in page</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const title = () => i18n.t("auth.passkeyOffer.title")

async function offerShown() {
  return screen.findByRole("heading", { name: title() })
}

beforeEach(() => {
  vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
  mocks.get.mockReset()
  mocks.post.mockReset()
  mocks.complete.mockReset()
  mocks.logout.mockReset()
  mocks.toastSuccess.mockReset()
  mocks.platformAvailable.mockReset().mockResolvedValue(true)
  mocks.startRegistration.mockReset().mockResolvedValue(CREATED)
  mocks.cancelCeremony.mockReset()
  mocks.loadWebAuthn.mockReset().mockResolvedValue({
    startRegistration: mocks.startRegistration,
    WebAuthnAbortService: { cancelCeremony: mocks.cancelCeremony },
  })
  mocks.signInOptions = { passkeySignInOffered: true }
  mocks.completion.returnTo = null
  mocks.completion.reason = null
  window.localStorage.removeItem(PASSKEY_OFFER_OFF_KEY)
  answeredOff.reset()
  givenAccount()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("the offer decides before it shows anything (e, f, g)", () => {
  it.each([
    ["this device cannot make a passkey of its own (e)", () => mocks.platformAvailable.mockResolvedValue(false)],
    ["the account already holds one (f)", () => givenAccount({ ...ME, passkeyCount: 1 })],
    ["the account says nothing of its passkeys", () => givenAccount({ ...ME, passkeyCount: undefined })],
    ["adding one would end in N9: email off and no factor (g)", () => givenAccount({ ...ME, emailCodeAvailable: false })],
    ["the account cannot be read", () => givenAccount("fails")],
    ["the deployment says passkeys do not run", () => {
      mocks.signInOptions = { passkeySignInOffered: false }
    }],
    // REVIEW (OI-280 PR B) finding 1: a page reached another way honours «Don't ask» too.
    ["this browser asked never to be offered again", () => {
      window.localStorage.setItem(PASSKEY_OFFER_OFF_KEY, "1")
    }],
  ])("goes on at once, with nothing shown, when %s", async (_label, given) => {
    given()
    renderOffer()

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1))
    expect(mocks.complete).toHaveBeenCalledWith({}, { proof: "code", launchUrl: null })
    expect(screen.queryByRole("heading", { name: title() })).not.toBeInTheDocument()
  })

  it("shows it for an account with no passkey that may add one, with the factor it has (email off, the app on)", async () => {
    givenAccount({ ...ME, emailCodeAvailable: false, qualifyingMfa: true })
    renderOffer()

    expect(await offerShown()).toBeInTheDocument()
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("shows nothing but a spinner while the account has not answered", async () => {
    givenAccount("pending")
    renderOffer()

    await waitFor(() => expect(mocks.get).toHaveBeenCalledWith("/api/v1/Users/me"))
    expect(screen.queryByRole("heading", { name: title() })).not.toBeInTheDocument()
    expect(screen.queryByRole("button")).not.toBeInTheDocument()
    expect(mocks.complete).not.toHaveBeenCalled()
  })
})

/**
 * REVIEW (OI-280 PR B), findings 1 and 5. Deliberate breaks, verified red: one way out for the
 * page's whole life, as before (a new arrival could never leave); the addition's 404 not passed on.
 */
describe("the offer's ways out, again and when passkeys turn out off (REVIEW)", () => {
  it("leaves again after a new arrival on the same page", async () => {
    function Again() {
      const navigate = useNavigate()
      return (
        <button
          onClick={() => navigate("/passkey/offer", { state: { from: "/users", passkeyOffer: EXIT } })}
        >
          again
        </button>
      )
    }
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={[{ pathname: "/passkey/offer", state: { from: "/users", passkeyOffer: EXIT } }]}>
          <Again />
          <Routes>
            <Route path="/passkey/offer" element={<PasskeyOfferPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    )
    await offerShown()

    await userEvent.click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.notNow") }))
    expect(mocks.complete).toHaveBeenCalledTimes(1)
    await userEvent.click(screen.getByRole("button", { name: "again" }))
    await userEvent.click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.notNow") }))

    expect(mocks.complete).toHaveBeenCalledTimes(2)
  })

  it("goes on when «Add» learns that passkeys are off (the options answer 404)", async () => {
    mocks.post.mockImplementation((path: string) =>
      Promise.resolve(
        path === "/api/v1/auth/passkeys/registration/options"
          ? problem(404, "Http.NotFound")
          : problem(500, "Http.InternalServerError")
      )
    )
    renderOffer()
    await offerShown()

    await userEvent.click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.add") }))

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledWith({}, { proof: "code", launchUrl: null }))
    expect(mocks.complete).toHaveBeenCalledTimes(1)
  })
})

describe("the offer's screen", () => {
  it("clears the note the sign-in pages' guard followed here, once it opens", async () => {
    notePasskeyOfferNavigation({ from: "/users", passkeyOffer: EXIT })
    renderOffer()

    await waitFor(() => expect(pendingPasskeyOfferNavigation()).toBeNull())
  })

  it("says what a passkey is, and offers Add, Not now and Don't ask on this device", async () => {
    renderOffer()
    await offerShown()

    expect(screen.getByText(i18n.t("auth.passkeyOffer.body"))).toBeInTheDocument()
    const offer = screen.getByTestId("passkey-offer")
    expect(within(offer).getAllByRole("button").map((button) => button.textContent)).toEqual([
      i18n.t("auth.passkeyOffer.add"),
      i18n.t("auth.passkeyOffer.notNow"),
      i18n.t("auth.passkeyOffer.dontAsk"),
    ])
  })

  it("«Not now» stores nothing and goes on with the sign-in's own proof", async () => {
    renderOffer({ from: "/users", passkeyOffer: { proof: "password", launchUrl: "https://crm.example.com/start" } })
    await offerShown()

    await userEvent.setup().click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.notNow") }))

    expect(mocks.complete).toHaveBeenCalledTimes(1)
    expect(mocks.complete).toHaveBeenCalledWith(
      {},
      { proof: "password", launchUrl: "https://crm.example.com/start" }
    )
    expect(window.localStorage.getItem(PASSKEY_OFFER_OFF_KEY)).toBeNull()
  })

  it("«Don't ask on this device» stores one flag, and goes on", async () => {
    renderOffer()
    await offerShown()

    await userEvent.setup().click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.dontAsk") }))

    expect(window.localStorage.getItem(PASSKEY_OFFER_OFF_KEY)).toBe("1")
    expect(mocks.complete).toHaveBeenCalledWith({}, { proof: "code", launchUrl: null })
  })

  it("«Add» runs the addition: the ceremony starts inside the click, and the passkey added, it says so and goes on", async () => {
    mocks.post.mockImplementation(async (path: string) =>
      path === OPTIONS_PATH ? ok(REGISTRATION) : ok({ id: "p-1", name: "Laptop" })
    )
    const user = userEvent.setup()
    renderOffer()
    await offerShown()

    await user.click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.add") }))
    const dialog = await screen.findByRole("dialog", { name: i18n.t("profile.passkeys.addTitle") })
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))
    // T-C2: nothing awaited between the click and the ceremony.
    expect(mocks.startRegistration).toHaveBeenCalledTimes(1)

    await waitFor(() => expect(mocks.complete).toHaveBeenCalledTimes(1))
    expect(mocks.post).toHaveBeenCalledWith(VERIFY_PATH, {
      body: { credential: CREATED, name: null, emailCode: null },
    })
    expect(mocks.toastSuccess).toHaveBeenCalledWith(i18n.t("auth.passkeyOffer.added"))
    expect(mocks.complete).toHaveBeenCalledWith({}, { proof: "code", launchUrl: null })
  })

  it("«Add» opens the window where the gate asks for a fresh proof (a password account signed in by code)", async () => {
    mocks.post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    renderOffer()
    await offerShown()

    await userEvent.setup().click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.add") }))

    const window_ = await screen.findByRole("dialog", { name: i18n.t("auth.reauthenticate.title") })
    expect(await within(window_).findByLabelText(i18n.t("auth.password"))).toBeInTheDocument()
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("«Add» asks for the emailed code where the gate asks for it", async () => {
    mocks.post.mockResolvedValue(ok({ ...REGISTRATION, emailCodeRequired: true }))
    renderOffer()
    await offerShown()

    await userEvent.setup().click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.add") }))

    const dialog = await screen.findByRole("dialog", { name: i18n.t("profile.passkeys.addTitle") })
    expect(within(dialog).getByText(i18n.t("profile.passkeys.emailCodeNeeded"))).toBeInTheDocument()
  })

  it("a device that refused keeps the screen, with a plain sentence and «Not now» still there", async () => {
    mocks.post.mockResolvedValue(ok(REGISTRATION))
    mocks.startRegistration.mockRejectedValue(Object.assign(new Error("no"), { name: "SecurityError" }))
    const user = userEvent.setup()
    renderOffer()
    await offerShown()

    await user.click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.add") }))
    const dialog = await screen.findByRole("dialog", { name: i18n.t("profile.passkeys.addTitle") })
    fireEvent.click(within(dialog).getByRole("button", { name: i18n.t("profile.passkeys.create") }))
    expect(await within(dialog).findByTestId("add-passkey-message")).toHaveTextContent(
      i18n.t("profile.passkeys.deviceRefused")
    )
    await user.click(within(dialog).getByRole("button", { name: i18n.t("common.cancel") }))

    expect(await screen.findByTestId("passkey-offer-message")).toHaveTextContent(
      i18n.t("auth.passkeyOffer.notAdded")
    )
    expect(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.notNow") })).toBeEnabled()
    expect(mocks.complete).not.toHaveBeenCalled()
  })

  it("the window's way out keeps an application's request: the sign-in page with it, never a sign-out", async () => {
    mocks.completion.returnTo =
      "https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=" + "d".repeat(64)
    mocks.post.mockResolvedValue(problem(403, "Auth.ReauthenticationRequired"))
    const user = userEvent.setup()
    renderOffer()
    await offerShown()

    await user.click(screen.getByRole("button", { name: i18n.t("auth.passkeyOffer.add") }))
    const window_ = await screen.findByRole("dialog", { name: i18n.t("auth.reauthenticate.title") })
    await user.click(within(window_).getByRole("button", { name: i18n.t("auth.reauthenticate.signOutLink") }))
    await user.click(await screen.findByRole("button", { name: i18n.t("auth.reauthenticateAction") }))

    expect(await screen.findByText("sign-in page")).toBeInTheDocument()
    expect(mocks.logout).not.toHaveBeenCalled()
  })
})
