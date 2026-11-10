import { act, fireEvent, render, screen } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

/**
 * The passkey sign-in of a page (S20 PR C, PK15; T-C2 to T-C5, ARR-0668): what
 * the sign-in page's button and suggestion, the two-factor page's passkey step
 * and the recovery page's passkey choice share. The pages' own answers are their
 * tests'; this is the ceremony: when the options are asked for, when the helper
 * loads, what a click may do, when the options are renewed, and what each
 * failure says.
 */

const mocks = vi.hoisted(() => ({
  post: vi.fn(),
  loadWebAuthn: vi.fn(),
  startAuthentication: vi.fn(),
  autofill: vi.fn(),
  cancelCeremony: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({ api: { POST: mocks.post } }))

// The real loader, but the helper it hands over is this one: the import guard
// (webauthn-import.test.ts) keeps the loader the only way to the package.
vi.mock("./webauthn", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./webauthn")>()),
  loadWebAuthn: mocks.loadWebAuthn,
}))

vi.mock("sonner", () => ({ toast: { error: mocks.toastError, success: vi.fn() } }))

import { PasskeyButton } from "./passkey-button"
import { usePasskeySignIn, type PasskeyOutcome } from "./use-passkey-sign-in"
import type { AuthenticationResponseJSON } from "./webauthn"

const OPTIONS_PATH = "/api/v1/auth/passkeys/assertion/options"
const LABEL = "Sign in with a passkey"
const CREDENTIAL = { id: "cred-1", rawId: "cred-1", type: "public-key" } as unknown as AuthenticationResponseJSON

const HELPER = {
  startAuthentication: mocks.startAuthentication,
  browserSupportsWebAuthnAutofill: mocks.autofill,
  WebAuthnAbortService: { cancelCeremony: mocks.cancelCeremony },
}

function options(challenge: string) {
  return { challenge, rpId: "localhost", allowCredentials: [], userVerification: "required", timeout: 300_000 }
}

function response(status: number, headers: Record<string, string> = {}) {
  return { status, headers: new Headers(headers) } as unknown as Response
}

/** The server's options, valid for `expiresInMs` from now. */
function optionsAnswer(challenge: string, expiresInMs = 5 * 60_000) {
  return {
    data: { options: options(challenge), expiresAt: new Date(Date.now() + expiresInMs).toISOString() },
    error: undefined,
    response: response(200),
  }
}

function problemAnswer(status: number, code?: string, headers?: Record<string, string>) {
  return {
    data: undefined,
    error: { status, ...(code ? { code, detail: `server sentence for ${code}` } : {}) },
    response: response(status, headers),
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function cancelled(name: "NotAllowedError" | "AbortError") {
  return Object.assign(new Error(name), { name })
}

/** Runs the due timers and every promise they let go, inside React's act. */
async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
    for (let turn = 0; turn < 10; turn++) await Promise.resolve()
  })
}

function optionsRequests() {
  return mocks.post.mock.calls.filter(([path]) => path === OPTIONS_PATH).length
}

function Probe({
  suggestion = false,
  passkeysExpected = false,
  onCredential,
}: {
  suggestion?: boolean
  passkeysExpected?: boolean
  onCredential: (credential: AuthenticationResponseJSON) => Promise<PasskeyOutcome>
}) {
  const ceremony = usePasskeySignIn({ suggestion, passkeysExpected, onCredential })
  return (
    <>
      <PasskeyButton ceremony={ceremony} label={LABEL} />
      <output data-testid="phase">{ceremony.phase}</output>
      {/* What a page does when a door answered that the account signs in with its passkey. */}
      <button type="button" onClick={ceremony.confirm}>
        a door asked for the passkey
      </button>
    </>
  )
}

function renderProbe(
  onCredential: (credential: AuthenticationResponseJSON) => Promise<PasskeyOutcome> = async () => ({}),
  suggestion = false,
  passkeysExpected = false
) {
  return render(
    <Probe suggestion={suggestion} passkeysExpected={passkeysExpected} onCredential={onCredential} />
  )
}

