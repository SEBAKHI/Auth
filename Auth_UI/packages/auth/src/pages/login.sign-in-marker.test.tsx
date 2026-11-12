import * as React from "react"
import { act, render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import {
  MemoryRouter,
  Route,
  Routes,
  useLocation,
  useNavigate,
} from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

/**
 * OI-275 on the sign-in page: one sign-in, not two, before the set-up page of an
 * application's two-step requirement. The server sent this tab back with
 * `mfa_enroll` for the request a sign-in here resumed moments ago: with a
 * matching marker the page goes straight to the set-up page through the real
 * completion rule, and never renders its form. Any condition false is the form.
 */

const HANDLE = "0123456789abcdef".repeat(4)
const PENDING = `client_id=app&auth_request=${HANDLE}`
const RESUME = `https://api.example.com/api/v1/auth/authorize/resume?${PENDING}`
const OTHER_RESUME = `https://api.example.com/api/v1/auth/authorize/resume?client_id=app&auth_request=${"f".repeat(64)}`
const MARKER_KEY = "auth.signIn.marker"

const mocks = vi.hoisted(() => ({
  status: "authenticated" as string,
  formRender: vi.fn(),
}))

// S13: the bot check is turnstile-widget.test.tsx's own. Here it is the shipped
// default, Off: no field, and every request goes out without a token.
vi.mock("@authsystem/auth/bot-defense/bot-check", () => ({
  useBotCheck: () => ({ field: null, prepare: async () => ({ proceed: true }), spent: () => {} }),
}))
vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("@authsystem/ui/branding", () => ({
  useBranding: () => ({
    name: "AuthSystem",
    logoUrl: null,
    isPending: false,
    staySignedInOffered: false,
    signInOptions: null,
  }),
  // The completion rule reads the deployment's passkey hint too (OI-280 PR B): none here.
  useSignInOptions: () => null,
}))
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ login: vi.fn(), status: mocks.status }),
}))
vi.mock("../flow-branding", () => ({
  useFlowBranding: () => ({
    appBranding: null,
    layout: { appName: null, appLogoUrl: null, appLogoUrlDark: null, securedBy: null },
  }),
}))
vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: "en" } }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }))
vi.mock("@authsystem/ui/auth-layout", () => ({
  AuthLayout: ({ title, children }: { title: string; children: React.ReactNode }) => (
    <div>
      <h1>{title}</h1>
      {children}
    </div>
  ),
}))
vi.mock("./email-code-flow", () => ({ EmailCodeFlow: () => <p>code flow</p> }))
// Counts every render of the form, committed or not: "never rendered" is a
// count of zero, not an absence after the fact.
vi.mock("@authsystem/ui/form", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@authsystem/ui/form")>()
  const Counted = (props: React.ComponentProps<typeof actual.Form>) => {
    mocks.formRender()
    return <actual.Form {...props} />
  }
  return { ...actual, Form: Counted }
})

import { setAccessToken } from "@authsystem/api/token-store"

import { toAccountsSearch } from "../return-to"
import { requestKey } from "../sign-in-marker"
import { LoginPage } from "./login"

function tokenWith(claims: Record<string, unknown>): string {
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "")
  return `header.${payload}.signature`
}

interface Marker {
  request: string
  sub: string
  sid: string
  at: number
  proof: string | null
}

function marker(overrides: Partial<Marker> = {}): Marker {
  return {
    request: requestKey(RESUME),
    sub: "user-1",
    sid: "session-1",
    at: Date.now(),
    proof: "password",
    ...overrides,
  }
}

function storeMarker(value: Marker) {
  window.sessionStorage.setItem(MARKER_KEY, JSON.stringify(value))
}

/**
 * The set-up page as far as this test needs it: where the router landed, what
 * it carried, and the page's "sign in again" exactly as two-factor-required.tsx
 * builds it (the same request, `mfa_enroll`, no method word).
 */
function SetUpProbe() {
  const location = useLocation()
  const navigate = useNavigate()
  const state = location.state as { returnTo?: string } | null
  return (
    <>
      <div data-testid="set-up">{JSON.stringify(location.state ?? null)}</div>
      <button
        onClick={() =>
          navigate(
            {
              pathname: "/login",
              search: toAccountsSearch(state?.returnTo, { reason: "mfa_enroll", method: null }),
            },
            { replace: true }
          )
        }
      >
        sign in again
      </button>
    </>
  )
}

function renderAt(search: string, options: { strict?: boolean } = {}) {
  const tree = (
    <MemoryRouter initialEntries={[`/login?${search}`]}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/two-factor/required" element={<SetUpProbe />} />
      </Routes>
    </MemoryRouter>
  )
  return render(options.strict ? <React.StrictMode>{tree}</React.StrictMode> : tree)
}

const enroll = (method = "password") => `${PENDING}&reason=mfa_enroll&method=${method}`

async function expectSetUp(options: { spent?: boolean } = {}) {
  const landed = await screen.findByTestId("set-up")
  expect(JSON.parse(landed.textContent ?? "null")).toEqual({
    mode: "application",
    returnTo: RESUME,
    from: "/",
  })
  expect(mocks.formRender).not.toHaveBeenCalled()
  // Spent by the completion the skip went through.
  if (options.spent !== false) expect(window.sessionStorage.getItem(MARKER_KEY)).toBeNull()
}

