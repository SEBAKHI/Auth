import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import {
  MemoryRouter,
  Route,
  Routes,
  useLocation,
  type InitialEntry,
} from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * OI-280 PR B (T-B9, V-B7): the completion rule opens the offer to add a passkey
 * after a sign-in, whatever its destination (Q1 (a)), only when the sign-in ended
 * here and not with a passkey, nothing else claims it first, the deployment's
 * hint says passkeys may run, no passkey request on this page answered 404, the
 * browser has WebAuthn and nobody asked not to be asked on this device. Every way
 * out of the offer comes back through the same rule, with the sign-in's proof.
 * The rule's older cases live in login-completion.test.tsx, unchanged.
 *
 * Deliberate break, verified red: the passkey sign-in's own word ignored (an offer
 * after a passkey sign-in).
 */

const mocks = vi.hoisted(() => ({
  signInOptions: { passkeySignInOffered: true } as { passkeySignInOffered?: boolean } | null,
  answeredOff: false,
}))

vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/ui/branding", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@authsystem/ui/branding")>()),
  useSignInOptions: () => mocks.signInOptions,
}))
vi.mock("./passkey/passkey-offer", async (importOriginal) => ({
  ...(await importOriginal<typeof import("./passkey/passkey-offer")>()),
  passkeysAnsweredOff: () => mocks.answeredOff,
}))

import { setAccessToken } from "@authsystem/api/token-store"

import { useLoginCompletion, type LoginCompletion } from "./login-completion"
import {
  PASSKEY_OFFER_OFF_KEY,
  PASSKEY_OFFER_PATH,
  clearPasskeyOfferNavigation,
  pendingPasskeyOfferNavigation,
} from "./passkey/passkey-offer"
import { clearPendingTwoFactorChallenge } from "./pending-challenge"
import { requestKey, SIGN_IN_MARKER_KEY } from "./sign-in-marker"