/** A name as the button reads it: a spinner, while one turns, says "Loading" first. */
function named(text: string) {
  return new RegExp(text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"))
}

function button() {
  return screen.getByRole("button", { name: named(LABEL) })
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setTimeout", "clearTimeout", "Date"] })
  vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
  mocks.post.mockReset()
  mocks.loadWebAuthn.mockReset().mockResolvedValue(HELPER)
  mocks.startAuthentication.mockReset().mockReturnValue(new Promise(() => {}))
  mocks.autofill.mockReset().mockResolvedValue(false)
  mocks.cancelCeremony.mockReset()
  mocks.toastError.mockReset()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe("when the options are asked for, and when the helper loads (T-C1, T-C4)", () => {
  it("shows nothing and loads nothing until the options answer 200, then the button is ready", async () => {
    const first = deferred<ReturnType<typeof optionsAnswer>>()
    mocks.post.mockReturnValueOnce(first.promise)
    renderProbe()

    await flush()
    expect(optionsRequests()).toBe(1)
    expect(mocks.loadWebAuthn).not.toHaveBeenCalled()
    // Not even a disabled button: a deployment without passkeys must not flash one.
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()

    first.resolve(optionsAnswer("c1"))
    await flush()
    expect(mocks.loadWebAuthn).toHaveBeenCalledTimes(1)
    expect(button()).toBeEnabled()
  })

  it("keeps the button away while the helper is still loading", async () => {
    const helper = deferred<typeof HELPER>()
    mocks.loadWebAuthn.mockReturnValueOnce(helper.promise)
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1"))
    renderProbe()

    await flush()
    expect(mocks.loadWebAuthn).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole("button", { name: named(LABEL) })).not.toBeInTheDocument()

    helper.resolve(HELPER)
    await flush()
    expect(button()).toBeEnabled()
  })

  it("hides everything when the options answer 404: passkeys do not run here", async () => {
    mocks.post.mockResolvedValue(problemAnswer(404, "General.NotFound"))
    renderProbe()

    await flush()
    expect(screen.getByTestId("phase")).toHaveTextContent("unavailable")
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    expect(mocks.loadWebAuthn).not.toHaveBeenCalled()

    // Nothing is asked again while the page stays.
    await flush(10 * 60_000)
    expect(optionsRequests()).toBe(1)
  })

  it("asks nothing of a browser without WebAuthn", async () => {
    vi.unstubAllGlobals()
    renderProbe()

    await flush()
    expect(mocks.post).not.toHaveBeenCalled()
    expect(screen.getByTestId("phase")).toHaveTextContent("unavailable")
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
  })

  it("asks nothing of a browser without WebAuthn when a door asks for the passkey: a button there could only fail", async () => {
    vi.unstubAllGlobals()
    renderProbe()
    await flush()

    fireEvent.click(screen.getByRole("button", { name: "a door asked for the passkey" }))
    await flush()

    expect(mocks.post).not.toHaveBeenCalled()
    expect(screen.getByTestId("phase")).toHaveTextContent("unavailable")
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
  })
})

describe("the click (T-C2)", () => {
  it("calls startAuthentication synchronously, inside the click", async () => {
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1"))
    renderProbe()
    await flush()

    fireEvent.click(button())

    // Not awaited: no microtask ran between the click and the ceremony.
    expect(mocks.startAuthentication).toHaveBeenCalledTimes(1)
    expect(mocks.startAuthentication).toHaveBeenCalledWith({ optionsJSON: options("c1") })
    expect(screen.getByRole("button", { name: named(i18n.t("auth.passkey.waiting")) })).toBeDisabled()
  })

  it("starts one ceremony for a double click", async () => {
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1"))
    renderProbe()
    await flush()

    const control = button()
    fireEvent.click(control)
    fireEvent.click(control)

    expect(mocks.startAuthentication).toHaveBeenCalledTimes(1)
  })
})

describe("the renewal (T-C3)", () => {
  it.each([
    ["five minutes", 5 * 60_000, 4 * 60_000],
    ["twenty minutes (a clock ahead of the server's)", 20 * 60_000, 4 * 60_000],
    ["forty-five seconds (a clock behind it)", 45_000, 30_000],
  ])("renews options that expire in %s a minute before, within bounds", async (_label, expiresIn, renewsAfter) => {
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1", expiresIn)).mockResolvedValueOnce(optionsAnswer("c2"))
    renderProbe()
    await flush()

    await flush(renewsAfter - 1)
    expect(optionsRequests()).toBe(1)

    await flush(1)
    expect(optionsRequests()).toBe(2)

    fireEvent.click(button())
    expect(mocks.startAuthentication).toHaveBeenLastCalledWith({ optionsJSON: options("c2") })
  })

  it("asks for fresh options after every answer was checked, and holds the button until they come", async () => {
    const second = deferred<ReturnType<typeof optionsAnswer>>()
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1")).mockReturnValueOnce(second.promise)
    mocks.startAuthentication.mockResolvedValueOnce(CREDENTIAL)
    const onCredential = vi.fn(async () => ({ message: "That passkey was refused." }))
    renderProbe(onCredential)
    await flush()

    fireEvent.click(button())
    await flush()

    expect(onCredential).toHaveBeenCalledWith(CREDENTIAL)
    expect(optionsRequests()).toBe(2)
    expect(screen.getByText("That passkey was refused.")).toBeInTheDocument()
    // The challenge was spent: no click until the new one is here.
    expect(button()).toBeDisabled()

    second.resolve(optionsAnswer("c2"))
    await flush()
    expect(button()).toBeEnabled()
    fireEvent.click(button())
    expect(mocks.startAuthentication).toHaveBeenLastCalledWith({ optionsJSON: options("c2") })
  })

  it("asks for nothing more once the page is leaving", async () => {
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1"))
    mocks.startAuthentication.mockResolvedValueOnce(CREDENTIAL)
    renderProbe(async () => ({ leaving: true }))
    await flush()

    fireEvent.click(button())
    await flush()

    expect(optionsRequests()).toBe(1)
  })

  it("stops everything when the page goes: the ceremony is aborted, and nothing is renewed", async () => {
    mocks.post.mockResolvedValue(optionsAnswer("c1"))
    const { unmount } = renderProbe()
    await flush()

    unmount()
    expect(mocks.cancelCeremony).toHaveBeenCalled()

    await flush(10 * 60_000)
    expect(optionsRequests()).toBe(1)
  })
})

describe("what a failure says (T-C4, ARR-0668)", () => {
  it.each([
    ["the person closed the browser's dialog", cancelled("NotAllowedError")],
    ["the page started another ceremony", cancelled("AbortError")],
    ["the helper's own abort", Object.assign(new Error("aborted"), { code: "ERROR_CEREMONY_ABORTED" })],
  ])("says nothing when %s, and keeps the options", async (_label, error) => {
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1"))
    mocks.startAuthentication.mockRejectedValueOnce(error)
    const onCredential = vi.fn(async () => ({}))
    renderProbe(onCredential)
    await flush()

    fireEvent.click(button())
    await flush()

    expect(screen.queryByTestId("passkey-message")).not.toBeInTheDocument()
    expect(mocks.toastError).not.toHaveBeenCalled()
    expect(onCredential).not.toHaveBeenCalled()
    expect(button()).toBeEnabled()
    // The challenge was not spent: the same options stay in hand.
    expect(optionsRequests()).toBe(1)
  })

  // Whatever the ceremony raised is the browser's: never the server's unknown
  // fault, whose sentence sends the person to support. A device check (2026-11-10)
  // saw the browser refuse the press without asking the platform's authenticator,
  // and the page say to contact support.
  it.each([
    ["a name the helper knows", Object.assign(new Error("no"), { name: "SecurityError" })],
    ["a name the helper passes through as it is", new DOMException("A request is already pending.", "OperationError")],
    ["another name it does not know", new DOMException("The operation failed.", "NotReadableError")],
    ["no credential from the browser (the helper's plain Error)", new Error("Authentication was not completed")],
  ])("says that the device refused, without a retry of its own: %s", async (_label, error) => {
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1"))
    mocks.startAuthentication.mockRejectedValueOnce(error)
    renderProbe()
    await flush()

    fireEvent.click(button())
    await flush()

    expect(screen.getByTestId("passkey-message")).toHaveTextContent(i18n.t("auth.passkey.deviceRefused"))
    expect(screen.queryByRole("button", { name: i18n.t("common.retry") })).not.toBeInTheDocument()
    expect(button()).toBeEnabled()
    // The challenge was not spent: the same options stay in hand.
    expect(optionsRequests()).toBe(1)
  })

  it("says nothing of a first failure: the page stays as it was, and asks again by itself", async () => {
    // A deployment without passkeys that cannot be reached for a moment must
    // not show a passkey control: nothing has said yet that passkeys run here.
    mocks.post.mockResolvedValueOnce(problemAnswer(503)).mockResolvedValue(optionsAnswer("c1"))
    renderProbe()
    await flush()

    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    expect(screen.getByTestId("phase")).toHaveTextContent("checking")

    await flush(60_000 - 1)
    expect(optionsRequests()).toBe(1)
    await flush(1)
    expect(optionsRequests()).toBe(2)
    expect(button()).toBeEnabled()
    expect(screen.queryByTestId("passkey-message")).not.toBeInTheDocument()
  })

  it("says nothing of a network failure before the first answer either", async () => {
    mocks.post.mockRejectedValueOnce(new TypeError("Failed to fetch")).mockResolvedValue(optionsAnswer("c1"))
    renderProbe()
    await flush()

    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    await flush(60_000)
    expect(button()).toBeEnabled()
  })

  it("once passkeys were seen running, says a server fault and offers to try again", async () => {
    mocks.post
      .mockResolvedValueOnce(optionsAnswer("c1"))
      .mockResolvedValueOnce(problemAnswer(503))
      .mockResolvedValue(optionsAnswer("c2"))
    renderProbe()
    await flush()
    expect(button()).toBeEnabled()

    // The renewal a minute before expiry meets the fault.
    await flush(4 * 60_000)
    expect(screen.getByTestId("passkey-message")).toHaveTextContent(i18n.t("errors.feedback.server"))
    expect(button()).toBeDisabled()

    fireEvent.click(screen.getByRole("button", { name: i18n.t("common.retry") }))
    await flush()
    expect(optionsRequests()).toBe(3)
    expect(screen.queryByTestId("passkey-message")).not.toBeInTheDocument()
    expect(button()).toBeEnabled()
  })

  it("clears the fault's sentence when the options answer again by themselves", async () => {
    mocks.post
      .mockResolvedValueOnce(optionsAnswer("c1"))
      .mockResolvedValueOnce(problemAnswer(503))
      .mockResolvedValue(optionsAnswer("c2"))
    renderProbe()
    await flush()
    await flush(4 * 60_000)
    expect(screen.getByTestId("passkey-message")).toBeInTheDocument()

    await flush(60_000)
    expect(optionsRequests()).toBe(3)
    expect(screen.queryByTestId("passkey-message")).not.toBeInTheDocument()
    expect(button()).toBeEnabled()
  })

  it("says a first failure on a page that expects passkeys (the server asked for one)", async () => {
    mocks.post.mockResolvedValueOnce(problemAnswer(503)).mockResolvedValue(optionsAnswer("c1"))
    renderProbe(async () => ({}), false, true)
    await flush()

    expect(screen.getByTestId("passkey-message")).toHaveTextContent(i18n.t("errors.feedback.server"))
    expect(screen.getByRole("button", { name: i18n.t("common.retry") })).toBeEnabled()
  })

  it("asks at once when a door said the account signs in with its passkey, and says its failure", async () => {
    mocks.post
      .mockResolvedValueOnce(problemAnswer(503))
      .mockResolvedValueOnce(problemAnswer(503))
      .mockResolvedValue(optionsAnswer("c1"))
    renderProbe()
    await flush()
    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole("button", { name: "a door asked for the passkey" }))
    await flush()

    expect(optionsRequests()).toBe(2)
    expect(screen.getByTestId("passkey-message")).toHaveTextContent(i18n.t("errors.feedback.server"))
  })

  it("asks nothing more when a door says so while the first answer is on its way, and says that answer's failure", async () => {
    const first = deferred<ReturnType<typeof problemAnswer>>()
    mocks.post.mockReturnValueOnce(first.promise).mockResolvedValue(optionsAnswer("c1"))
    renderProbe()
    await flush()
    expect(optionsRequests()).toBe(1)

    fireEvent.click(screen.getByRole("button", { name: "a door asked for the passkey" }))
    await flush()
    expect(optionsRequests()).toBe(1)

    first.resolve(problemAnswer(503))
    await flush()
    expect(screen.getByTestId("passkey-message")).toHaveTextContent(i18n.t("errors.feedback.server"))
  })

  it("says how long a 429 asked to wait, and asks again only after it", async () => {
    mocks.post
      .mockResolvedValueOnce(optionsAnswer("c1"))
      .mockResolvedValueOnce(problemAnswer(429, "General.RateLimited", { "Retry-After": "30" }))
      .mockResolvedValue(optionsAnswer("c2"))
    renderProbe()
    await flush()
    await flush(4 * 60_000)

    expect(screen.getByTestId("passkey-message")).toHaveTextContent(
      i18n.t("auth.passkey.rateLimitedFor", { seconds: 30 })
    )
    // A wait, not a fault: no retry button to press into the limit.
    expect(screen.queryByRole("button", { name: i18n.t("common.retry") })).not.toBeInTheDocument()

    await flush(30_000 - 1)
    expect(optionsRequests()).toBe(2)
    await flush(1)
    expect(optionsRequests()).toBe(3)
    expect(button()).toBeEnabled()
  })

  it("asks again only after the wait of a 429 before the first answer, and says nothing meanwhile", async () => {
    mocks.post
      .mockResolvedValueOnce(problemAnswer(429, "General.RateLimited", { "Retry-After": "30" }))
      .mockResolvedValue(optionsAnswer("c1"))
    renderProbe()
    await flush()

    expect(screen.queryByTestId("passkey-sign-in")).not.toBeInTheDocument()
    await flush(30_000 - 1)
    expect(optionsRequests()).toBe(1)
    await flush(1)
    expect(optionsRequests()).toBe(2)
    expect(button()).toBeEnabled()
  })

  it("says to wait a moment when a 429 names no time", async () => {
    mocks.post
      .mockResolvedValueOnce(optionsAnswer("c1"))
      .mockResolvedValueOnce(problemAnswer(429))
      .mockResolvedValue(optionsAnswer("c2"))
    renderProbe()
    await flush()
    await flush(4 * 60_000)

    expect(screen.getByTestId("passkey-message")).toHaveTextContent(i18n.t("auth.passkey.rateLimited"))
  })

  it("says the page's answer for a refused sign-in, and keeps the control", async () => {
    mocks.post.mockResolvedValue(optionsAnswer("c1"))
    mocks.startAuthentication.mockResolvedValueOnce(CREDENTIAL)
    renderProbe(async () => ({ message: "Not this time." }))
    await flush()

    fireEvent.click(button())
    await flush()

    expect(screen.getByTestId("passkey-message")).toHaveTextContent("Not this time.")
    expect(button()).toBeEnabled()
  })
})