async function expectForm() {
  expect(await screen.findByRole("button", { name: "auth.signIn" })).toBeInTheDocument()
  expect(screen.queryByTestId("set-up")).toBeNull()
  expect(mocks.formRender).toHaveBeenCalled()
}

beforeEach(() => {
  window.sessionStorage.clear()
  mocks.status = "authenticated"
  mocks.formRender.mockClear()
  setAccessToken(tokenWith({ sub: "user-1", sid: "session-1" }))
})

describe("the skip (U2)", () => {
  it("goes straight to the set-up page after a password sign-in, without rendering the form", async () => {
    storeMarker(marker())
    renderAt(enroll())
    await expectSetUp()
  })

  it("goes straight there after an external identity", async () => {
    storeMarker(marker({ proof: "external" }))
    renderAt(enroll())
    await expectSetUp()
  })

  it("goes straight there for an account without a password, whatever the screen said", async () => {
    storeMarker(marker({ proof: null }))
    renderAt(enroll("code"))
    await expectSetUp()
  })

  it("hands over once under StrictMode's double effects", async () => {
    // A tab storage that records its removals: each hand-over spends the
    // marker once in the completion rule, so one removal is one hand-over,
    // whatever StrictMode ran twice.
    const items = new Map<string, string>()
    const removals: string[] = []
    const recording = {
      getItem: (key: string) => items.get(key) ?? null,
      setItem: (key: string, value: string) => void items.set(key, String(value)),
      removeItem: (key: string) => {
        removals.push(key)
        items.delete(key)
      },
      clear: () => items.clear(),
      key: (index: number) => [...items.keys()][index] ?? null,
      get length() {
        return items.size
      },
    }
    const own = Object.getOwnPropertyDescriptor(window, "sessionStorage")
    Object.defineProperty(window, "sessionStorage", { configurable: true, get: () => recording })
    try {
      storeMarker(marker())
      renderAt(enroll(), { strict: true })
      await expectSetUp()
      expect(removals.filter((key) => key === MARKER_KEY)).toHaveLength(1)
    } finally {
      if (own) Object.defineProperty(window, "sessionStorage", own)
    }
  })
})

describe("the form, as before (U2)", () => {
  const cases: Array<[string, () => void, string?]> = [
    ["no marker", () => {}],
    ["a marker for another request", () => storeMarker(marker({ request: requestKey(OTHER_RESUME) }))],
    ["another account", () => storeMarker(marker({ sub: "user-2" }))],
    ["another accounts session", () => storeMarker(marker({ sid: "session-2" }))],
    ["a marker older than two minutes", () => storeMarker(marker({ at: Date.now() - 121_000 }))],
    ["a marker from the future", () => storeMarker(marker({ at: Date.now() + 5_000 }))],
    ["a garbled marker", () => window.sessionStorage.setItem(MARKER_KEY, "{not json")],
    [
      "the reason mfa_step_up",
      () => storeMarker(marker()),
      `${PENDING}&reason=mfa_step_up&method=password`,
    ],
    ["no reason at all", () => storeMarker(marker()), PENDING],
    [
      "a password account whose sign-in was an emailed code alone",
      () => storeMarker(marker({ proof: "code" })),
    ],
    [
      "a password account whose sign-in the screen did not name",
      () => storeMarker(marker({ proof: null })),
    ],
    [
      "no method word and a sign-in the screen did not name",
      () => storeMarker(marker({ proof: null })),
      `${PENDING}&reason=mfa_enroll`,
    ],
    [
      "a token without a session id",
      () => {
        storeMarker(marker())
        setAccessToken(tokenWith({ sub: "user-1" }))
      },
    ],
    [
      "a signed-out tab",
      () => {
        storeMarker(marker())
        mocks.status = "unauthenticated"
      },
    ],
  ]

  for (const [name, arrange, search] of cases) {
    it(`shows the form with ${name}`, async () => {
      arrange()
      renderAt(search ?? enroll())
      await expectForm()
    })
  }

  it("shows the form when storage throws", async () => {
    storeMarker(marker())
    const own = Object.getOwnPropertyDescriptor(window, "sessionStorage")
    Object.defineProperty(window, "sessionStorage", {
      configurable: true,
      get() {
        throw new DOMException("blocked", "SecurityError")
      },
    })
    try {
      renderAt(enroll())
      await expectForm()
    } finally {
      if (own) Object.defineProperty(window, "sessionStorage", own)
    }
  })
})

describe("one shot (U3)", () => {
  it("a set-up that asks to sign in again finds the form, never the set-up page again", async () => {
    storeMarker(marker())
    renderAt(enroll())
    // Whether it was spent is the skip tests' to say; this one asks what the
    // next visit does.
    await expectSetUp({ spent: false })

    await userEvent.click(screen.getByRole("button", { name: "sign in again" }))

    await expectForm()
  })

  it("reads the marker without spending it when it shows the form", async () => {
    storeMarker(marker({ proof: "code" }))
    renderAt(enroll())
    await expectForm()
    await act(async () => {})
    expect(window.sessionStorage.getItem(MARKER_KEY)).not.toBeNull()
  })
})
