import { beforeEach, describe, expect, it, vi } from "vitest"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import {
  MemoryRouter,
  Route,
  Routes,
  useLocation,
  type InitialEntry,
} from "react-router-dom"

vi.mock("@authsystem/api/env", () => ({
  API_BASE_URL: "https://api.example.com",
}))

import {
  useLoginCompletion,
  type LoginCompletion,
} from "./login-completion"
import {
  clearPendingTwoFactorChallenge,
  getPendingReturnTo,
  setPendingReturnTo,
} from "./pending-challenge"

const AUTHORIZE =
  "https://api.example.com/api/v1/auth/authorize?client_id=app&state=xyz"

const assign = vi.fn()

beforeEach(() => {
  assign.mockClear()
  clearPendingTwoFactorChallenge()
  vi.stubGlobal("location", { assign, href: "https://accounts.example.com/login" })
})

/** Reports where the router ended up, and what it carried there. */
function Landing() {
  const location = useLocation()
  return (
    <div data-testid="landing">
      {location.pathname}
      {location.search}
      {"|"}
      {JSON.stringify(location.state ?? null)}
    </div>
  )
}

function renderCompletion(
  entry: InitialEntry,
  act: (completion: LoginCompletion) => void,
  options?: Parameters<typeof useLoginCompletion>[0]
) {
  function Probe() {
    const completion = useLoginCompletion(options)
    return (
      <>
        <button onClick={() => act(completion)}>act</button>
        <span data-testid="returnTo">{completion.returnTo ?? "none"}</span>
        <span data-testid="from">{completion.from}</span>
      </>
    )
  }

  render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/login" element={<Probe />} />
        <Route path="/two-factor" element={<Probe />} />
        <Route path="*" element={<Landing />} />
      </Routes>
    </MemoryRouter>
  )

  return { act: () => userEvent.click(screen.getByText("act")) }
}

function loginEntry(returnTo?: string, state?: unknown): InitialEntry {
  return {
    pathname: "/login",
    search: returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : "",
    state,
  } as InitialEntry
}

describe("complete", () => {
  it("resumes a pending authorize request with a top-level navigation", async () => {
    // Not a router transition: the IdP session cookie is SameSite=Lax and
    // rides along with nothing else, so authorize would not see the session.
    const { act } = renderCompletion(loginEntry(AUTHORIZE), (c) =>
      c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(AUTHORIZE)
  })

  it("lands on the default page when nothing is pending", async () => {
    const { act } = renderCompletion(loginEntry(), (c) =>
      c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(screen.getByTestId("landing")).toHaveTextContent("/|")
  })

  it("returns to the attempted location recorded by the route guard", async () => {
    const { act } = renderCompletion(
      loginEntry(undefined, { from: { pathname: "/sessions", search: "?tab=all" } }),
      (c) => c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(screen.getByTestId("landing")).toHaveTextContent("/sessions?tab=all")
  })

  it("honors a caller's default landing page", () => {
    renderCompletion(loginEntry(), () => {}, { defaultFrom: "/profile" })

    expect(screen.getByTestId("from")).toHaveTextContent("/profile")
  })

  it("carries the pending request through a forced password change", async () => {
    // The screen is an interstitial, not a destination. Dropping returnTo here
    // was one of the seven silent losses this rule exists to prevent.
    const { act } = renderCompletion(loginEntry(AUTHORIZE), (c) =>
      c.complete({ requiresPasswordChange: true })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    const landing = screen.getByTestId("landing")
    expect(landing).toHaveTextContent("/force-password-change")
    expect(landing).toHaveTextContent(AUTHORIZE)
  })
})

describe("challenge", () => {
  it("hands the pending request to the 2FA screen", async () => {
    const { act } = renderCompletion(loginEntry(AUTHORIZE), (c) =>
      c.challenge("challenge-token")
    )

    await act()

    expect(getPendingReturnTo()).toBe(AUTHORIZE)
  })
})

describe("interstitial", () => {
  it("carries the pending request to any screen on the way to a session", async () => {
    const { act } = renderCompletion(loginEntry(AUTHORIZE), (c) =>
      c.interstitial("/verify-email", { email: "user@example.com" })
    )

    await act()

    const landing = screen.getByTestId("landing")
    expect(landing).toHaveTextContent("/verify-email")
    expect(landing).toHaveTextContent("user@example.com")
    expect(landing).toHaveTextContent(AUTHORIZE)
  })
})

describe("where the pending request is read from", () => {
  it("re-validates a value threaded through router state", async () => {
    const { act } = renderCompletion(
      { pathname: "/two-factor", state: { returnTo: AUTHORIZE } } as InitialEntry,
      (c) => c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(AUTHORIZE)
  })

  it("refuses a foreign destination smuggled through router state", async () => {
    // Router state is not attacker-reachable across origins, but it is not the
    // place the rule about legal destinations is allowed to be relaxed.
    const { act } = renderCompletion(
      {
        pathname: "/two-factor",
        state: { returnTo: "https://evil.example.com/steal" },
      } as InitialEntry,
      (c) => c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
  })

  it("ignores the in-memory fallback unless the screen asks for it", () => {
    setPendingReturnTo(AUTHORIZE)

    renderCompletion(loginEntry(), () => {})

    expect(screen.getByTestId("returnTo")).toHaveTextContent("none")
  })

  it("resumes from the in-memory fallback on the screen that may", () => {
    // Only the 2FA screen is reachable after the state carrying both the
    // challenge and its destination was lost.
    setPendingReturnTo(AUTHORIZE)

    renderCompletion({ pathname: "/two-factor" } as InitialEntry, () => {}, {
      resumePending: true,
    })

    expect(screen.getByTestId("returnTo")).toHaveTextContent(AUTHORIZE)
  })

  it("prefers the query string over anything remembered", () => {
    const stale = "https://api.example.com/api/v1/auth/authorize?client_id=stale"
    setPendingReturnTo(stale)

    renderCompletion(loginEntry(AUTHORIZE), () => {}, { resumePending: true })

    expect(screen.getByTestId("returnTo")).toHaveTextContent("client_id=app")
  })
})
