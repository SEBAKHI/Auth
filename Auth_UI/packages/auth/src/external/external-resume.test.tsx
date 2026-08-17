import { beforeEach, describe, expect, it, vi } from "vitest"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
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

vi.mock("./use-external-providers", () => ({
  useExternalProviders: () => ({
    googleEnabled: true,
    googleClientId: "google-client-id",
    appleEnabled: true,
    appleServicesId: "apple-services-id",
  }),
}))

import { AppleSignIn } from "./apple-sign-in"
import { GoogleSignIn } from "./google-sign-in"

const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"

const assign = vi.fn()

/**
 * Both providers fetch their SDK by appending a <script> and waiting for its
 * load event, which jsdom never fires for a remote src. Resolve it the moment
 * the element is appended so the component proceeds as it would in a browser.
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
  resolveProviderScripts()
})

describe("Google sign-in on the hosted login page", () => {
  /** Signs in through the credential callback Google would invoke. */
  async function signInWithGoogle() {
    let callback: ((r: { credential: string }) => void) | undefined
    vi.stubGlobal("google", {
      accounts: {
        id: {
          initialize: (config: { callback: (r: { credential: string }) => void }) => {
            callback = config.callback
          },
          renderButton: () => {},
        },
      },
    })

    await waitFor(() => expect(callback).toBeDefined())
    callback!({ credential: "google-id-token" })
    await waitFor(() => expect(loginExternal).toHaveBeenCalled())
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
