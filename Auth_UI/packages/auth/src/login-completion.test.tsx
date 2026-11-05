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
  getPendingSignInReason,
  setPendingReturnTo,
} from "./pending-challenge"
import { setAccessToken } from "@authsystem/api/token-store"
import { requestKey, SIGN_IN_MARKER_KEY } from "./sign-in-marker"

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
        <span data-testid="reason">{JSON.stringify(completion.reason)}</span>
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

  it("hands the methods the server offered to the 2FA screen with the token (S24)", async () => {
    // Its own router: the shared one renders the probe again at /two-factor, and
    // what is checked here is the state the 2FA screen receives.
    function Challenge() {
      const completion = useLoginCompletion()
      return (
        <button onClick={() => completion.challenge("challenge-token", ["email", "recovery"])}>
          act
        </button>
      )
    }
    render(
      <MemoryRouter initialEntries={[loginEntry(AUTHORIZE)]}>
        <Routes>
          <Route path="/login" element={<Challenge />} />
          <Route path="/two-factor" element={<Landing />} />
        </Routes>
      </MemoryRouter>
    )

    await userEvent.click(screen.getByText("act"))

    const landing = screen.getByTestId("landing")
    expect(landing).toHaveTextContent("/two-factor")
    expect(landing).toHaveTextContent('"methods":["email","recovery"]')
    expect(landing).toHaveTextContent('"challengeToken":"challenge-token"')
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

describe("complete, with an accepted invitation's launch address (OI-192)", () => {
  const LAUNCH = "https://crm.example.com/signin/start?iss=https%3A%2F%2Fid.example.com"
  const ORGANIZATION = "/organizations/33333333-3333-3333-3333-333333333333"

  beforeEach(() => setAccessToken(null))

  it("enters the application when nothing else claims the sign-in", async () => {
    const { act } = renderCompletion(loginEntry(), (c) =>
      c.complete({ requiresPasswordChange: false }, { launchUrl: LAUNCH, from: ORGANIZATION })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(LAUNCH)
    expect(screen.queryByTestId("landing")).toBeNull()
  })

  it("lets a pending authorize request win over the launch", async () => {
    const { act } = renderCompletion(loginEntry(AUTHORIZE), (c) =>
      c.complete({ requiresPasswordChange: false }, { launchUrl: LAUNCH })
    )

    await act()

    expect(assign).toHaveBeenCalledTimes(1)
    expect(assign).toHaveBeenCalledWith(AUTHORIZE)
  })

  it("lets the second-factor requirement win over the launch", async () => {
    setAccessToken(tokenWith({ sub: "u", mfa_req: "enroll" }))
    const { act } = renderCompletion(loginEntry(), (c) =>
      c.complete({ requiresPasswordChange: false }, { launchUrl: LAUNCH, from: ORGANIZATION })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(screen.getByTestId("landing")).toHaveTextContent(
      `/two-factor/required|{"from":"${ORGANIZATION}"}`
    )
  })

  it("lets the forced password change win, carrying the caller's landing page", async () => {
    const { act } = renderCompletion(loginEntry(), (c) =>
      c.complete({ requiresPasswordChange: true }, { launchUrl: LAUNCH, from: ORGANIZATION })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(screen.getByTestId("landing")).toHaveTextContent(
      `/force-password-change|{"from":"${ORGANIZATION}","returnTo":null,"reason":null}`
    )
  })

  it.each([
    "javascript:alert(1)",
    "http://crm.example.com/signin/start",
    "/signin/start",
    "//crm.example.com/signin/start",
    "https://user:pw@crm.example.com/signin/start",
  ])("ignores %s and lands on the caller's page", async (launchUrl) => {
    const { act } = renderCompletion(loginEntry(), (c) =>
      c.complete({ requiresPasswordChange: false }, { launchUrl, from: ORGANIZATION })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(screen.getByTestId("landing")).toHaveTextContent(`${ORGANIZATION}|null`)
  })

  it("lands on the recorded origin when the caller names no page and no launch", async () => {
    const { act } = renderCompletion(
      loginEntry(undefined, { from: { pathname: "/sessions", search: "" } }),
      (c) => c.complete({ requiresPasswordChange: false }, { launchUrl: null })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(screen.getByTestId("landing")).toHaveTextContent("/sessions|null")
  })
})

/** An access token whose payload carries `claims`, as the token store holds one. */
function tokenWith(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "")
  return `header.${payload}.signature`
}

describe("complete, for a platform administrator still owing a second factor (S08)", () => {
  beforeEach(() => setAccessToken(null))

  it("goes to /two-factor/required, carrying where the user was going", async () => {
    setAccessToken(tokenWith({ sub: "u", mfa_req: "step_up" }))
    const { act } = renderCompletion(
      loginEntry(undefined, { from: { pathname: "/users", search: "" } }),
      (c) => c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(screen.getByTestId("landing")).toHaveTextContent(
      '/two-factor/required|{"from":"/users"}'
    )
  })

  it("asks only after a pending authorize request, which the withholding never touches", async () => {
    setAccessToken(tokenWith({ sub: "u", mfa_req: "enroll" }))
    const { act } = renderCompletion(loginEntry(AUTHORIZE), (c) =>
      c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(AUTHORIZE)
  })

  it("asks only after the forced password change", async () => {
    setAccessToken(tokenWith({ sub: "u", mfa_req: "enroll" }))
    const { act } = renderCompletion(loginEntry(), (c) =>
      c.complete({ requiresPasswordChange: true })
    )

    await act()

    expect(screen.getByTestId("landing")).toHaveTextContent("/force-password-change|")
  })

  it("sends a token without the claim where it was going, as before", async () => {
    setAccessToken(tokenWith({ sub: "u", permissions: "users:read" }))
    const { act } = renderCompletion(
      loginEntry(undefined, { from: { pathname: "/users", search: "" } }),
      (c) => c.complete({ requiresPasswordChange: false })
    )

    await act()

    expect(screen.getByTestId("landing")).toHaveTextContent("/users|null")
  })
})

// ── S23: the sign-in reason travels with the pending request ─────────────────

describe("the sign-in reason (S23)", () => {
  const HANDLE = "a".repeat(64)
  const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`
  const STEP_UP = { reason: "mfa_step_up", method: "password" }
  const ENROLL = { reason: "mfa_enroll", method: "code" }

  function reasonEntry(search: string, state?: unknown): InitialEntry {
    return { pathname: "/login", search, state } as InitialEntry
  }

  it("is read from the query beside the request, in both shapes", () => {
    renderCompletion(
      reasonEntry(`?client_id=app&auth_request=${HANDLE}&reason=mfa_step_up&method=password`),
      () => {}
    )
    expect(screen.getByTestId("reason")).toHaveTextContent(JSON.stringify(STEP_UP))
  })

  it("is read from the legacy shape too", () => {
    renderCompletion(
      reasonEntry(`?returnTo=${encodeURIComponent(AUTHORIZE)}&reason=mfa_enroll&method=code`),
      () => {}
    )
    expect(screen.getByTestId("returnTo")).toHaveTextContent(AUTHORIZE)
    expect(screen.getByTestId("reason")).toHaveTextContent(JSON.stringify(ENROLL))
  })

  it("is ignored without a request, and never borrowed from another source", () => {
    // A reason in the query, a request from router state: they were not written
    // together, so the request goes on without it.
    renderCompletion(
      reasonEntry("?reason=mfa_enroll&method=code", { returnTo: AUTHORIZE }),
      () => {}
    )
    expect(screen.getByTestId("returnTo")).toHaveTextContent(AUTHORIZE)
    expect(screen.getByTestId("reason")).toHaveTextContent("null")
  })

  it("is read from router state with the request, validated", () => {
    renderCompletion(
      {
        pathname: "/two-factor",
        state: { returnTo: AUTHORIZE, reason: { reason: "mfa_passkey", method: "code" } },
      } as InitialEntry,
      () => {}
    )
    // mfa_passkey is not a reason this page reads before S20 (T-B3).
    expect(screen.getByTestId("reason")).toHaveTextContent("null")
  })

  it("is read from the in-memory store only where the request may be", () => {
    setPendingReturnTo(AUTHORIZE, { reason: "mfa_step_up", method: "password" })

    renderCompletion({ pathname: "/two-factor" } as InitialEntry, () => {}, {
      resumePending: true,
    })

    expect(screen.getByTestId("reason")).toHaveTextContent(JSON.stringify(STEP_UP))
  })

  it("is handed to the 2FA screen by challenge(), in state and in the store", async () => {
    function Challenge() {
      const completion = useLoginCompletion()
      return <button onClick={() => completion.challenge("challenge-token")}>act</button>
    }
    render(
      <MemoryRouter
        initialEntries={[reasonEntry(`?client_id=app&auth_request=${HANDLE}&reason=mfa_step_up&method=password`)]}
      >
        <Routes>
          <Route path="/login" element={<Challenge />} />
          <Route path="/two-factor" element={<Landing />} />
        </Routes>
      </MemoryRouter>
    )

    await userEvent.click(screen.getByText("act"))

    expect(screen.getByTestId("landing")).toHaveTextContent(`"reason":${JSON.stringify(STEP_UP)}`)
    expect(getPendingReturnTo()).toBe(RESUME)
    expect(getPendingSignInReason()).toEqual(STEP_UP)
  })

  it("rides through a forced password change", async () => {
    const { act } = renderCompletion(
      reasonEntry(`?client_id=app&auth_request=${HANDLE}&reason=mfa_enroll&method=code`),
      (c) => c.complete({ requiresPasswordChange: true })
    )

    await act()

    const landing = screen.getByTestId("landing")
    expect(landing).toHaveTextContent("/force-password-change")
    expect(landing).toHaveTextContent(`"reason":${JSON.stringify(ENROLL)}`)
    expect(assign).not.toHaveBeenCalled()
  })

  it("rides through an interstitial", async () => {
    const { act } = renderCompletion(
      reasonEntry(`?client_id=app&auth_request=${HANDLE}&reason=mfa_step_up&method=password`),
      (c) => c.interstitial("/verify-email", { email: "user@example.com" })
    )

    await act()

    const landing = screen.getByTestId("landing")
    expect(landing).toHaveTextContent("/verify-email")
    expect(landing).toHaveTextContent(`"reason":${JSON.stringify(STEP_UP)}`)
  })

  it.each(["mfa_step_up", "mfa_reauthenticate"])(
    "%s resumes the application at once",
    async (word) => {
      const { act } = renderCompletion(
        reasonEntry(`?client_id=app&auth_request=${HANDLE}&reason=${word}&method=password`),
        (c) => c.complete({ requiresPasswordChange: false })
      )

      await act()

      expect(assign).toHaveBeenCalledTimes(1)
      expect(assign).toHaveBeenCalledWith(RESUME)
    }
  )

  it("mfa_enroll opens the application-mode screen, whose own completion resumes once", async () => {
    setPendingReturnTo(RESUME, { reason: "mfa_enroll", method: "code" })

    // The screen's own completion: the request in router state, the reason gone.
    function Screen() {
      const location = useLocation()
      const completion = useLoginCompletion()
      return (
        <>
          <div data-testid="screen">{JSON.stringify(location.state)}</div>
          <button onClick={() => completion.complete({})}>finish</button>
        </>
      )
    }
    function Login() {
      const completion = useLoginCompletion()
      return <button onClick={() => completion.complete({})}>act</button>
    }
    render(
      <MemoryRouter
        initialEntries={[reasonEntry(`?client_id=app&auth_request=${HANDLE}&reason=mfa_enroll&method=code`)]}
      >
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/two-factor/required" element={<Screen />} />
        </Routes>
      </MemoryRouter>
    )

    await userEvent.click(screen.getByText("act"))

    expect(assign).not.toHaveBeenCalled()
    const state = JSON.parse(screen.getByTestId("screen").textContent ?? "null")
    expect(state).toEqual({ mode: "application", returnTo: RESUME, from: "/" })
    // Spent: a later sign-in in this tab does not open the screen again.
    expect(getPendingReturnTo()).toBeNull()
    expect(getPendingSignInReason()).toBeNull()

    await userEvent.click(screen.getByText("finish"))

    expect(assign).toHaveBeenCalledTimes(1)
    expect(assign).toHaveBeenCalledWith(RESUME)
  })
})

// ── OI-275: the marker a resume leaves for the sign-in page ──────────────────

describe("the sign-in marker (OI-275)", () => {
  const HANDLE = "b".repeat(64)
  const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`
  const PENDING = `?client_id=app&auth_request=${HANDLE}`
  const entry = (search: string) => ({ pathname: "/login", search }) as InitialEntry
  const stored = () => window.sessionStorage.getItem(SIGN_IN_MARKER_KEY)

  beforeEach(() => {
    assign.mockReset()
    window.sessionStorage.clear()
    setAccessToken(tokenWith({ sub: "user-1", sid: "session-1" }))
  })

  it("is written before the resume, with the request, the account, the session, the time and the proof", async () => {
    let atNavigation: string | null = "not navigated"
    assign.mockImplementation(() => {
      atNavigation = stored()
    })
    const before = Date.now()
    const { act } = renderCompletion(entry(PENDING), (c) =>
      c.complete({}, { proof: "password" })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(RESUME)
    const marker = JSON.parse(atNavigation ?? "null") as Record<string, unknown>
    expect(marker).toEqual({
      request: requestKey(RESUME),
      sub: "user-1",
      sid: "session-1",
      at: expect.any(Number),
      proof: "password",
    })
    expect(marker.at as number).toBeGreaterThanOrEqual(before)
    expect(marker.at as number).toBeLessThanOrEqual(Date.now())
  })

  it("records an unknown proof as null when the screen names none", async () => {
    const { act } = renderCompletion(entry(PENDING), (c) => c.complete({}))

    await act()

    expect(JSON.parse(stored() ?? "null")).toMatchObject({ request: requestKey(RESUME), proof: null })
  })

  it("is not written by a resume that ends no sign-in, and an older one is dropped", async () => {
    window.sessionStorage.setItem(SIGN_IN_MARKER_KEY, "left over")
    const { act } = renderCompletion(entry(PENDING), (c) =>
      c.complete({}, { endsSignIn: false })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(RESUME)
    expect(stored()).toBeNull()
  })

  it("is not written from a token without sub or sid, and an older one is dropped", async () => {
    window.sessionStorage.setItem(SIGN_IN_MARKER_KEY, "left over")
    setAccessToken(tokenWith({ sub: "user-1" }))
    const { act } = renderCompletion(entry(PENDING), (c) =>
      c.complete({}, { proof: "password" })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(RESUME)
    expect(stored()).toBeNull()
  })

  it("is not written by a plain sign-in", async () => {
    const plain = renderCompletion(entry(""), (c) => c.complete({}, { proof: "password" }))
    await plain.act()
    expect(stored()).toBeNull()
  })

  it("is not written before a forced password change", async () => {
    const { act } = renderCompletion(entry(PENDING), (c) =>
      c.complete({ requiresPasswordChange: true }, { proof: "password" })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(stored()).toBeNull()
  })

  it("is spent where mfa_enroll opens the set-up page", async () => {
    window.sessionStorage.setItem(
      SIGN_IN_MARKER_KEY,
      JSON.stringify({ request: requestKey(RESUME), sub: "user-1", sid: "session-1", at: Date.now(), proof: "password" })
    )
    const { act } = renderCompletion(entry(`${PENDING}&reason=mfa_enroll&method=password`), (c) =>
      c.complete({}, { proof: "password" })
    )

    await act()

    expect(assign).not.toHaveBeenCalled()
    expect(screen.getByTestId("landing")).toHaveTextContent("/two-factor/required")
    expect(stored()).toBeNull()
  })
})
