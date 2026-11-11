import { afterEach, describe, expect, it, vi } from "vitest"

import { SUPPORTED_LANGUAGES } from "@authsystem/i18n/languages"

import { CLOSE_THIS_WINDOW, runGoogleCallback, STILL_OPEN_AFTER_MS } from "./google-callback-page"
import { GOOGLE_CHANNEL } from "./google-code-flow"

/**
 * S02 PR D: /google-callback.html's whole job, in order: take the code out of the address, hand
 * it to the sign-in page on the same-origin channel and to the opener, close the window.
 *
 * Deliberate breaks, each verified red: replaceState moved after the posts; the opener posted
 * to "*"; the channel's post removed; close() removed; the still-open line shown even when the
 * window closed; the line's direction fixed to ltr.
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

const timers: Array<{ run: () => void; ms: number }> = []

/** A window whose close() takes effect only when `closes` says so (a browser may refuse it). */
function fakeWindow(
  search: string,
  opener: { postMessage: ReturnType<typeof vi.fn> } | null,
  options: { closes?: boolean; language?: string } = {}
) {
  const win = {
    closed: false,
    location: { search, pathname: "/google-callback.html", origin: "https://accounts.example.com" },
    history: { replaceState: vi.fn(() => steps.push("replaceState")) },
    navigator: { language: options.language ?? "en-US" },
    document: { documentElement: { lang: "en", dir: "" }, body: { textContent: "" } },
    opener,
    close: vi.fn(() => {
      steps.push("close")
      if (options.closes ?? true) win.closed = true
    }),
    setTimeout: (run: () => void, ms: number) => timers.push({ run, ms }),
  }
  return win as unknown as Window & {
    history: { replaceState: ReturnType<typeof vi.fn> }
    close: ReturnType<typeof vi.fn>
    document: { documentElement: { lang: string; dir: string }; body: { textContent: string } }
  }
}

afterEach(() => {
  posted.length = 0
  steps.length = 0
  timers.length = 0
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

  it("says the window can be closed, in the browser's language, when the browser kept it open", () => {
    vi.stubGlobal("BroadcastChannel", FakeChannel)
    const win = fakeWindow("?state=s&code=c", null, { closes: false, language: "ar-SY" })

    runGoogleCallback(win)
    expect(timers).toHaveLength(1)
    expect(timers[0].ms).toBe(STILL_OPEN_AFTER_MS)
    expect(win.document.body.textContent).toBe("")
    timers[0].run()

    expect(win.document.body.textContent).toBe(CLOSE_THIS_WINDOW.ar)
    expect(win.document.documentElement).toEqual({ lang: "ar", dir: "rtl" })
  })

  it("shows nothing once the window closed, and falls back to English for another language", () => {
    vi.stubGlobal("BroadcastChannel", FakeChannel)
    const closed = fakeWindow("?state=s&code=c", null)
    runGoogleCallback(closed)
    timers.splice(0).forEach((timer) => timer.run())
    expect(closed.document.body.textContent).toBe("")

    const kept = fakeWindow("?state=s&code=c", null, { closes: false, language: "de-DE" })
    runGoogleCallback(kept)
    timers.splice(0).forEach((timer) => timer.run())
    expect(kept.document.body.textContent).toBe(CLOSE_THIS_WINDOW.en)
    expect(kept.document.documentElement.dir).toBe("ltr")
  })

  it("has the line in every language the apps ship", () => {
    expect(Object.keys(CLOSE_THIS_WINDOW).sort()).toEqual(SUPPORTED_LANGUAGES.map((language) => language.code).sort())
    for (const text of Object.values(CLOSE_THIS_WINDOW)) expect(text.trim().length).toBeGreaterThan(0)
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