describe("the address field's suggestion (T-C5)", () => {
  it("runs only where the browser offers it, with the options in hand, and restarts with each renewal", async () => {
    mocks.autofill.mockResolvedValue(true)
    mocks.post.mockResolvedValueOnce(optionsAnswer("c1")).mockResolvedValue(optionsAnswer("c2"))
    renderProbe(async () => ({}), true)
    await flush()

    expect(mocks.startAuthentication).toHaveBeenCalledWith({
      optionsJSON: options("c1"),
      useBrowserAutofill: true,
    })

    await flush(4 * 60_000)
    expect(mocks.startAuthentication).toHaveBeenLastCalledWith({
      optionsJSON: options("c2"),
      useBrowserAutofill: true,
    })
  })

  it("is not started where the browser does not offer it: the button alone", async () => {
    mocks.autofill.mockResolvedValue(false)
    mocks.post.mockResolvedValue(optionsAnswer("c1"))
    renderProbe(async () => ({}), true)
    await flush()

    expect(mocks.startAuthentication).not.toHaveBeenCalled()
    expect(button()).toBeEnabled()
  })

  it("hands a passkey picked from the suggestion to the page, once", async () => {
    mocks.autofill.mockResolvedValue(true)
    mocks.post.mockResolvedValue(optionsAnswer("c1"))
    const picked = deferred<AuthenticationResponseJSON>()
    mocks.startAuthentication.mockReturnValueOnce(picked.promise)
    const onCredential = vi.fn(async () => ({ leaving: true }))
    renderProbe(onCredential, true)
    await flush()

    picked.resolve(CREDENTIAL)
    await flush()

    expect(onCredential).toHaveBeenCalledTimes(1)
    expect(onCredential).toHaveBeenCalledWith(CREDENTIAL)
  })

  it("says nothing when a renewal or the button aborted it", async () => {
    mocks.autofill.mockResolvedValue(true)
    mocks.post.mockResolvedValue(optionsAnswer("c1"))
    mocks.startAuthentication.mockRejectedValueOnce(cancelled("AbortError"))
    renderProbe(async () => ({}), true)
    await flush()

    expect(screen.queryByTestId("passkey-message")).not.toBeInTheDocument()
    expect(mocks.toastError).not.toHaveBeenCalled()
  })
})
