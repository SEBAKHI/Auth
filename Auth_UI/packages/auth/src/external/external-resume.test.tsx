import { beforeEach, describe, expect, it, vi } from "vitest"
import { act, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { webcrypto } from "node:crypto"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"

import "@authsystem/i18n"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

const loginExternal = vi.fn()
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ loginExternal }),
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useTheme: () => ({ resolvedTheme: "light" }),
}))

const LIVE_PROVIDERS = [
  { code: "google", name: "Google", iconUrl: "", clientId: "google-client-id" },
  { code: "apple", name: "Apple", iconUrl: "", clientId: "apple-services-id" },
]

vi.mock("./use-external-providers", async (importActual) => {
  const actual = await importActual<typeof import("./use-external-providers")>()
  return {
    ...actual,
    useExternalProviders: () => ({
      googleEnabled: true,
      googleExpected: true,
      googleClientId: "google-client-id",
      appleEnabled: true,
      appleServicesId: "apple-services-id",
    }),
    externalProvidersQuery: () => ({
      queryKey: ["external-providers"],
      queryFn: async () => LIVE_PROVIDERS,
      staleTime: 0,
    }),
  }
})

vi.mock("./request-nonce", () => ({
  requestExternalNonce: async () => "server-nonce",
}))

// The Google code exchange (S02 PR D); the nonce above, every other call unused here.
vi.mock("@authsystem/api/client", () => ({
  api: {
    POST: async () => ({ data: { idToken: "google-id-token" }, response: new Response("{}") }),
  },
}))

import { AppleSignIn } from "./apple-sign-in"
import { GoogleSignIn } from "./google-sign-in"

const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"

const assign = vi.fn()

/** The channels the page listens on (stubbed before it renders, which is when it opens them). */
const channels: Array<{ name: string; onmessage: ((event: MessageEvent) => void) | null }> = []

/**
 * Apple's button fetches its SDK by appending a <script> and waiting for its
 * load event, which jsdom never fires for a remote src. Resolve it the moment
 * the element is appended so the component proceeds as it would in a browser.
 * (Google's button loads no script since S02 PR D.)
 */
function resolveProviderScripts() {
  const append = HTMLHeadElement.prototype.appendChild
  vi.spyOn(HTMLHeadElement.prototype, "appendChild").mockImplementation(function (
    this: HTMLHeadElement,
    node: Node
  ) {
    const appended = append.call(this, node) as Node
    if (node instanceof HTMLScriptElement) {
      queueMicrotask(() => node.dispatchEvent(new Event("load")))
    }
    return appended
  } as typeof append)
}

function Landing() {
  const location = useLocation()
  return <div data-testid="landing">{location.pathname}</div>
}

function renderOnHostedLogin(button: React.ReactNode, returnTo?: string) {
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    <MemoryRouter
      initialEntries={[
        {
          pathname: "/login",
          search: returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : "",
        },
      ]}
    >
      <Routes>
        <Route path="/login" element={<>{button}</>} />
        <Route path="*" element={<Landing />} />
      </Routes>
    </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.restoreAllMocks()
  assign.mockClear()
  loginExternal.mockReset()
  loginExternal.mockResolvedValue({
    status: "authenticated",
    requiresPasswordChange: false,
  })
  vi.stubGlobal("location", { assign, href: "https://accounts.example.com/login" })
  vi.stubGlobal("crypto", webcrypto)
  channels.length = 0
  vi.stubGlobal(
    "BroadcastChannel",
    class {
      onmessage: ((event: MessageEvent) => void) | null = null
      readonly name: string
      constructor(name: string) {
        this.name = name
        channels.push(this)
      }
      postMessage() {}
      close() {}
    }
  )
  resolveProviderScripts()
})

describe("Google sign-in on the hosted login page", () => {
  /**
   * Signs in as a person does since S02 PR D: a click opens the popup, which goes to Google;
   * the callback page hands the code back on the channel; the API exchanges it for the ID token.
   */
  async function signInWithGoogle() {
    const popup = { closed: false, close: vi.fn(), location: { replace: vi.fn() } }
    vi.spyOn(window, "open").mockImplementation(() => popup as unknown as Window)

    await userEvent.click(await screen.findByRole("button", { name: "Continue with Google" }))
    await waitFor(() => expect(popup.location.replace).toHaveBeenCalled())
    const state = new URL(popup.location.replace.mock.calls[0][0] as string).searchParams.get("state")
    await act(async () => {
      for (const channel of channels) channel.onmessage?.({ data: { type: "google-code", state, code: "c" } } as MessageEvent)
    })
    await waitFor(() => expect(loginExternal).toHaveBeenCalledWith("google", "google-id-token", "server-nonce", expect.anything()))
  }

  it("resumes the pending authorize request", async () => {
    // The defect this pins: the button read only the router's `from`, which is
    // empty on a page reached by a full redirect from the authorize endpoint,
    // so a relying party's request died at the accounts home page.
    renderOnHostedLogin(<GoogleSignIn />, AUTHORIZE)

    await signInWithGoogle()

    await waitFor(() => expect(assign).toHaveBeenCalledWith(AUTHORIZE))
  })

  it("still lands on the default page for a plain sign-in", async () => {
    renderOnHostedLogin(<GoogleSignIn />)

    await signInWithGoogle()

    await waitFor(() => expect(screen.getByTestId("landing")).toBeInTheDocument())
    expect(assign).not.toHaveBeenCalled()
  })

  it("carries the pending request into the two-factor step", async () => {
    loginExternal.mockResolvedValue({
      status: "twoFactorRequired",
      challengeToken: "challenge",
    })
    renderOnHostedLogin(<GoogleSignIn />, AUTHORIZE)

    await signInWithGoogle()

    const { getPendingReturnTo } = await import("../pending-challenge")
    await waitFor(() => expect(getPendingReturnTo()).toBe(AUTHORIZE))
  })
})

describe("Apple sign-in on the hosted login page", () => {
  async function signInWithApple() {
    vi.stubGlobal("AppleID", {
      auth: {
        init: () => {},
        signIn: async () => ({
          authorization: { id_token: "apple-id-token", code: "apple-code" },
        }),
      },
    })

    await userEvent.click(await screen.findByRole("button"))
    await waitFor(() => expect(loginExternal).toHaveBeenCalled())
  }

  it("resumes the pending authorize request", async () => {
    renderOnHostedLogin(<AppleSignIn />, AUTHORIZE)

    await signInWithApple()

    await waitFor(() => expect(assign).toHaveBeenCalledWith(AUTHORIZE))
  })
})
