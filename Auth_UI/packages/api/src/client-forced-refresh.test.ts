import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * S12: a token the server refuses before it expires — its signing key was
 * withdrawn, its session ended — is refreshed once, instead of every request
 * failing until it expires.
 *
 * Until S12 the refresh asked "is the token fresh?" before refreshing, and a
 * refused token whose expiry was still ahead said yes: the 401 path, the
 * upload and step-up all came back with the same refused token.
 *
 * Like client.test.ts, each "tab" is its own load of the token stack, sharing
 * one localStorage, one lock manager, one BroadcastChannel and one server.
 */

vi.mock("@authsystem/i18n", () => ({
  default: { language: "en", t: (key: string) => key },
}))

vi.mock("@authsystem/api/image-downscale", () => ({
  prepareImageForUpload: async (file: File) => file,
}))

const REFRESH_KEY = "auth.refreshToken"
const FORCED_KEY = "auth.forcedRefresh"
const SENTINEL = "__cookie__"

let storage: Map<string, string>
let messages: unknown[]

// ---------------------------------------------------------------- environment

function installLocalStorage(): Map<string, string> {
  const store = new Map<string, string>()
  const mock: Storage = {
    getItem: (key) => store.get(key) ?? null,
    setItem: (key, value) => {
      store.set(key, String(value))
    },
    removeItem: (key) => {
      store.delete(key)
    },
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

/** A real FIFO lock manager: a stub that grants at once would make the tab assertions vacuous. */
function installWebLocks(): void {
  const tails = new Map<string, Promise<unknown>>()
  const manager = {
    request: (name: string, a: unknown, b?: unknown) => {
      const callback = (typeof a === "function" ? a : b) as () => Promise<unknown>
      const previous = tails.get(name) ?? Promise.resolve()
      const run = previous.then(
        () => callback(),
        () => callback()
      )
      tails.set(
        name,
        run.then(
          () => undefined,
          () => undefined
        )
      )
      return run
    },
  }
  Object.defineProperty(navigator, "locks", { value: manager, configurable: true, writable: true })
}

/** Delivers to every other open channel, never to the sender; records every message. */
function installBroadcastChannel(): void {
  const open = new Set<FakeBroadcastChannel>()

  class FakeBroadcastChannel {
    name: string
    onmessage: ((event: MessageEvent) => void) | null = null
    private closed = false

    constructor(name: string) {
      this.name = name
      open.add(this)
    }

    postMessage(data: unknown): void {
      messages.push(data)
      for (const peer of open) {
        if (peer === this || peer.closed || peer.name !== this.name) continue
        peer.onmessage?.({ data } as MessageEvent)
      }
    }

    close(): void {
      this.closed = true
      open.delete(this)
    }
  }

  vi.stubGlobal("BroadcastChannel", FakeBroadcastChannel)
}

// --------------------------------------------------------------- fake backend

/** An hour from now, fixed for the run: the same sequence must give the same token on every call. */
const EXPIRES_AT = Math.floor(Date.now() / 1000) + 3600

/** An access token an hour from expiry: never "expired" by the client's own reading. */
function accessToken(sequence: number): string {
  const payload = { sub: "65c4e1e6-0000-0000-0000-000000000000", jti: `access-${sequence}`, exp: EXPIRES_AT }
  const encoded = btoa(JSON.stringify(payload)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "")
  return `header.${encoded}.signature`
}

function json(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", ...headers },
  })
}

interface Server {
  /** Unexpired access tokens the API refuses, as after a key change. */
  refused: Set<string>
  /** Answers for the next refreshes, in order, instead of a new pair. */
  refreshAnswers: (() => Response)[]
  /** Held until released: the refresh stays in flight. */
  refreshGate: Promise<void> | null
  refreshes: () => number
  /** The bearer each API request carried, in order. */
  presented: (string | null)[]
}

