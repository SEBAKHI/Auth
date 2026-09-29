import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * Cross-tab behaviour of a cookie session (S01). The session key holds the
 * sentinel, so every tab sees the SAME value before and after a rotation: the
 * listener must read a write as "rotated", only a removal as "signed out", and
 * the channel must carry the access token and nothing else.
 *
 * Like client.test.ts, every "tab" is a fresh module instance loaded after
 * resetModules(), sharing one localStorage and one channel bus — and there are
 * no static imports of the token stack, which would be a third instance.
 */

vi.mock("@authsystem/i18n", () => ({
  default: { language: "en", t: (key: string) => key },
}))

const REFRESH_KEY = "auth.refreshToken"
const SPENDING_KEY = "auth.refreshSpending"
const SENTINEL = "__cookie__"

let storage: Map<string, string>
let posted: unknown[]

function installLocalStorage(): Map<string, string> {
  const store = new Map<string, string>()
  const mock: Storage = {
    getItem: (key) => store.get(key) ?? null,
    setItem: (key, value) => void store.set(key, String(value)),
    removeItem: (key) => void store.delete(key),
    clear: () => store.clear(),
    key: (index) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size
    },
  }
  vi.stubGlobal("localStorage", mock)
  Object.defineProperty(window, "localStorage", { value: mock, configurable: true, writable: true })
  return store
}

/** A FIFO lock manager: a stub that granted at once would prove nothing. */
function installWebLocks(): void {
  const tails = new Map<string, Promise<unknown>>()
  Object.defineProperty(navigator, "locks", {
    configurable: true,
    writable: true,
    value: {
      request: (name: string, a: unknown, b?: unknown) => {
        const callback = (typeof a === "function" ? a : b) as () => Promise<unknown>
        const run = (tails.get(name) ?? Promise.resolve()).then(() => callback())
        tails.set(name, run.catch(() => undefined))
        return run
      },
    },
  })
}

/** Delivers to every other channel of the name, never to the sender. */
function installBroadcastChannel(): void {
  const open = new Set<Channel>()
  class Channel {
    onmessage: ((event: MessageEvent) => void) | null = null
    readonly name: string
    constructor(name: string) {
      this.name = name
      open.add(this)
    }
    postMessage(data: unknown) {
      posted.push(data)
      for (const peer of open) {
        if (peer !== this && peer.name === this.name) peer.onmessage?.({ data } as MessageEvent)
      }
    }
    close() {
      open.delete(this)
    }
  }
  vi.stubGlobal("BroadcastChannel", Channel)
}

function accessToken(sequence: number): string {
  const payload = btoa(JSON.stringify({ jti: `a${sequence}`, exp: Math.floor(Date.now() / 1000) + 900 }))
  return `h.${payload.replace(/=+$/, "")}.s`
}

type Tab = {
  client: typeof import("@authsystem/api/client")
  tokenStore: typeof import("@authsystem/api/token-store")
  tabSync: typeof import("@authsystem/api/tab-sync")
}
const tabs: Tab[] = []

async function openTab(): Promise<Tab> {
  vi.resetModules()
  const tokenStore = await import("@authsystem/api/token-store")
  const tabSync = await import("@authsystem/api/tab-sync")
  const client = await import("@authsystem/api/client")
  const tab = { client, tokenStore, tabSync }
  tabs.push(tab)
  // startTabSync reconciles under the lock; let that settle.
  await new Promise((resolve) => setTimeout(resolve, 0))
  return tab
}

function storageEvent(key: string, newValue: string | null): StorageEvent {
  const event = new Event("storage") as StorageEvent
  Object.assign(event, { key, newValue, storageArea: window.localStorage })
  return event
}

let refreshes = 0
function installCookieRefresh(): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => {
      refreshes += 1
      return new Response(JSON.stringify({ accessToken: accessToken(refreshes), refreshToken: SENTINEL }), {
        status: 200,
        headers: { "content-type": "application/json" },
      })
    })
  )
}

beforeEach(() => {
  storage = installLocalStorage()
  posted = []
  refreshes = 0
  installWebLocks()
  installBroadcastChannel()
})

