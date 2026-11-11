import { act, fireEvent, render, screen, waitFor } from "@testing-library/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { webcrypto } from "node:crypto"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
  GOOGLE_CLIENT_ID: "",
  APPLE_SERVICES_ID: "",
}))

const { post, loginExternal, toast, providers, fetchProviders } = vi.hoisted(() => ({
  post: vi.fn(),
  loginExternal: vi.fn(),
  toast: { success: vi.fn(), error: vi.fn() },
  providers: { googleEnabled: true, googleExpected: true },
  fetchProviders: vi.fn(),
}))

vi.mock("@authsystem/api/client", () => ({ api: { POST: (...args: unknown[]) => post(...args) } }))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ loginExternal }),
}))

vi.mock("sonner", () => ({ toast }))

vi.mock("./use-external-providers", async (importActual) => {
  const actual = await importActual<typeof import("./use-external-providers")>()
  return {
    ...actual,
    useExternalProviders: () => ({
      ...providers,
      googleClientId: "test-client.apps.googleusercontent.com",
      appleEnabled: false,
      appleServicesId: "",
    }),
    externalProvidersQuery: () => ({ queryKey: ["external-providers"], queryFn: fetchProviders, staleTime: 0 }),
  }
})

const requestExternalNonce = vi.fn<() => Promise<string>>()
vi.mock("./request-nonce", () => ({
  requestExternalNonce: () => requestExternalNonce(),
}))

import { ExternalProviders } from "./external-providers"
import { GOOGLE_CHANNEL } from "./google-code-flow"

/**
 * S02 PR D: the "Continue with Google" button and its code flow, with the popup, the channel,
 * the API and the sign-in replaced at their edges. What each test sees is what a person sees:
 * the button, the popup's address, the toasts, and the calls the page makes.
 *
 * Deliberate breaks, each verified red: the section drawn only on the live answer
 * (googleEnabled); a message accepted whatever its state; a message accepted twice; a
 * postMessage accepted from any origin; access_denied reported; the blocked-popup line removed;
 * the popup left open when Google turns out off; the exchange's verifier taken from a new value;
 * the button disabled while Google's window is open (REVIEW 162 F4).
 */

const LIVE_GOOGLE = [{ code: "google", name: "Google", iconUrl: "", clientId: "test-client.apps.googleusercontent.com" }]

class FakeChannel {
  static instances: FakeChannel[] = []
  onmessage: ((event: MessageEvent) => void) | null = null
  readonly name: string
  constructor(name: string) {
    this.name = name
    FakeChannel.instances.push(this)
  }
  postMessage() {}
  close() {
    FakeChannel.instances = FakeChannel.instances.filter((channel) => channel !== this)
  }
  static emit(data: unknown) {
    for (const channel of FakeChannel.instances.filter((c) => c.name === GOOGLE_CHANNEL)) {
      channel.onmessage?.({ data } as MessageEvent)
    }
  }
}

interface FakePopup {
  closed: boolean
  close: ReturnType<typeof vi.fn>
  focus: ReturnType<typeof vi.fn>
  location: { replace: ReturnType<typeof vi.fn> }
}

let popup: FakePopup
let open: ReturnType<typeof vi.spyOn>

function newPopup(): FakePopup {
  const created: FakePopup = {
    closed: false,
    close: vi.fn(() => {
      created.closed = true
    }),
    focus: vi.fn(),
    location: { replace: vi.fn() },
  }
  return created
}

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/login"]}>
        <ExternalProviders />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

/** Clicks the button and waits until the popup was sent to Google; returns the authorize URL. */
async function clickAndReachGoogle(): Promise<URL> {
  fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }))
  await waitFor(() => expect(popup.location.replace).toHaveBeenCalledTimes(1))
  return new URL(popup.location.replace.mock.calls[0][0] as string)
}

