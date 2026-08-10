import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * Multi-tab refresh coordination.
 *
 * The original defect survived because the only concurrency guard was a
 * module-scoped promise, which de-duplicates one JS context and nothing else.
 * A test that drives a single context therefore proves nothing here — every
 * test below that matters loads the modules TWICE, so the two "tabs" get their
 * own module state (refresh promise, in-memory access token, BroadcastChannel)
 * while sharing one localStorage, one lock manager and one server, exactly as
 * two tabs of one origin do.
 *
 * There are deliberately NO static imports of client/token-store/tab-sync: a
 * static import creates a third instance that vi.resetModules() never touches,
 * and assertions would then read the wrong module's state.
 */

vi.mock("@authsystem/i18n", () => ({
  default: { language: "en", t: (key: string) => key },
}))

const REFRESH_KEY = "auth.refreshToken"
const PENDING_KEY = "auth.refreshPending"

let storage: Map<string, string>

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
  Object.defineProperty(window, "localStorage", {
    value: mock,
    configurable: true,
    writable: true,
  })
  return store
}

/**
 * A real FIFO lock manager. jsdom has none, and a stub that grants immediately
 * would make every assertion below vacuous — the queueing IS the fix.
 */
function installWebLocks(): void {
  const tails = new Map<string, Promise<unknown>>()
  const manager = {
    request: (name: string, a: unknown, b?: unknown) => {
      const callback = (typeof a === "function" ? a : b) as () => Promise<unknown>
      const previous = tails.get(name) ?? Promise.resolve()
      const settled = previous.then(
        () => undefined,
        () => undefined
      )
      const run = settled.then(() => callback())
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
  Object.defineProperty(navigator, "locks", {
    value: manager,
    configurable: true,
    writable: true,
  })
}

function removeWebLocks(): void {
  Object.defineProperty(navigator, "locks", {
    value: undefined,
    configurable: true,
    writable: true,
  })
}

/**
 * Delivers to every other open channel of the same name and never to the
 * sender, which is the contract that matters here. Stubbed explicitly because
 * jsdom does not define BroadcastChannel, which leaves Node's process-wide one
 * in place — it would appear to work while leaking state across test files.
 */
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

function accessToken(sequence: number, secondsToExpiry = 900): string {
  const payload = {
    sub: "65c4e1e6-0000-0000-0000-000000000000",
    jti: `access-${sequence}`,
    exp: Math.floor(Date.now() / 1000) + secondsToExpiry,
  }
  const encoded = btoa(JSON.stringify(payload))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/, "")
  return `header.${encoded}.signature`
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  })
}

interface FakeServer {
  presented: string[]
  refreshCalls: () => number
  reuseDetections: () => number
}

/**
 * Models the server's actual rotation and reuse-detection behaviour: a token is
 * spendable once, a spent token is reported as reuse AND revokes every live
 * token for the account. Anything the client does that produces a reuse
 * detection here would produce the production log line.
 */
function installServer(initialRefreshToken = "R0"): FakeServer {
  const live = new Set<string>([initialRefreshToken])
  const spent = new Set<string>()
  const presented: string[] = []
  let issued = 0
  let reuse = 0

  const handler = async (input: unknown, init?: RequestInit): Promise<Response> => {
    const request = input as { url?: string; headers?: Headers; clone?: () => Request }
    const url =
      typeof input === "string" ? input : (request.url ?? String(input))
    const headers =
      request.headers instanceof Headers
        ? request.headers
        : new Headers((init?.headers as HeadersInit | undefined) ?? {})

    if (!url.includes("/Auth/refresh")) {
      return headers.has("Authorization")
        ? json(200, { id: "65c4e1e6", email: "user@example.com" })
        : json(401, { title: "Auth.Unauthorized" })
    }

    const raw =
      typeof request.clone === "function"
        ? await request.clone().text()
        : String(init?.body ?? "{}")
    const { refreshToken } = JSON.parse(raw) as { refreshToken: string }
    presented.push(refreshToken)

    if (spent.has(refreshToken)) {
      reuse += 1
      live.clear()
      return json(403, { title: "Auth.TokenRevoked" })
    }
    if (!live.has(refreshToken)) {
      return json(404, { title: "Auth.RefreshTokenNotFound" })
    }

    live.delete(refreshToken)
    spent.add(refreshToken)
    issued += 1
    const next = `R${issued}`
    live.add(next)
    return json(200, {
      accessToken: accessToken(issued),
      refreshToken: next,
    })
  }

  vi.stubGlobal("fetch", vi.fn(handler))

  return {
    presented,
    refreshCalls: () => presented.length,
    reuseDetections: () => reuse,
  }
}

