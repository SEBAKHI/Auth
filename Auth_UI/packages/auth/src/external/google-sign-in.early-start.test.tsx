import { render, waitFor } from "@testing-library/react"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ loginExternal: vi.fn() }),
}))

vi.mock("@authsystem/ui/theme-provider", () => ({
  useTheme: () => ({ resolvedTheme: "light" }),
}))

const providers = { googleEnabled: false, googleExpected: false }
vi.mock("./use-external-providers", () => ({
  useExternalProviders: () => ({
    ...providers,
    googleClientId: "google-client-id",
    appleEnabled: false,
    appleServicesId: "",
  }),
}))

const requestExternalNonce = vi.fn<() => Promise<string>>()
vi.mock("./request-nonce", () => ({
  requestExternalNonce: () => requestExternalNonce(),
}))

import { ExternalProviders } from "./external-providers"
import { GoogleSignIn } from "./google-sign-in"

/**
 * Google's script and the server's nonce start loading while the providers' answer is on
 * its way, when this browser saw Google offered last time. They used to wait for that
 * answer. The button itself still waits for it.
 *
 * Deliberate breaks: start the loads on `googleEnabled` only; draw the button on
 * `googleExpected`; ask for a second nonce when the answer arrives; mount the section
 * only once a provider is usable.
 */

const scripts: string[] = []
const initialize = vi.fn()

function renderButton() {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <GoogleSignIn />
    </MemoryRouter>
  )
}

beforeEach(() => {
  scripts.length = 0
  initialize.mockReset()
  requestExternalNonce.mockReset()
  requestExternalNonce.mockResolvedValue("server-nonce")
  // jsdom never loads a remote script: it "loads" the moment it is appended.
  const append = HTMLHeadElement.prototype.appendChild
  vi.spyOn(HTMLHeadElement.prototype, "appendChild").mockImplementation(function (
    this: HTMLHeadElement,
    node: Node
  ) {
    const appended = append.call(this, node) as Node
    if (node instanceof HTMLScriptElement) {
      scripts.push(node.src)
      queueMicrotask(() => node.dispatchEvent(new Event("load")))
    }
    return appended
  } as typeof append)
  vi.stubGlobal("google", { accounts: { id: { initialize, renderButton: vi.fn() } } })
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe("GoogleSignIn before the providers' answer", () => {
  // First in the file: the loader keeps one script per language for the module's life,
  // so only the first test to load one sees it appended.
  it("loads the script and asks for the nonce early, then draws the button with that nonce", async () => {
    providers.googleEnabled = false
    providers.googleExpected = true
    // A new element each time: the same one would let React skip the render.
    const section = () => (
      <MemoryRouter initialEntries={["/login"]}>
        <ExternalProviders />
      </MemoryRouter>
    )
    const view = render(section())

    await waitFor(() => expect(requestExternalNonce).toHaveBeenCalledTimes(1))
    expect(scripts.some((src) => src.startsWith("https://accounts.google.com/gsi/client"))).toBe(true)
    expect(initialize).not.toHaveBeenCalled()
    // Nothing of the section shows before the answer.
    expect(view.container.firstElementChild).toHaveClass("hidden")
    expect(view.queryByText("or")).toBeNull()

    providers.googleEnabled = true
    view.rerender(section())

    await waitFor(() => expect(initialize).toHaveBeenCalledTimes(1))
    expect(initialize.mock.calls[0][0]).toMatchObject({ client_id: "google-client-id", nonce: "server-nonce" })
    // The same button: the early nonce was its own, not asked for again.
    expect(requestExternalNonce).toHaveBeenCalledTimes(1)
    expect(view.container.firstElementChild).not.toHaveClass("hidden")
  })

  it("starts nothing before the answer when this browser never saw Google offered", async () => {
    providers.googleEnabled = false
    providers.googleExpected = false
    renderButton()

    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(requestExternalNonce).not.toHaveBeenCalled()
    expect(initialize).not.toHaveBeenCalled()
  })

  it("draws nothing when the answer says Google is off, whatever loaded early", async () => {
    providers.googleEnabled = false
    providers.googleExpected = true
    const view = renderButton()

    await waitFor(() => expect(requestExternalNonce).toHaveBeenCalledTimes(1))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(initialize).not.toHaveBeenCalled()
    expect(view.container).toBeEmptyDOMElement()
  })
})