afterEach(() => {
  for (const tab of tabs.splice(0)) tab.tabSync.stopTabSync()
  vi.unstubAllGlobals()
  vi.resetModules()
})

describe("a cookie session across tabs", () => {
  it("writing the sentinel in one tab does not end the session in another", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    const other = await openTab()
    other.tokenStore.setAccessToken(accessToken(0))
    const expired = vi.fn()
    window.addEventListener(other.tabSync.SESSION_EXPIRED_EVENT, expired)

    window.dispatchEvent(storageEvent(REFRESH_KEY, SENTINEL))

    window.removeEventListener(other.tabSync.SESSION_EXPIRED_EVENT, expired)
    expect(expired).not.toHaveBeenCalled()
    expect(other.tokenStore.getAccessToken()).toBe(accessToken(0))
  })

  it("removing the key ends the session in the other tab", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    const other = await openTab()
    const expired = vi.fn()
    window.addEventListener(other.tabSync.SESSION_EXPIRED_EVENT, expired)

    window.dispatchEvent(storageEvent(REFRESH_KEY, null))

    window.removeEventListener(other.tabSync.SESSION_EXPIRED_EVENT, expired)
    expect(expired).toHaveBeenCalled()
  })

  it("a new tab asks the others for an access token when the key holds the sentinel", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    await openTab()

    expect(posted).toContainEqual({ kind: "hello" })
  })

  it("a new tab without a session asks nobody", async () => {
    await openTab()

    expect(posted).not.toContainEqual({ kind: "hello" })
  })

  it("puts nothing but access tokens on the channel", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    installCookieRefresh()
    const tabA = await openTab()
    await openTab()

    await tabA.client.sharedRefresh()

    const carried = posted.filter((message) => (message as { kind: string }).kind === "access")
    expect(carried.length).toBeGreaterThan(0)
    for (const message of posted) {
      expect(Object.keys(message as object).sort()).toEqual(
        (message as { kind: string }).kind === "access" ? ["kind", "token"] : ["kind"]
      )
      expect(JSON.stringify(message)).not.toContain(SENTINEL)
    }
  })
})

describe("a cookie refresh whose context died mid-flight", () => {
  it("resumes when the marker is young: the next refresh goes ahead", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    storage.set(SPENDING_KEY, String(Date.now() - 4_000))
    installCookieRefresh()

    const tab = await openTab()

    expect(storage.get(REFRESH_KEY)).toBe(SENTINEL)
    expect(storage.has(SPENDING_KEY)).toBe(false)
    expect(await tab.client.sharedRefresh()).toBe(true)
    expect(refreshes).toBe(1)
  })

  it("ends the session locally when the marker is older than the resume window", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    storage.set(SPENDING_KEY, String(Date.now() - 11_000))
    installCookieRefresh()

    const tab = await openTab()

    expect(storage.has(REFRESH_KEY)).toBe(false)
    expect(storage.has(SPENDING_KEY)).toBe(false)
    expect(await tab.client.sharedRefresh()).toBe(false)
    expect(refreshes).toBe(0)
  })

  it("waits for a live spender instead of reading its marker as abandoned", async () => {
    storage.set(REFRESH_KEY, SENTINEL)
    let release: (response: Response) => void = () => undefined
    vi.stubGlobal(
      "fetch",
      vi.fn(
        () =>
          new Promise<Response>((resolve) => {
            release = resolve
          })
      )
    )
    const spender = await openTab()
    const inFlight = spender.client.sharedRefresh()
    await vi.waitFor(() => expect(storage.has(SPENDING_KEY)).toBe(true))
    // Make the live marker look ancient: only the lock can save the session now.
    storage.set(SPENDING_KEY, String(Date.now() - 60_000))

    const booting = openTab()
    await new Promise((resolve) => setTimeout(resolve, 10))
    expect(storage.get(REFRESH_KEY)).toBe(SENTINEL)

    release(
      new Response(JSON.stringify({ accessToken: accessToken(1), refreshToken: SENTINEL }), {
        status: 200,
        headers: { "content-type": "application/json" },
      })
    )
    expect(await inFlight).toBe(true)
    await booting
    expect(storage.get(REFRESH_KEY)).toBe(SENTINEL)
  })
})