// ------------------------------------------------------------------ tab setup

type Tab = {
  client: typeof import("@authsystem/api/client")
  tokenStore: typeof import("@authsystem/api/token-store")
  tabSync: typeof import("@authsystem/api/tab-sync")
}

const openTabs: Tab[] = []

/**
 * Loads a fresh instance of the whole token stack. resetModules() is called
 * per tab rather than once up front: calling it once and importing twice hands
 * back the same namespace the second time, because the first import has already
 * repopulated the registry.
 */
async function openTab(): Promise<Tab> {
  vi.resetModules()
  const tokenStore = await import("@authsystem/api/token-store")
  const tabSync = await import("@authsystem/api/tab-sync")
  const client = await import("@authsystem/api/client")
  const tab = { client, tokenStore, tabSync }
  openTabs.push(tab)
  return tab
}

function storageEvent(key: string, newValue: string | null): StorageEvent {
  const event = new Event("storage") as StorageEvent
  Object.assign(event, { key, newValue, storageArea: window.localStorage })
  return event
}

beforeEach(() => {
  storage = installLocalStorage()
  installWebLocks()
  installBroadcastChannel()
})

afterEach(() => {
  for (const tab of openTabs.splice(0)) tab.tabSync.stopTabSync()
  vi.unstubAllGlobals()
  vi.resetModules()
})

// ----------------------------------------------------------------------- specs

describe("concurrent tabs", () => {
  it("spends the shared refresh token once when two tabs refresh together", async () => {
    storage.set(REFRESH_KEY, "R0")
    const server = installServer()

    const tabA = await openTab()
    const tabB = await openTab()

    const [okA, okB] = await Promise.all([
      tabA.client.sharedRefresh(),
      tabB.client.sharedRefresh(),
    ])

    expect(okA).toBe(true)
    expect(okB).toBe(true)
    // The whole point: two contexts, ONE network refresh.
    expect(server.refreshCalls()).toBe(1)
    expect(server.presented).toEqual(["R0"])
    expect(server.reuseDetections()).toBe(0)

    // The tab that queued adopted the winner's access token instead of
    // spending the rotated refresh token a second time.
    expect(tabA.tokenStore.getAccessToken()).not.toBeNull()
    expect(tabB.tokenStore.getAccessToken()).toBe(
      tabA.tokenStore.getAccessToken()
    )
    expect(storage.get(REFRESH_KEY)).toBe("R1")
  })

  it("waits rather than failing: the queued tab still ends up authenticated", async () => {
    storage.set(REFRESH_KEY, "R0")
    installServer()

    const tabA = await openTab()
    const tabB = await openTab()

    const results = await Promise.all([
      tabA.client.ensureFreshAccessToken(),
      tabB.client.ensureFreshAccessToken(),
    ])

    // Multi-tab is a supported scenario — neither context may be left signed out.
    expect(results[0]).toBeTruthy()
    expect(results[1]).toBeTruthy()
  })

  it("never replays a spent token, even when the broadcast is missed", async () => {
    // No BroadcastChannel: the queued tab cannot adopt the winner's access
    // token, so it must fall through and rotate the CURRENT token. That costs a
    // second network call and must still cost zero reuse detections.
    vi.stubGlobal("BroadcastChannel", undefined)
    storage.set(REFRESH_KEY, "R0")
    const server = installServer()

    const tabA = await openTab()
    const tabB = await openTab()

    const [okA, okB] = await Promise.all([
      tabA.client.sharedRefresh(),
      tabB.client.sharedRefresh(),
    ])

    expect(okA).toBe(true)
    expect(okB).toBe(true)
    expect(server.refreshCalls()).toBe(2)
    // The hard invariant: each request carried the token that was current at
    // the moment it was sent. "R0" twice is the production bug.
    expect(server.presented).toEqual(["R0", "R1"])
    expect(server.reuseDetections()).toBe(0)
  })

  it("serialises three tabs onto one live token chain", async () => {
    vi.stubGlobal("BroadcastChannel", undefined)
    storage.set(REFRESH_KEY, "R0")
    const server = installServer()

    const tabs = [await openTab(), await openTab(), await openTab()]
    const results = await Promise.all(
      tabs.map((tab) => tab.client.sharedRefresh())
    )

    expect(results).toEqual([true, true, true])
    expect(server.reuseDetections()).toBe(0)
    expect(new Set(server.presented).size).toBe(server.presented.length)
  })
})

