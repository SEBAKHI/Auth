import { afterEach, describe, expect, it, vi } from "vitest"

import { runGoogleCallback } from "./google-callback-page"
import { GOOGLE_CHANNEL } from "./google-code-flow"

/**
 * S02 PR D: /google-callback.html's whole job, in order: take the code out of the address, hand
 * it to the sign-in page on the same-origin channel and to the opener, close the window.
 *
 * Deliberate breaks, each verified red: replaceState moved after the posts; the opener posted
 * to "*"; the channel's post removed; close() removed.
 */
const posted: Array<{ channel: string; data: unknown }> = []
const steps: string[] = []

class FakeChannel {
  private readonly name: string
  constructor(name: string) {
    this.name = name
  }
  postMessage(data: unknown) {
    steps.push("channel")
    posted.push({ channel: this.name, data })
  }
  close() {}
}

function fakeWindow(search: string, opener: { postMessage: ReturnType<typeof vi.fn> } | null) {
  return {
    location: { search, pathname: "/google-callback.html", origin: "https://accounts.example.com" },
    history: { replaceState: vi.fn(() => steps.push("replaceState")) },
    opener,
    close: vi.fn(() => steps.push("close")),
  } as unknown as Window & { history: { replaceState: ReturnType<typeof vi.fn> }; close: ReturnType<typeof vi.fn> }
}

afterEach(() => {
  posted.length = 0
  steps.length = 0
  vi.unstubAllGlobals()
})

describe("the Google callback page", () => {
  it("takes the code out of the address first, then hands it over twice, then closes", () => {
    vi.stubGlobal("BroadcastChannel", FakeChannel)
    const opener = { postMessage: vi.fn(() => steps.push("opener")) }
    const win = fakeWindow("?state=s1&code=4%2F0Ab&scope=openid", opener)

    runGoogleCallback(win)

    const message = { type: "google-code", state: "s1", code: "4/0Ab" }
    expect(win.history.replaceState).toHaveBeenCalledWith(null, "", "/google-callback.html")
    expect(posted).toEqual([{ channel: GOOGLE_CHANNEL, data: message }])
    expect(opener.postMessage).toHaveBeenCalledWith(message, "https://accounts.example.com")
    expect(steps).toEqual(["replaceState", "channel", "opener", "close"])
  })

  it("hands over Google's error the same way, and works without an opener", () => {
    vi.stubGlobal("BroadcastChannel", FakeChannel)
    const win = fakeWindow("?error=access_denied&state=s2", null)

    runGoogleCallback(win)

    expect(posted).toEqual([
      { channel: GOOGLE_CHANNEL, data: { type: "google-error", state: "s2", error: "access_denied" } },
    ])
    expect(steps).toEqual(["replaceState", "channel", "close"])
  })

  it("sends nothing for an address without a state, and still clears it and closes", () => {
    vi.stubGlobal("BroadcastChannel", FakeChannel)
    const opener = { postMessage: vi.fn() }
    const win = fakeWindow("?code=stray", opener)

    runGoogleCallback(win)

    expect(posted).toEqual([])
    expect(opener.postMessage).not.toHaveBeenCalled()
    expect(steps).toEqual(["replaceState", "close"])
  })
})
