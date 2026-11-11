import { render, screen } from "@testing-library/react"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * OI-280 PR B: the sign-in pages' guard and the offer to add a passkey. The guard sends a signed-in
 * visitor away from the sign-in page as soon as the session flips, and so races the page's own
 * completion; the offer's route is lazy, its navigation still loading when the guard renders, and
 * the guard's navigation replaced it, so a sign-in without an application's request never saw the
 * offer (found by the pictures' run, V-B15, on the built app). The guard now follows the completion
 * there while its note is fresh. Deliberate break, verified red: the guard's follow removed (the
 * first case lands on the home page; the harness's plain sign-in leg never sees the offer).
 */

const HANDLE = "0123456789abcdef".repeat(4)

const mocks = vi.hoisted(() => ({ status: "authenticated" as string }))

vi.mock("@authsystem/api/env", () => ({ API_BASE_URL: "https://api.example.com" }))
vi.mock("./auth-context", () => ({ useAuth: () => ({ status: mocks.status }) }))

import {
  PASSKEY_OFFER_PATH,
  clearPasskeyOfferNavigation,
  notePasskeyOfferNavigation,
} from "./passkey/passkey-offer"
import { RequireAnonymous } from "./require-auth"

function Where({ name }: { name: string }) {
  const location = useLocation()
  return (
    <div data-testid="where">
      {JSON.stringify({ name, path: location.pathname, state: location.state ?? null })}
    </div>
  )
}

function renderAt(entry: string) {
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route element={<RequireAnonymous />}>
          <Route path="/login" element={<Where name="login" />} />
        </Route>
        <Route path="/" element={<Where name="home" />} />
        <Route path={PASSKEY_OFFER_PATH} element={<Where name="offer" />} />
      </Routes>
    </MemoryRouter>
  )
}

async function landed() {
  return JSON.parse((await screen.findByTestId("where")).textContent ?? "null") as {
    name: string
    path: string
    state: unknown
  }
}

beforeEach(() => {
  mocks.status = "authenticated"
  clearPasskeyOfferNavigation()
})

afterEach(() => {
  clearPasskeyOfferNavigation()
  vi.useRealTimers()
})

describe("the sign-in pages' guard and the offer to add a passkey (OI-280 PR B)", () => {
  it("follows a completion on its way to the offer, with the state the completion gave it", async () => {
    const state = {
      from: "/",
      returnTo: null,
      reason: null,
      passkeyOffer: { proof: "code", launchUrl: null },
    }
    notePasskeyOfferNavigation(state)

    renderAt("/login")

    expect(await landed()).toEqual({ name: "offer", path: PASSKEY_OFFER_PATH, state })
  })

  it("sends a signed-in visitor on as before when no completion is on its way", async () => {
    renderAt("/login")

    expect(await landed()).toEqual({ name: "home", path: "/", state: null })
  })

  it("forgets a note past its lifetime: a navigation that never arrived steers no later visit", async () => {
    vi.useFakeTimers({ toFake: ["Date"] })
    vi.setSystemTime(new Date("2026-11-11T09:00:00Z"))
    notePasskeyOfferNavigation({ from: "/" })
    vi.setSystemTime(new Date("2026-11-11T09:00:11Z"))

    renderAt("/login")

    expect(await landed()).toEqual({ name: "home", path: "/", state: null })
  })

  it("forgets a note the offer's page cleared", async () => {
    notePasskeyOfferNavigation({ from: "/" })
    clearPasskeyOfferNavigation()

    renderAt("/login")

    expect(await landed()).toEqual({ name: "home", path: "/", state: null })
  })

  it("shows the page while an application's request is pending, note or not, as before", async () => {
    notePasskeyOfferNavigation({ from: "/" })

    renderAt(`/login?client_id=app&auth_request=${HANDLE}`)

    expect(await landed()).toEqual(expect.objectContaining({ name: "login", path: "/login" }))
  })
})