/** A cookie-mode server: each refresh mints access-1, access-2, …; the API refuses what it is told to. */
function installServer(): Server {
  let issued = 0
  let refreshes = 0
  const server: Server = {
    refused: new Set(),
    refreshAnswers: [],
    refreshGate: null,
    refreshes: () => refreshes,
    presented: [],
  }

  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: unknown, init?: RequestInit) => {
      const request = input instanceof Request ? input : null
      const url = request ? request.url : String(input)
      const headers = request ? request.headers : new Headers((init?.headers as HeadersInit | undefined) ?? {})

      if (url.includes("/Auth/refresh")) {
        refreshes += 1
        if (server.refreshGate) await server.refreshGate
        const scripted = server.refreshAnswers.shift()
        if (scripted) return scripted()
        issued += 1
        return json(200, { accessToken: accessToken(issued), refreshToken: SENTINEL })
      }

      const bearer = headers.get("Authorization")?.replace(/^Bearer /, "") ?? null
      server.presented.push(bearer)
      if (!bearer || server.refused.has(bearer)) return json(401, { code: "Http.Unauthenticated" })
      if (url.includes("/Images")) return json(200, { key: "k", url: "https://cdn.example.com/k" })
      return json(200, { id: "65c4e1e6" })
    })
  )

  return server
}

// ------------------------------------------------------------------ tab setup

type Tab = {
  client: typeof import("@authsystem/api/client")
  tokenStore: typeof import("@authsystem/api/token-store")
  tabSync: typeof import("@authsystem/api/tab-sync")
  upload: typeof import("@authsystem/api/upload")
  expired: () => number
}

const openTabs: Tab[] = []
const listeners: (() => void)[] = []

async function openTab(holding: string | null = accessToken(0)): Promise<Tab> {
  vi.resetModules()
  const tokenStore = await import("@authsystem/api/token-store")
  const tabSync = await import("@authsystem/api/tab-sync")
  const client = await import("@authsystem/api/client")
  const upload = await import("@authsystem/api/upload")
  if (holding) tokenStore.setAccessToken(holding)

  let expired = 0
  const onExpired = () => {
    expired += 1
  }
  window.addEventListener(client.SESSION_EXPIRED_EVENT, onExpired)
  listeners.push(() => window.removeEventListener(client.SESSION_EXPIRED_EVENT, onExpired))

  const tab = { client, tokenStore, tabSync, upload, expired: () => expired }
  openTabs.push(tab)
  return tab
}

const me = (tab: Tab) => tab.client.api.GET("/api/v1/Auth/me")

beforeEach(() => {
  storage = installLocalStorage()
  storage.set(REFRESH_KEY, SENTINEL)
  messages = []
  installWebLocks()
  installBroadcastChannel()
})

afterEach(() => {
  for (const tab of openTabs.splice(0)) tab.tabSync.stopTabSync()
  for (const remove of listeners.splice(0)) remove()
  vi.unstubAllGlobals()
  vi.resetModules()
})

// ----------------------------------------------------------------------- specs

describe("a 401 on a token that has not expired", () => {
  it("forces one /refresh, and the next request carries the new token", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))

    const { response } = await me(tab)

    expect(response.status).toBe(401)
    expect(server.refreshes()).toBe(1)
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(1))

    const retried = await me(tab)
    expect(retried.response.status).toBe(200)
    expect(server.presented).toEqual([accessToken(0), accessToken(1)])
    expect(tab.expired()).toBe(0)
  })

  it("forces nothing more when the token a forced refresh produced is refused too", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))
    server.refused.add(accessToken(1))

    await me(tab)
    await me(tab)
    await me(tab)

    expect(server.refreshes()).toBe(1)
    expect(tab.tokenStore.hasSession()).toBe(true)
    expect(tab.expired()).toBe(0)
  })

  it("forces again once a request with the produced token has succeeded", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))

    await me(tab)
    expect(storage.get(FORCED_KEY)).toBe("jti:access-1")

    expect((await me(tab)).response.status).toBe(200)
    expect(storage.has(FORCED_KEY)).toBe(false)

    server.refused.add(accessToken(1))
    await me(tab)
    expect(server.refreshes()).toBe(2)
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(2))
  })

  it("ends the session on a final code from /refresh", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))
    server.refreshAnswers.push(() => json(404, { code: "Auth.RefreshTokenNotFound" }))

    await me(tab)

    expect(tab.tokenStore.hasSession()).toBe(false)
    expect(tab.expired()).toBe(1)
  })

  it("keeps the session and the token it had when /refresh answers 500", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))
    server.refreshAnswers.push(() => json(500, { code: "Http.InternalError" }))

    await me(tab)

    expect(storage.get(REFRESH_KEY)).toBe(SENTINEL)
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(0))
    expect(tab.expired()).toBe(0)
  })
})