describe("replayed refresh", () => {
  it("spends a rejected token once per request cycle, not twice", async () => {
    // The production signature was a PAIR of reuse warnings ~400-560ms apart:
    // one from the proactive refresh in onRequest, one from the 401 handler in
    // onResponse re-refreshing the same dead token.
    storage.set(REFRESH_KEY, "STALE")
    const server = installServer()

    const tab = await openTab()
    await tab.client.api.GET("/api/v1/Auth/me")

    expect(server.refreshCalls()).toBe(1)
    expect(server.presented).toEqual(["STALE"])
    expect(tab.tokenStore.getRefreshToken()).toBeNull()
  })

  it("drops a refresh token the server called final", async () => {
    storage.set(REFRESH_KEY, "STALE")
    installServer()

    const tab = await openTab()
    await tab.client.sharedRefresh()

    expect(tab.tokenStore.getRefreshToken()).toBeNull()
  })

  it("signs out cleanly when the server says this session was already ended", async () => {
    // What another device now receives after a mass revocation elsewhere. It is
    // NOT reported as reuse any more, so the tab must simply end the session
    // rather than keep a dead token around to replay.
    storage.set(REFRESH_KEY, "R0")
    const fetchMock = vi.fn(async () =>
      json(403, { title: "Auth.RefreshTokenRevoked" })
    )
    vi.stubGlobal("fetch", fetchMock)

    const tab = await openTab()
    const expired = vi.fn()
    window.addEventListener(tab.tabSync.SESSION_EXPIRED_EVENT, expired)

    expect(await tab.client.ensureFreshAccessToken()).toBeNull()

    window.removeEventListener(tab.tabSync.SESSION_EXPIRED_EVENT, expired)
    expect(tab.tokenStore.getRefreshToken()).toBeNull()
    expect(expired).toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it("keeps the refresh token when the rejection is not about the token", async () => {
    storage.set(REFRESH_KEY, "R0")
    // A CDN/WAF 429 and an inactive-application 403 both leave the credential
    // valid; clearing on the status class alone would sign the fleet out.
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => json(429, { title: "Too Many Requests" }))
    )

    const tab = await openTab()
    const ok = await tab.client.sharedRefresh()

    expect(ok).toBe(false)
    expect(tab.tokenStore.getRefreshToken()).toBe("R0")
  })

  it("keeps the refresh token when the network fails outright", async () => {
    storage.set(REFRESH_KEY, "R0")
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new TypeError("Failed to fetch")
      })
    )

    const tab = await openTab()

    expect(await tab.client.sharedRefresh()).toBe(false)
    expect(tab.tokenStore.getRefreshToken()).toBe("R0")
    expect(storage.get(PENDING_KEY)).toBeUndefined()
  })
})

describe("a refresh whose context died mid-flight", () => {
  it("does not replay the token it never learned the outcome of", async () => {
    // Reload during a refresh: the server already rotated and revoked R0, but
    // the response died with the document. Replaying R0 is what the server
    // reports as theft.
    storage.set(REFRESH_KEY, "R0")
    storage.set(PENDING_KEY, "R0")
    const server = installServer()

    const tab = await openTab()

    expect(server.refreshCalls()).toBe(0)
    expect(tab.tokenStore.getRefreshToken()).toBeNull()
    expect(storage.get(PENDING_KEY)).toBeUndefined()
  })

  it("keeps the session when another tab already rotated past the marker", async () => {
    storage.set(REFRESH_KEY, "R1")
    storage.set(PENDING_KEY, "R0")
    installServer("R1")

    const tab = await openTab()

    expect(tab.tokenStore.getRefreshToken()).toBe("R1")
    expect(storage.get(PENDING_KEY)).toBeUndefined()
  })

  it("clears the marker once the refresh has settled", async () => {
    storage.set(REFRESH_KEY, "R0")
    installServer()

    const tab = await openTab()
    await tab.client.sharedRefresh()

    expect(storage.get(PENDING_KEY)).toBeUndefined()
  })
})

