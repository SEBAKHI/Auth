import type * as React from "react"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { useStaySignedIn } from "../stay-signed-in"

/**
 * OI-154: the sign-in page's "Stay signed in" box. Unticked on every visit, never
 * kept in the browser, handed to the password sign-in and, at the moment they
 * sign in, to the provider buttons under the form.
 */
const mocks = vi.hoisted(() => ({
  login: vi.fn(),
  complete: vi.fn(),
  challenge: vi.fn(),
  /** What the branding payload says (OI-165); the box is offered unless a test says otherwise. */
  branding: { staySignedInOffered: true },
}))

vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({ name: "AuthSystem", logoUrl: null, isPending: false, ...mocks.branding }),
}))

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ login: mocks.login }),
}))

vi.mock("../login-completion", () => ({
  useLoginCompletion: () => ({
    returnTo: null,
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

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

vi.mock("sonner", () => ({
  toast: { error: vi.fn(), success: vi.fn() },
}))

vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({ title, children }: { title: string; children: React.ReactNode }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
}))

const { LoginPage } = await import("./login")

/** A provider button, as the real ones read the box: when it signs in. */
function ProbeButton({ onSignIn }: { onSignIn: (staySignedIn: boolean) => void }) {
  const readStaySignedIn = useStaySignedIn()
  return (
    <button type="button" onClick={() => onSignIn(readStaySignedIn())}>
      provider
    </button>
  )
}

function renderPage(providers?: React.ReactNode) {
  return render(
    <MemoryRouter initialEntries={["/login"]}>
      <LoginPage providers={providers} />
    </MemoryRouter>
  )
}

async function signIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("auth.email"), "jane@one.example")
  await user.type(screen.getByLabelText("auth.password"), "pw")
  await user.click(screen.getByRole("button", { name: "auth.signIn" }))
}

describe("LoginPage: Stay signed in", () => {
  beforeEach(() => {
    mocks.login.mockReset()
    mocks.login.mockResolvedValue({ status: "authenticated", requiresPasswordChange: false })
    mocks.branding.staySignedInOffered = true
    window.localStorage.clear()
    window.sessionStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("is unticked on the first render, with its label and description", () => {
    renderPage()

    const box = screen.getByRole("checkbox", { name: "auth.staySignedIn" })
    expect(box).not.toBeChecked()
    expect(box).toHaveAccessibleDescription("auth.staySignedInHint")
  })

  it("signs in for a browser session unless ticked", async () => {
    const user = userEvent.setup()
    renderPage()

    await signIn(user)

    expect(mocks.login).toHaveBeenCalledWith("jane@one.example", "pw", false)
  })

  it("signs in to stay signed in once ticked", async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    await signIn(user)

    expect(mocks.login).toHaveBeenCalledWith("jane@one.example", "pw", true)
  })

  it("hands the provider buttons the box as it is when they sign in", async () => {
    const user = userEvent.setup()
    const seen: boolean[] = []
    renderPage(<ProbeButton onSignIn={(value) => seen.push(value)} />)

    await user.click(screen.getByRole("button", { name: "provider" }))
    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    await user.click(screen.getByRole("button", { name: "provider" }))
    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    await user.click(screen.getByRole("button", { name: "provider" }))

    expect(seen).toEqual([false, true, false])
  })

  it("keeps the choice nowhere: ticking writes nothing to localStorage or sessionStorage", async () => {
    const setItem = vi.spyOn(Storage.prototype, "setItem")
    const localSet = vi.spyOn(window.localStorage, "setItem")
    const sessionSet = vi.spyOn(window.sessionStorage, "setItem")
    const user = userEvent.setup()
    renderPage()

    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))

    expect(setItem).not.toHaveBeenCalled()
    expect(localSet).not.toHaveBeenCalled()
    expect(sessionSet).not.toHaveBeenCalled()
  })

  it("starts unticked again on the next visit", async () => {
    const user = userEvent.setup()
    const first = renderPage()
    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))
    expect(screen.getByRole("checkbox", { name: "auth.staySignedIn" })).toBeChecked()
    first.unmount()

    renderPage()

    expect(screen.getByRole("checkbox", { name: "auth.staySignedIn" })).not.toBeChecked()
  })
})

/**
 * OI-165: when the deployment's administrator decides for everyone, the branding
 * payload says so and the page shows no box. It then sends false; the server
 * applies the administrator's choice whatever the page sends.
 */
describe("LoginPage: Stay signed in, when the administrator decides", () => {
  beforeEach(() => {
    mocks.login.mockReset()
    mocks.login.mockResolvedValue({ status: "authenticated", requiresPasswordChange: false })
    mocks.branding.staySignedInOffered = false
  })

  it("shows no box", () => {
    renderPage()

    expect(screen.getByRole("button", { name: "auth.signIn" })).toBeInTheDocument()
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument()
    expect(screen.queryByText("auth.staySignedIn")).not.toBeInTheDocument()
  })

  it("signs in with staySignedIn false", async () => {
    const user = userEvent.setup()
    renderPage()

    await signIn(user)

    expect(mocks.login).toHaveBeenCalledWith("jane@one.example", "pw", false)
  })

  it("hands the provider buttons false", async () => {
    const user = userEvent.setup()
    const seen: boolean[] = []
    renderPage(<ProbeButton onSignIn={(value) => seen.push(value)} />)

    await user.click(screen.getByRole("button", { name: "provider" }))

    expect(seen).toEqual([false])
  })

  it("sends false after a tick made before the box went away", async () => {
    // The cached branding offered the box; the refetch says the administrator
    // decides. The tick the person made meanwhile is not sent.
    const user = userEvent.setup()
    const seen: boolean[] = []
    mocks.branding.staySignedInOffered = true
    const page = renderPage(<ProbeButton onSignIn={(value) => seen.push(value)} />)
    await user.click(screen.getByRole("checkbox", { name: "auth.staySignedIn" }))

    mocks.branding.staySignedInOffered = false
    page.rerender(
      <MemoryRouter initialEntries={["/login"]}>
        <LoginPage providers={<ProbeButton onSignIn={(value) => seen.push(value)} />} />
      </MemoryRouter>
    )
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "provider" }))
    await signIn(user)

    expect(seen).toEqual([false])
    expect(mocks.login).toHaveBeenCalledWith("jane@one.example", "pw", false)
  })
})