describe("a 429 on a forced refresh", () => {
  it("is deferred with the server's wait, and clears or ends nothing", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refreshAnswers.push(() => json(429, { code: "Http.RateLimited" }, { "Retry-After": "7" }))

    const result = await tab.client.sharedRefresh({ rejected: accessToken(0) })

    expect(result).toEqual({ status: "deferred", retryAfterSeconds: 7 })
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(0))
    expect(storage.get(REFRESH_KEY)).toBe(SENTINEL)
    expect(tab.expired()).toBe(0)
  })

  it("leaves the session as it is when it answers a 401", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))
    server.refreshAnswers.push(() => json(429, { code: "Http.RateLimited" }))

    await me(tab)

    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(0))
    expect(storage.get(REFRESH_KEY)).toBe(SENTINEL)
    expect(tab.expired()).toBe(0)
  })
})

describe("across tabs", () => {
  it("costs one /refresh when two tabs are refused on the same token", async () => {
    const server = installServer()
    const tabA = await openTab()
    const tabB = await openTab()
    server.refused.add(accessToken(0))

    await Promise.all([me(tabA), me(tabB)])

    expect(server.refreshes()).toBe(1)
    expect(tabA.tokenStore.getAccessToken()).toBe(accessToken(1))
    expect(tabB.tokenStore.getAccessToken()).toBe(accessToken(1))
  })

  it("adopts a replacement another tab obtained, never the refused value", async () => {
    const server = installServer()
    const tabA = await openTab()
    const tabB = await openTab()
    server.refused.add(accessToken(0))

    await me(tabA)
    const result = await tabB.client.sharedRefresh({ rejected: accessToken(0) })

    expect(result).toEqual({ status: "fresh" })
    expect(server.refreshes()).toBe(1)
    expect(tabB.tokenStore.getAccessToken()).toBe(accessToken(1))
  })

  it("says forced in the access message and in the answer to hello", async () => {
    const server = installServer()
    const tabA = await openTab()
    server.refused.add(accessToken(0))

    await me(tabA)
    expect(messages).toContainEqual({ kind: "access", token: accessToken(1), forced: true })

    messages = []
    const tabC = await openTab(null)
    tabC.tabSync.startTabSync()

    expect(messages).toContainEqual({ kind: "access", token: accessToken(1), forced: true })
    expect(tabC.tokenStore.getAccessToken()).toBe(accessToken(1))
  })

  it("keeps the guard in a tab that adopted the token without storage", async () => {
    const server = installServer()
    const tabA = await openTab()
    const tabB = await openTab()
    server.refused.add(accessToken(0))
    await me(tabA)

    // Storage gone (a locked-down private window): only the message said it was forced.
    vi.stubGlobal("localStorage", {
      getItem: () => {
        throw new Error("SecurityError")
      },
      setItem: () => {
        throw new Error("SecurityError")
      },
      removeItem: () => {
        throw new Error("SecurityError")
      },
    })
    Object.defineProperty(window, "localStorage", { value: globalThis.localStorage, configurable: true, writable: true })

    expect(tabB.tokenStore.isForcedRefresh(accessToken(1))).toBe(true)
  })
})

describe("refreshSessionNow (S08), a caller of the same primitive", () => {
  it("refuses the token it holds without ever dropping it meanwhile", async () => {
    const server = installServer()
    const tab = await openTab()
    let release!: () => void
    server.refreshGate = new Promise<void>((resolve) => {
      release = resolve
    })

    const pending = tab.client.refreshSessionNow()
    await vi.waitFor(() => expect(server.refreshes()).toBe(1))
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(0))
    release()

    expect(await pending).toBe(true)
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(1))
  })

  it("keeps the token and the session on a 429", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refreshAnswers.push(() => json(429, { code: "Http.RateLimited" }))

    expect(await tab.client.refreshSessionNow()).toBe(false)
    expect(tab.tokenStore.getAccessToken()).toBe(accessToken(0))
    expect(tab.expired()).toBe(0)
  })
})

describe("the upload", () => {
  const file = () => new File(["x"], "a.png", { type: "image/png" })

  it("forces one /refresh when its unexpired token is refused, and retries with the new one", async () => {
    const server = installServer()
    const tab = await openTab()
    server.refused.add(accessToken(0))

    await expect(tab.upload.uploadImage(file())).resolves.toEqual({ key: "k", url: "https://cdn.example.com/k" })

    expect(server.refreshes()).toBe(1)
    expect(server.presented).toEqual([accessToken(0), accessToken(1)])
  })
})