beforeEach(() => {
  vi.stubGlobal("crypto", webcrypto)
  vi.stubGlobal("BroadcastChannel", FakeChannel)
  FakeChannel.instances = []
  providers.googleEnabled = true
  providers.googleExpected = true
  popup = newPopup()
  open = vi.spyOn(window, "open").mockImplementation(() => popup as unknown as Window)
  requestExternalNonce.mockReset().mockResolvedValue("server-nonce")
  fetchProviders.mockReset().mockResolvedValue(LIVE_GOOGLE)
  post.mockReset().mockResolvedValue({ data: { idToken: "the-id-token" }, response: new Response("{}") })
  loginExternal.mockReset().mockResolvedValue({ status: "success" })
  toast.success.mockReset()
  toast.error.mockReset()
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe("the Google button", () => {
  it("shows with the page, from this browser's hint, before the live answer, with the or-divider", () => {
    providers.googleEnabled = false
    providers.googleExpected = true

    renderSection()

    expect(screen.getByRole("button", { name: "Continue with Google" })).toBeVisible()
    expect(screen.getByText("or")).toBeVisible()
    expect(document.querySelector('script[src*="accounts.google.com"]')).toBeNull()
  })

  it("is absent when Google is neither offered nor expected", () => {
    providers.googleEnabled = false
    providers.googleExpected = false

    const view = renderSection()

    expect(view.container).toBeEmptyDOMElement()
  })

  it("opens the popup in the click itself, then sends it to Google with a fresh state, the PKCE challenge and the server nonce", async () => {
    renderSection()

    const url = await clickAndReachGoogle()

    expect(open).toHaveBeenCalledTimes(1)
    expect(open.mock.calls[0][0]).toBe("about:blank")
    // A fresh name per attempt: another page cannot find the popup by a known name.
    expect(open.mock.calls[0][1]).toMatch(/^google-sign-in-[A-Za-z0-9_-]{16}$/)
    expect(open.mock.calls[0][2]).toMatch(/^popup,width=500,height=640,/)
    expect(url.origin + url.pathname).toBe("https://accounts.google.com/o/oauth2/v2/auth")
    expect(url.searchParams.get("client_id")).toBe("test-client.apps.googleusercontent.com")
    expect(url.searchParams.get("redirect_uri")).toBe(`${window.location.origin}/google-callback.html`)
    expect(url.searchParams.get("nonce")).toBe("server-nonce")
    expect(url.searchParams.get("state")).toMatch(/^[A-Za-z0-9_-]{43}$/)
    expect(url.searchParams.get("code_challenge")).toMatch(/^[A-Za-z0-9_-]{43}$/)
    expect(url.searchParams.get("code_challenge_method")).toBe("S256")
    expect(requestExternalNonce).toHaveBeenCalledTimes(1)
  })

  it("exchanges the code with its verifier and signs in with the ID token and the server nonce", async () => {
    renderSection()
    const url = await clickAndReachGoogle()

    await act(async () => {
      FakeChannel.emit({ type: "google-code", state: url.searchParams.get("state"), code: "4/0Ab" })
    })

    await waitFor(() => expect(loginExternal).toHaveBeenCalledWith("google", "the-id-token", "server-nonce", expect.anything()))
    const [path, init] = post.mock.calls[0] as [string, { body: { code: string; codeVerifier: string } }]
    expect(path).toBe("/api/v1/Auth/external/google/code")
    expect(init.body.code).toBe("4/0Ab")
    // The verifier is the one whose S256 challenge went to Google.
    const digest = await webcrypto.subtle.digest("SHA-256", new TextEncoder().encode(init.body.codeVerifier))
    const challenge = btoa(String.fromCharCode(...new Uint8Array(digest))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "")
    expect(challenge).toBe(url.searchParams.get("code_challenge"))
    expect(popup.close).toHaveBeenCalled()
  })

  it("ignores a message for another state, a second message for the same state, and a postMessage from another origin", async () => {
    renderSection()
    const url = await clickAndReachGoogle()
    const state = url.searchParams.get("state")

    await act(async () => {
      FakeChannel.emit({ type: "google-code", state: "someone-elses-state", code: "c0" })
      window.dispatchEvent(new MessageEvent("message", {
        origin: "https://evil.example.net",
        data: { type: "google-code", state, code: "c1" },
      }))
    })
    expect(post).not.toHaveBeenCalled()

    await act(async () => {
      FakeChannel.emit({ type: "google-code", state, code: "c2" })
      FakeChannel.emit({ type: "google-code", state, code: "c3" })
    })
    await waitFor(() => expect(loginExternal).toHaveBeenCalledTimes(1))
    expect(post).toHaveBeenCalledTimes(1)
    expect((post.mock.calls[0][1] as { body: { code: string } }).body.code).toBe("c2")
  })

  it("accepts the opener's postMessage from this origin", async () => {
    renderSection()
    const url = await clickAndReachGoogle()

    await act(async () => {
      window.dispatchEvent(new MessageEvent("message", {
        origin: window.location.origin,
        data: { type: "google-code", state: url.searchParams.get("state"), code: "c-opener" },
      }))
    })

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1))
  })

  it("stays silent when the person backs out at Google (access_denied)", async () => {
    renderSection()
    const url = await clickAndReachGoogle()

    await act(async () => {
      FakeChannel.emit({ type: "google-error", state: url.searchParams.get("state"), error: "access_denied" })
    })

    expect(toast.error).not.toHaveBeenCalled()
    expect(post).not.toHaveBeenCalled()
    expect(screen.getByRole("button", { name: "Continue with Google" })).toBeEnabled()
  })

  it("reports any other Google error with local copy, never Google's text", async () => {
    renderSection()
    const url = await clickAndReachGoogle()

    await act(async () => {
      FakeChannel.emit({ type: "google-error", state: url.searchParams.get("state"), error: "server_error" })
    })

    expect(toast.error).toHaveBeenCalledTimes(1)
    expect(toast.error.mock.calls[0][0]).not.toContain("server_error")
  })

  it("shows the catalog sentence when the exchange fails", async () => {
    const detail = "The sign-in with the provider could not be completed. Please try again."
    post.mockResolvedValue({ error: { status: 400, code: "ExternalAuth.CodeExchangeFailed", detail }, response: new Response("{}", { status: 400 }) })
    renderSection()
    const url = await clickAndReachGoogle()

    await act(async () => {
      FakeChannel.emit({ type: "google-code", state: url.searchParams.get("state"), code: "spent" })
    })

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(detail))
    expect(loginExternal).not.toHaveBeenCalled()
  })

  it("says the browser blocked the window when it did, and sends nothing", () => {
    open.mockImplementation(() => null)
    renderSection()

    fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }))

    expect(screen.getByRole("alert")).toHaveTextContent("Your browser blocked the Google sign-in window")
    // Only the nonce the page asked for on load: the click asked for nothing more.
    expect(requestExternalNonce).toHaveBeenCalledTimes(1)
    expect(fetchProviders).not.toHaveBeenCalled()
  })

  it("closes the popup and stops when the live answer says Google is off", async () => {
    providers.googleEnabled = false
    fetchProviders.mockResolvedValue([])
    renderSection()

    fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }))

    await waitFor(() => expect(popup.close).toHaveBeenCalledTimes(1))
    expect(popup.location.replace).not.toHaveBeenCalled()
  })

  it("ends quietly when the person closes the blank window before it reaches Google", async () => {
    let resolveNonce!: (nonce: string) => void
    requestExternalNonce.mockImplementation(() => new Promise<string>((resolve) => (resolveNonce = resolve)))
    renderSection()

    fireEvent.click(screen.getByRole("button", { name: "Continue with Google" }))
    popup.closed = true
    await act(async () => resolveNonce("server-nonce"))

    await waitFor(() => expect(screen.getByRole("button", { name: "Continue with Google" })).toBeEnabled())
    expect(popup.location.replace).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
  })

  it("lets a second click start a new attempt and abandons the first", async () => {
    renderSection()
    const first = await clickAndReachGoogle()
    // The person closed the first popup: the button is usable again.
    popup.closed = true
    await waitFor(() => expect(screen.getByRole("button", { name: "Continue with Google" })).toBeEnabled(), { timeout: 2000 })

    popup = newPopup()
    const second = await clickAndReachGoogle()
    expect(second.searchParams.get("state")).not.toBe(first.searchParams.get("state"))
    expect(open.mock.calls[1][1]).not.toBe(open.mock.calls[0][1])

    await act(async () => {
      FakeChannel.emit({ type: "google-code", state: first.searchParams.get("state"), code: "old" })
    })
    expect(post).not.toHaveBeenCalled()
    expect(requestExternalNonce).toHaveBeenCalledTimes(2)
  })

  // REVIEW 162 F4: clicking the page sends the popup behind it; the button must bring it back.
  it("brings Google's open window forward on a second click, opens no other, and stays usable", async () => {
    renderSection()
    await clickAndReachGoogle()
    const button = screen.getByRole("button", { name: "Continue with Google" })
    expect(button).toBeEnabled()
    expect(button).toHaveAttribute("aria-busy", "false")

    fireEvent.click(button)

    expect(popup.focus).toHaveBeenCalledTimes(1)
    expect(open).toHaveBeenCalledTimes(1)
    expect(button).toBeEnabled()
  })

  it("starts a new attempt on a second click once the handle reports closed, without waiting for the poll", async () => {
    renderSection()
    const first = await clickAndReachGoogle()
    const firstPopup = popup
    // The person closed it, or Google's page cut its link to this one.
    firstPopup.closed = true
    popup = newPopup()

    const second = await clickAndReachGoogle()

    expect(firstPopup.focus).not.toHaveBeenCalled()
    expect(open).toHaveBeenCalledTimes(2)
    expect(second.searchParams.get("state")).not.toBe(first.searchParams.get("state"))
  })

  it("is disabled and busy only while the code is with the API", async () => {
    let finish!: (value: unknown) => void
    post.mockImplementation(() => new Promise((resolve) => (finish = resolve)))
    renderSection()
    const url = await clickAndReachGoogle()

    await act(async () => {
      FakeChannel.emit({ type: "google-code", state: url.searchParams.get("state"), code: "4/0Ab" })
    })

    const button = screen.getByRole("button", { name: /Continue with Google/ })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute("aria-busy", "true")
    await act(async () => finish({ data: { idToken: "the-id-token" }, response: new Response("{}") }))
    await waitFor(() => expect(screen.getByRole("button", { name: /Continue with Google/ })).toBeEnabled())
  })
})