describe("storage propagation", () => {
  it("ends the session in a tab when another tab clears the refresh token", async () => {
    storage.set(REFRESH_KEY, "R0")
    installServer()

    const tab = await openTab()
    tab.tokenStore.setAccessToken(accessToken(1))

    const expired = vi.fn()
    window.addEventListener(tab.tabSync.SESSION_EXPIRED_EVENT, expired)

    storage.delete(REFRESH_KEY)
    window.dispatchEvent(storageEvent(REFRESH_KEY, null))

    window.removeEventListener(tab.tabSync.SESSION_EXPIRED_EVENT, expired)

    expect(expired).toHaveBeenCalled()
    expect(tab.tokenStore.getAccessToken()).toBeNull()
  })

  it("ignores storage writes for unrelated keys", async () => {
    storage.set(REFRESH_KEY, "R0")
    installServer()

    const tab = await openTab()
    tab.tokenStore.setAccessToken(accessToken(1))

    window.dispatchEvent(storageEvent("vite-ui-theme", "dark"))

    expect(tab.tokenStore.getAccessToken()).not.toBeNull()
  })
})

describe("startup handshake", () => {
  it("lets a new tab adopt a live access token instead of rotating", async () => {
    storage.set(REFRESH_KEY, "R0")
    const server = installServer()

    const first = await openTab()
    await first.client.sharedRefresh()
    expect(server.refreshCalls()).toBe(1)

    // A second tab opens while the first holds a live access token.
    const second = await openTab()

    expect(second.tokenStore.getAccessToken()).toBe(
      first.tokenStore.getAccessToken()
    )
    expect(await second.client.ensureFreshAccessToken()).toBeTruthy()
    // Opening a tab must not cost a rotation of the shared single-use token.
    expect(server.refreshCalls()).toBe(1)
  })
})

describe("when the lock cannot be had", () => {
  it("refreshes unlocked rather than failing the request", async () => {
    // A tab whose fetch never settles holds the lock until its document dies,
    // so the wait is bounded. Both rejection names must be handled: a manually
    // cancelled signal gives AbortError, but AbortSignal.timeout() gives
    // TimeoutError, and treating that as unexpected would rethrow — turning the
    // safety valve into a hard failure of the very request it protects.
    for (const name of ["AbortError", "TimeoutError"]) {
      storage.clear()
      storage.set(REFRESH_KEY, "R0")
      const server = installServer()

      Object.defineProperty(navigator, "locks", {
        value: {
          request: () => {
            const error = new Error("lock wait gave up")
            error.name = name
            return Promise.reject(error)
          },
        },
        configurable: true,
        writable: true,
      })

      const tab = await openTab()

      expect(await tab.client.sharedRefresh()).toBe(true)
      expect(server.refreshCalls()).toBe(1)
      tab.tabSync.stopTabSync()
    }
  })

  it("propagates a genuine lock-manager failure instead of hiding it", async () => {
    storage.set(REFRESH_KEY, "R0")
    installServer()

    Object.defineProperty(navigator, "locks", {
      value: { request: () => Promise.reject(new Error("SecurityError")) },
      configurable: true,
      writable: true,
    })

    const tab = await openTab()

    await expect(tab.client.sharedRefresh()).rejects.toThrow("SecurityError")
  })
})

describe("without Web Locks", () => {
  it("still refreshes, degrading to the pre-lock behaviour", async () => {
    removeWebLocks()
    storage.set(REFRESH_KEY, "R0")
    const server = installServer()

    const tab = await openTab()

    expect(await tab.client.sharedRefresh()).toBe(true)
    expect(server.refreshCalls()).toBe(1)
    expect(server.reuseDetections()).toBe(0)
  })
})