const HANDLE = "c".repeat(64)
const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${HANDLE}`
const PENDING = `?client_id=app&auth_request=${HANDLE}`
const LAUNCH = "https://crm.example.com/start"

const assign = vi.fn()

/** An access token whose payload carries `claims`, as the token store holds one. */
function tokenWith(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "")
  return `header.${payload}.signature`
}

/** Where the router ended up, and what it carried there. */
function Landing() {
  const location = useLocation()
  return (
    <div data-testid="landing">
      {location.pathname}
      {"|"}
      {JSON.stringify(location.state ?? null)}
    </div>
  )
}

/** The rule run from a page at `probeAt`; any other path is a landing. */
function renderCompletion(
  entry: InitialEntry,
  act: (completion: LoginCompletion) => void,
  probeAt = "/login"
) {
  function Probe() {
    const completion = useLoginCompletion()
    return <button onClick={() => act(completion)}>act</button>
  }

  render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path={probeAt} element={<Probe />} />
        <Route path="*" element={<Landing />} />
      </Routes>
    </MemoryRouter>
  )

  return { act: () => userEvent.click(screen.getByText("act")) }
}

function landing(): { path: string; state: Record<string, unknown> | null } {
  const [path, state] = (screen.getByTestId("landing").textContent ?? "").split("|")
  return { path, state: JSON.parse(state) as Record<string, unknown> | null }
}

const marker = () => window.sessionStorage.getItem(SIGN_IN_MARKER_KEY)

beforeEach(() => {
  assign.mockReset()
  vi.stubGlobal("location", { assign, href: "https://accounts.example.com/login" })
  vi.stubGlobal("PublicKeyCredential", function PublicKeyCredential() {})
  mocks.signInOptions = { passkeySignInOffered: true }
  mocks.answeredOff = false
  window.localStorage.removeItem(PASSKEY_OFFER_OFF_KEY)
  window.sessionStorage.clear()
  clearPendingTwoFactorChallenge()
  clearPasskeyOfferNavigation()
  setAccessToken(tokenWith({ sub: "user-1", sid: "session-1" }))
})

afterEach(() => {
  vi.unstubAllGlobals()
  setAccessToken(null)
})

describe("the offer to add a passkey after a sign-in (T-B9)", () => {
  it("opens after a sign-in to the console, before the landing, carrying where it goes and how it was proved", async () => {
    const { act } = renderCompletion({ pathname: "/login", state: { from: "/users" } }, (c) =>
      c.complete({}, { proof: "password" })
    )

    await act()

    expect(landing()).toEqual({
      path: PASSKEY_OFFER_PATH,
      state: {
        from: "/users",
        returnTo: null,
        reason: null,
        passkeyOffer: { proof: "password", launchUrl: null },
      },
    })
    // And the note the sign-in pages' guard follows while the offer's lazy route loads.
    expect(pendingPasskeyOfferNavigation()).toEqual({ state: landing().state })
  })

  it("opens before an application's request resumes: nothing resumes, and no marker is written yet", async () => {
    const { act } = renderCompletion({ pathname: "/login", search: PENDING }, (c) =>
      c.complete({}, { proof: "code" })
    )

    await act()

    expect(landing().path).toBe(PASSKEY_OFFER_PATH)
    expect(landing().state).toMatchObject({
      returnTo: RESUME,
      passkeyOffer: { proof: "code", launchUrl: null },
    })
    expect(assign).not.toHaveBeenCalled()
    expect(marker()).toBeNull()
  })

  it("carries an invitation's application through it", async () => {
    const { act } = renderCompletion({ pathname: "/login" }, (c) =>
      c.complete({}, { proof: "code", launchUrl: LAUNCH })
    )

    await act()

    expect(landing().state).toMatchObject({ passkeyOffer: { proof: "code", launchUrl: LAUNCH } })
  })

  it("its way out resumes the request once, with the marker of the sign-in's own proof, and opens no second offer", async () => {
    const { act } = renderCompletion(
      {
        pathname: PASSKEY_OFFER_PATH,
        state: { returnTo: RESUME, passkeyOffer: { proof: "code", launchUrl: null } },
      },
      (c) => c.complete({}, { proof: "code" }),
      PASSKEY_OFFER_PATH
    )

    await act()

    expect(assign).toHaveBeenCalledTimes(1)
    expect(assign).toHaveBeenCalledWith(RESUME)
    expect(JSON.parse(marker() ?? "null")).toMatchObject({
      request: requestKey(RESUME),
      sub: "user-1",
      sid: "session-1",
      proof: "code",
    })
  })

  it.each([
    ["after a passkey sign-in", { signedInWithPasskey: true }, () => {}],
    ["while the deployment's word says passkeys do not run", {}, () => {
      mocks.signInOptions = { passkeySignInOffered: false }
    }],
    ["outside any word from the deployment", {}, () => {
      mocks.signInOptions = null
    }],
    ["once a passkey request on this page answered 404", {}, () => {
      mocks.answeredOff = true
    }],
    ["in a browser without WebAuthn", {}, () => {
      vi.stubGlobal("PublicKeyCredential", undefined)
    }],
    ["after «Don't ask on this device»", {}, () => {
      window.localStorage.setItem(PASSKEY_OFFER_OFF_KEY, "1")
    }],
  ])("never opens %s: the sign-in lands where it was going", async (_label, options, given) => {
    given()
    const { act } = renderCompletion({ pathname: "/login", state: { from: "/users" } }, (c) =>
      c.complete({}, { proof: "password", ...options })
    )

    await act()

    expect(landing().path).toBe("/users")
  })

  it("never opens for a completion that ends no sign-in: the request resumes, as before", async () => {
    const { act } = renderCompletion({ pathname: "/login", search: PENDING }, (c) =>
      c.complete({}, { endsSignIn: false })
    )

    await act()

    expect(assign).toHaveBeenCalledWith(RESUME)
    expect(marker()).toBeNull()
  })

  it("never opens from the set-up page: the set-up comes first, and no offer follows it", async () => {
    const { act } = renderCompletion(
      { pathname: "/two-factor/required", state: { from: "/users" } },
      (c) => c.complete({}),
      "/two-factor/required"
    )

    await act()

    expect(landing().path).toBe("/users")
  })

  it("never opens before a forced password change, which comes first", async () => {
    const { act } = renderCompletion({ pathname: "/login" }, (c) =>
      c.complete({ requiresPasswordChange: true }, { proof: "password" })
    )

    await act()

    expect(landing().path).toBe("/force-password-change")
  })

  it("never opens before an application's set-up (mfa_enroll), which comes first", async () => {
    const { act } = renderCompletion(
      {
        pathname: "/login",
        state: { returnTo: RESUME, reason: { reason: "mfa_enroll", method: null } },
      },
      (c) => c.complete({}, { proof: "password" })
    )

    await act()

    expect(landing().path).toBe("/two-factor/required")
    expect(assign).not.toHaveBeenCalled()
  })

  it("never opens before the platform's two-step requirement, which comes first", async () => {
    setAccessToken(tokenWith({ sub: "user-1", sid: "session-1", mfa_req: "enroll" }))
    const { act } = renderCompletion({ pathname: "/login", state: { from: "/users" } }, (c) =>
      c.complete({}, { proof: "password" })
    )

    await act()

    expect(landing().path).toBe("/two-factor/required")
  })
})

/**
 * REVIEW (OI-280 PR B), findings 1 and 3. The offer stands between a sign-in and where it goes and
 * is never where one goes: a session that ended on it (signed out from its window, or expired
 * there) records it as the origin, and its way out would lead back to it, where nothing leaves
 * again. And the rule keeps the identity it had before the offer, while still deciding with the
 * latest branding answer: pages hold it in effects, and the Google button starts over with a new
 * nonce whenever it changes. Deliberate breaks, verified red: the offer path taken as a
 * destination; the hint back in the rule's dependencies.
 */
describe("the offer is never a destination, and the rule's identity holds (REVIEW)", () => {
  it("a sign-in whose recorded origin is the offer goes, after the offer, to the landing", async () => {
    const { act } = renderCompletion(
      { pathname: "/login", state: { from: { pathname: PASSKEY_OFFER_PATH } } },
      (c) => c.complete({}, { proof: "code" })
    )

    await act()

    expect(landing()).toEqual({
      path: PASSKEY_OFFER_PATH,
      state: {
        from: "/",
        returnTo: null,
        reason: null,
        passkeyOffer: { proof: "code", launchUrl: null },
      },
    })
  })

  it("the offer's way out never leads back to the offer", async () => {
    const { act } = renderCompletion(
      {
        pathname: PASSKEY_OFFER_PATH,
        state: { from: PASSKEY_OFFER_PATH, passkeyOffer: { proof: "code", launchUrl: null } },
      },
      (c) => c.complete({}, { proof: "code" }),
      PASSKEY_OFFER_PATH
    )

    await act()

    expect(landing().path).toBe("/")
  })

  it("keeps its identity when the branding answers, and decides with the latest answer", async () => {
    mocks.signInOptions = null
    const seen: LoginCompletion["complete"][] = []
    function Probe() {
      const completion = useLoginCompletion()
      seen.push(completion.complete)
      return <button onClick={() => completion.complete({}, { proof: "code" })}>act</button>
    }
    const tree = () => (
      <MemoryRouter initialEntries={["/login"]}>
        <Routes>
          <Route path="/login" element={<Probe />} />
          <Route path="*" element={<Landing />} />
        </Routes>
      </MemoryRouter>
    )
    const view = render(tree())

    mocks.signInOptions = { passkeySignInOffered: true }
    view.rerender(tree())

    expect(seen.length).toBeGreaterThan(1)
    expect(new Set(seen).size).toBe(1)
    await userEvent.click(screen.getByText("act"))
    expect(landing().path).toBe(PASSKEY_OFFER_PATH)
  })
})