/**
 * The browser identifier that tells one client from another.
 *
 * It used to be a field in the body of each request that mints a session, so
 * every such endpoint had to remember it. verify-email did not, which recorded
 * the sign-in completing a registration under a signature built from an empty
 * id — and the user's next login, which did send one, was filed as a different
 * browser and emailed about.
 */
describe("identifying the browser", () => {
  /** Matches on the path exactly: "/Auth/login" must not find "login-history". */
  function sentHeaders(path: string): Headers | undefined {
    const calls = (globalThis.fetch as unknown as { mock: { calls: unknown[][] } })
      .mock.calls
    return calls
      .map((call) => call[0] as Request)
      .find(
        (request) =>
          typeof request?.url === "string" &&
          new URL(request.url).pathname === path
      )?.headers
  }

  it("sends the device header on the login flow, which carries no token", async () => {
    // The header is set ABOVE the auth-flow early return. Setting it below —
    // where the Authorization header goes — would leave every anonymous
    // session-minting endpoint exactly as blind as the body field did.
    installServer()
    const tab = await openTab()

    await tab.client.api.POST("/api/v1/Auth/login", {
      body: { email: "user@example.com", password: "x" },
    })

    expect(
      sentHeaders("/api/v1/Auth/login")?.get("X-Device-Id")
    ).toBeTruthy()
  })

  it("sends the same value on an authenticated request", async () => {
    // One browser, one identifier, whatever the endpoint — that equality is the
    // whole fix. A per-call-site value is what allowed two to disagree.
    storage.set(REFRESH_KEY, "R0")
    installServer()
    const tab = await openTab()

    await tab.client.api.POST("/api/v1/Auth/login", {
      body: { email: "user@example.com", password: "x" },
    })
    await tab.client.api.GET("/api/v1/Auth/login-history" as never, {} as never)

    const onLogin = sentHeaders("/api/v1/Auth/login")?.get("X-Device-Id")
    const onHistory = sentHeaders("/api/v1/Auth/login-history")?.get("X-Device-Id")

    expect(onLogin).toBeTruthy()
    expect(onHistory).toBe(onLogin)
  })
})

/**
 * Which endpoints are treated as anonymous.
 *
 * The fake server answers any non-refresh request 200 when an Authorization
 * header is present and 401 when it is not, so "did the client authenticate
 * this?" is readable straight off the status.
 */
describe("deciding which requests carry a token", () => {
  it("authenticates a path that merely starts with the login path", async () => {
    // The defect: isAuthFlow() used url.includes(LOGIN_PATH), so
    // "/api/v1/Auth/login-history" counted as the login endpoint. The client
    // sent it with no token, took the 401 as expected, and skipped the refresh
    // retry too — a signed-in user saw a permanently failing panel with nothing
    // in the logs pointing at the cause.
    storage.set(REFRESH_KEY, "R0")
    installServer()
    const tab = await openTab()

    const { response } = await tab.client.api.GET(
      "/api/v1/Auth/login-history" as never,
      {} as never
    )

    expect(response.status).toBe(200)
  })

  it("still sends the login endpoint itself anonymously", async () => {
    // The other half of the same guard: tightening the match must not start
    // attaching tokens to the endpoints that mint them.
    storage.set(REFRESH_KEY, "R0")
    installServer()
    const tab = await openTab()

    const { response } = await tab.client.api.POST("/api/v1/Auth/login", {
      body: { email: "user@example.com", password: "x" },
    })

    expect(response.status).toBe(401)
  })

  it("recognises the two-factor verify path despite its lower-case controller", async () => {
    // The API spells this route "/api/v1/auth/2fa/verify" while login and
    // refresh use "/api/v1/Auth/…". Under the old case-sensitive test the
    // constant never matched, so the check silently covered two paths, not three.
    storage.set(REFRESH_KEY, "R0")
    installServer()
    const tab = await openTab()

    const { response } = await tab.client.api.POST(
      "/api/v1/auth/2fa/verify" as never,
      { body: { code: "000000" } } as never
    )

    expect(response.status).toBe(401)
  })
})
