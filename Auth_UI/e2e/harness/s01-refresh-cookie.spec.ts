import type { BrowserContext, Page } from "@playwright/test"

import { HOSTS, ORIGINS, expect, test, type HarnessApi } from "./fixtures"
import { SENTINEL, refreshCookieName, type FirstPartyServer } from "./first-party-session"

/**
 * S01 in a real browser: the refresh token of the console and the accounts app
 * lives in a per-app `__Host-` HttpOnly Strict cookie on the API host, and page
 * scripts never see it. Checks B1-B11 of the card, each with its negative
 * control — a variant that the same assertion must catch, so no check can pass
 * because it went blind.
 *
 * What these prove and what they do not (H8): the harness never runs the real
 * API. The API host answers through first-party-session.ts, a model of the
 * server's delivery. What is proven here is what only a browser decides — what
 * it stores, what it attaches to which request, what it withholds — under each
 * app's real web.config CSP, on same-site HTTPS origins. The server's own
 * decisions (E1's cookie choice, E4's Origin barrier) are pinned by the xUnit
 * suites and by the owner's post-enable check (rollout step 4).
 *
 * B8 (no CSP violation) holds for every test in this file: the csp fixture is
 * automatic and fails a test on any securitypolicyviolation beyond the register.
 */

const CONSOLE_COOKIE = refreshCookieName(ORIGINS.console)
const ACCOUNTS_COOKIE = refreshCookieName(ORIGINS.accounts)

const USER = {
  id: "99999999-9999-9999-9999-999999999999",
  email: "isolated@example.test",
  firstName: "Isolated",
  lastName: "Operator",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

/** Everything a script on the page can read back: both storages and every IndexedDB store. */
async function readableStorage(page: Page): Promise<string> {
  return page.evaluate(async () => {
    const dump = (storage: Storage) =>
      Object.fromEntries(Array.from({ length: storage.length }, (_, index) => {
        const key = storage.key(index) as string
        return [key, storage.getItem(key)]
      }))
    const databases: Record<string, unknown[]> = {}
    for (const info of await indexedDB.databases()) {
      if (!info.name) continue
      const db = await new Promise<IDBDatabase>((resolve, reject) => {
        const open = indexedDB.open(info.name as string)
        open.onsuccess = () => resolve(open.result)
        open.onerror = () => reject(open.error)
      })
      for (const store of Array.from(db.objectStoreNames)) {
        databases[`${info.name}/${store}`] = await new Promise<unknown[]>((resolve) => {
          const all = db.transaction(store).objectStore(store).getAll()
          all.onsuccess = () => resolve(all.result)
        })
      }
      db.close()
    }
    return JSON.stringify({
      localStorage: dump(localStorage),
      sessionStorage: dump(sessionStorage),
      indexedDB: databases,
    })
  })
}

async function refreshCookie(context: BrowserContext, name: string) {
  return (await context.cookies(ORIGINS.api)).find((cookie) => cookie.name === name)
}

async function signInThroughTheForm(page: Page, app: "console" | "accounts") {
  await page.goto(`${ORIGINS[app]}/login`)
  await page.locator('input[type="email"]').fill(USER.email)
  await page.locator('input[type="password"]').fill("Harness1!")
  await page.locator('button[type="submit"]').click()
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
}

async function useSignIn(api: HarnessApi, secret: string) {
  await api.useAuthenticated(
    [],
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/auth/login") return false
      await api.firstParty.answerSignIn(route, USER, secret)
      return true
    },
    { session: "none" }
  )
}

async function waitForRefreshes(firstParty: FirstPartyServer, origin: string, count: number) {
  await expect.poll(() => firstParty.from(origin).length, { timeout: 10_000 }).toBeGreaterThanOrEqual(count)
}

// ---------------------------------------------------------------- B1

for (const app of ["console", "accounts"] as const) {
  test(`B1 ${app}: after sign-in no readable storage holds the refresh token; the browser keeps it HttpOnly`, async ({
    page,
    context,
    api,
  }) => {
    const secret = `signin-secret-${app}`
    await useSignIn(api, secret)

    await signInThroughTheForm(page, app)

    const readable = await readableStorage(page)
    expect(readable).not.toContain(secret)
    expect(JSON.parse(readable).localStorage["auth.refreshToken"]).toBe(SENTINEL)

    const cookie = await refreshCookie(context, refreshCookieName(ORIGINS[app]))
    expect(cookie).toMatchObject({ value: secret, httpOnly: true, secure: true, sameSite: "Strict", path: "/" })
    expect(cookie?.domain).toBe(HOSTS.api)
    expect(await page.evaluate(() => window.isSecureContext)).toBe(true)
  })
}

test("B1 negative control: a token delivered in the body IS found in readable storage", async ({ page, api }) => {
  const secret = "signin-secret-legacy"
  await useSignIn(api, secret)
  api.firstParty.mode = "legacy-body"

  await signInThroughTheForm(page, "console")

  expect(await readableStorage(page)).toContain(secret)
})

// ---------------------------------------------------------------- B2

test("B2: the refresh carries the app's cookie and its Origin with an empty body, and survives a reload", async ({
  page,
  api,
}) => {
  await api.useAuthenticated([])

  await page.goto(`${ORIGINS.console}/`)
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)
  const [first] = api.firstParty.from(ORIGINS.console)
  expect(first).toMatchObject({ origin: ORIGINS.console, body: "{}", cookie: "seeded-console" })

  await page.reload()
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 2)
  // The browser stored the rotated cookie from the first answer and sent that.
  expect(api.firstParty.from(ORIGINS.console)[1]).toMatchObject({ body: "{}", cookie: "rotated-1" })
})

test("B2 negative control: with the cookie gone, the check sees no cookie on the refresh", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  await context.clearCookies()

  await page.goto(`${ORIGINS.console}/`)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)

  expect(api.firstParty.from(ORIGINS.console)[0].cookie).toBeNull()
  // Where the page lands afterwards is not asserted: the shared /me mock answers
  // without a bearer, which the real API never does.
})

// ---------------------------------------------------------------- B3

async function openTwoConsoleTabs(page: Page, context: BrowserContext, withoutLocksInSecond: boolean) {
  const second = await context.newPage()
  if (withoutLocksInSecond) {
    await second.addInitScript(() => {
      Object.defineProperty(navigator, "locks", { value: undefined, configurable: true })
    })
  }
  await Promise.all([page.goto(`${ORIGINS.console}/`), second.goto(`${ORIGINS.console}/`)])
  return second
}

test("B3 (E2): two tabs of one app refreshing together send ONE refresh; no cookie value twice; no sign-out", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  // Hold the refresh so both tabs are booting while it is in flight.
  api.firstParty.refreshDelayMs = 800

  const second = await openTwoConsoleTabs(page, context, false)

  for (const tab of [page, second]) await expect(tab).not.toHaveURL(/\/login(\?|$)/)
  await page.waitForTimeout(1_500)
  const seen = api.firstParty.from(ORIGINS.console)
  expect(seen).toHaveLength(1)
  expect(new Set(seen.map((refresh) => refresh.cookie)).size).toBe(seen.length)
})

test("B3 negative control: without Web Locks in one tab, the check sees the same cookie spent twice", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  api.firstParty.refreshDelayMs = 800

  await openTwoConsoleTabs(page, context, true)

  await waitForRefreshes(api.firstParty, ORIGINS.console, 2)
  const cookies = api.firstParty.from(ORIGINS.console).map((refresh) => refresh.cookie)
  expect(new Set(cookies).size).toBeLessThan(cookies.length)
})

// ---------------------------------------------------------------- B4

test("B4 (E1): console and accounts together - each refresh names its own Origin while both cookies ride along", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  const accounts = await context.newPage()

  // Two phases, so which app sent each refresh is known from the test, not
  // read back from the header under test: the console alone, then the accounts
  // app joins it in the same browser.
  await page.goto(`${ORIGINS.console}/`)
  await expect.poll(() => api.firstParty.refreshes.length, { timeout: 10_000 }).toBeGreaterThanOrEqual(1)
  const consolePhase = [...api.firstParty.refreshes]
  await accounts.goto(`${ORIGINS.accounts}/`)
  await expect
    .poll(() => api.firstParty.refreshes.length, { timeout: 10_000 })
    .toBeGreaterThan(consolePhase.length)
  const accountsPhase = api.firstParty.refreshes.slice(consolePhase.length)

  // Every refresh names, literally, the app that sent it - the form
  // FirstPartyOriginResolverTests matches: scheme and host, no slash - while the
  // browser attaches BOTH apps' cookies, so Origin is the only thing that can
  // tell the server whose cookie to spend.
  const eachNames = (phase: typeof consolePhase, origin: string) => {
    expect(phase.length).toBeGreaterThan(0)
    for (const refresh of phase) {
      expect(refresh.origin).toBe(origin)
      expect(refresh.cookieNames).toEqual(expect.arrayContaining([CONSOLE_COOKIE, ACCOUNTS_COOKIE]))
    }
  }
  eachNames(consolePhase, "https://console.example.com")
  eachNames(accountsPhase, "https://accounts.example.com")

  // Negative control: the same check with the apps swapped must fail.
  expect(() => eachNames(consolePhase, ORIGINS.accounts)).toThrow()
  expect(() => eachNames(accountsPhase, ORIGINS.console)).toThrow()

  // Both stay signed in, and the console keeps renewing with the accounts app open.
  await page.reload()
  for (const tab of [page, accounts]) await expect(tab).not.toHaveURL(/\/login(\?|$)/)
})

// ---------------------------------------------------------------- B5 / B6

function forgeryPage(target: string, init: string) {
  return `<!doctype html><title>forgery</title><script>
    for (const path of ["/api/v1/Auth/refresh", "/api/v1/Auth/end-session"]) {
      fetch("${target}" + path, ${init}).catch(() => undefined)
    }
  </script>`
}

test("B5 (E4): a same-site page's forged refresh carries ITS Origin, and the Strict cookie rides along", async ({
  page,
  api,
  attacker,
  requests,
}) => {
  await api.useAuthenticated([])
  const url = attacker.serve(
    HOSTS.apex,
    "/forge.html",
    forgeryPage(
      ORIGINS.api,
      // The Origin header is forbidden to scripts: this attempt is dropped by the browser.
      `{ method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "Origin": "${ORIGINS.console}" }, body: "{}" }`
    )
  )

  await page.goto(url)
  await expect.poll(() => requests.to(HOSTS.api).filter((entry) => entry.method === "POST").length).toBe(2)

  for (const entry of requests.to(HOSTS.api).filter((request) => request.method === "POST")) {
    expect(entry.origin, entry.path).toBe(ORIGINS.apex)
    // Same site: SameSite=Strict does NOT stop this. Only the server's Origin
    // list does - which is exactly why that list exists.
    expect(entry.cookie ?? "", entry.path).toContain(CONSOLE_COOKIE)
  }
})

test("B6 (E4): a cross-site page's forged request arrives WITHOUT the Strict cookie", async ({
  page,
  api,
  attacker,
  requests,
}) => {
  await api.useAuthenticated([])
  const url = attacker.serve(
    HOSTS.attacker,
    "/forge.html",
    forgeryPage(ORIGINS.api, `{ method: "POST", mode: "no-cors", credentials: "include", body: "x" }`)
  )

  await page.goto(url)
  await expect.poll(() => requests.to(HOSTS.api).filter((entry) => entry.method === "POST").length).toBe(2)

  for (const entry of requests.to(HOSTS.api).filter((request) => request.method === "POST")) {
    expect(entry.origin).toBe(ORIGINS.attacker)
    expect(entry.cookie ?? "").not.toContain(CONSOLE_COOKIE)
  }
})

test("B6 negative control: a SameSite=None cookie IS attached to the same cross-site request", async ({
  page,
  context,
  api,
  attacker,
  requests,
}) => {
  await api.useAuthenticated([])
  await context.addCookies([
    { name: "harness_none", value: "1", url: ORIGINS.api, secure: true, httpOnly: true, sameSite: "None" },
  ])
  const url = attacker.serve(
    HOSTS.attacker,
    "/forge.html",
    forgeryPage(ORIGINS.api, `{ method: "POST", mode: "no-cors", credentials: "include", body: "x" }`)
  )

  await page.goto(url)
  await expect.poll(() => requests.to(HOSTS.api).filter((entry) => entry.method === "POST").length).toBe(2)

  for (const entry of requests.to(HOSTS.api).filter((request) => request.method === "POST")) {
    expect(entry.cookie ?? "").toContain("harness_none=1")
  }
})

// ---------------------------------------------------------------- B7

test("B7 (E3): a stored legacy token is sent once, replaced by the sentinel, and the next refresh is {}", async ({
  page,
  api,
}) => {
  await api.useAuthenticated([], undefined, { session: "legacy" })

  await page.goto(`${ORIGINS.console}/`)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)
  expect(JSON.parse(api.firstParty.from(ORIGINS.console)[0].body)).toEqual({ refreshToken: "isolated-refresh" })
  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.refreshToken"))).toBe(SENTINEL)

  await page.reload()
  await waitForRefreshes(api.firstParty, ORIGINS.console, 2)
  expect(api.firstParty.from(ORIGINS.console)[1]).toMatchObject({ body: "{}", cookie: "rotated-1" })
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
})

test("B7 negative control: a server that answers in the body leaves a non-sentinel value the check sees", async ({
  page,
  api,
}) => {
  await api.useAuthenticated([], undefined, { session: "legacy" })
  api.firstParty.mode = "legacy-body"

  await page.goto(`${ORIGINS.console}/`)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)

  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.refreshToken"))).toBe("rotated-1")
})

// ---------------------------------------------------------------- B9

async function recordStorageEvents(page: Page) {
  await page.addInitScript(() => {
    const events: (string | null)[] = []
    ;(window as unknown as { __refreshEvents: (string | null)[] }).__refreshEvents = events
    // What an older bundle's listener sees (tab-sync.ts): a removal is a sign-out.
    window.addEventListener("storage", (event) => {
      if (event.key === "auth.refreshToken") events.push(event.newValue)
    })
  })
}

const recorded = (page: Page) =>
  page.evaluate(() => (window as unknown as { __refreshEvents: (string | null)[] }).__refreshEvents)

test("B9 (E3): an old-bundle tab and a migrating tab share one origin without signing each other out", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([], undefined, { session: "legacy" })
  api.firstParty.refreshDelayMs = 800
  const oldBundle = await context.newPage()
  await recordStorageEvents(oldBundle)

  await page.goto(`${ORIGINS.console}/`)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)
  await oldBundle.goto(`${ORIGINS.console}/`)
  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.refreshToken"))).toBe(SENTINEL)

  const events = await recorded(oldBundle)
  expect(events).toContain(SENTINEL)
  expect(events).not.toContain(null)
  for (const tab of [page, oldBundle]) await expect(tab).not.toHaveURL(/\/login(\?|$)/)

  // An older bundle posts back what it stored; the cookie still rides along.
  const before = api.firstParty.from(ORIGINS.console).length
  await oldBundle.evaluate(async (target) => {
    await fetch(`${target}/api/v1/Auth/refresh`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: "__cookie__" }),
    })
  }, ORIGINS.api)
  const echoed = api.firstParty.from(ORIGINS.console)[before]
  expect(JSON.parse(echoed.body)).toEqual({ refreshToken: SENTINEL })
  expect(echoed.cookie).not.toBeNull()
})

test("B9 negative control: a sign-out in one tab IS recorded as a removal in the other", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  const other = await context.newPage()
  await recordStorageEvents(other)
  await page.goto(`${ORIGINS.console}/`)
  await other.goto(`${ORIGINS.console}/`)
  await expect(other).not.toHaveURL(/\/login(\?|$)/)

  await page.evaluate(() => localStorage.removeItem("auth.refreshToken"))

  await expect.poll(() => recorded(other)).toContain(null)
})

// ---------------------------------------------------------------- B10

test("B10: switching the cookie off returns the token to storage, expires the cookie, and signs no one out", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  api.firstParty.mode = "rollback"

  await page.goto(`${ORIGINS.console}/`)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)
  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.refreshToken"))).toBe("rotated-1")
  expect(await refreshCookie(context, CONSOLE_COOKIE)).toBeUndefined()
  await expect(page).not.toHaveURL(/\/login(\?|$)/)

  await page.reload()
  await waitForRefreshes(api.firstParty, ORIGINS.console, 2)
  expect(JSON.parse(api.firstParty.from(ORIGINS.console)[1].body)).toEqual({ refreshToken: "rotated-1" })
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
})

test("B10 negative control: an answer that does not expire the cookie leaves it in the jar", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  api.firstParty.mode = "rollback-keeping-cookie"

  await page.goto(`${ORIGINS.console}/`)
  await waitForRefreshes(api.firstParty, ORIGINS.console, 1)

  expect(await refreshCookie(context, CONSOLE_COOKIE)).toBeDefined()
})

// ---------------------------------------------------------------- B11

async function signOutFromTheUserMenu(page: Page) {
  await page.getByRole("button", { name: `${USER.firstName} ${USER.lastName}` }).click()
  await page.getByRole("menuitem", { name: "Sign out" }).click()
}

/**
 * Records, from the first script of the load, whether the signed-in shell (its
 * user menu) is ever rendered. The same element the sign-out clicks, so a check
 * that stays undefined proves something only because the selector can match.
 */
async function watchForTheSignedInShell(page: Page) {
  await page.addInitScript((name: string) => {
    const flag = window as unknown as { __signedInShown?: boolean }
    new MutationObserver(() => {
      if (document.querySelector(`button[aria-label="${name}"]`)) flag.__signedInShown = true
    }).observe(document, { childList: true, subtree: true })
  }, `${USER.firstName} ${USER.lastName}`)
}

const signedInShown = (page: Page) =>
  page.evaluate(() => (window as unknown as { __signedInShown?: boolean }).__signedInShown)

test("B11: a sign-out that fails on the network is finished on the next load with the cookie", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  api.firstParty.dropLogout = true

  await page.goto(`${ORIGINS.console}/`)
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
  await signOutFromTheUserMenu(page)
  await expect(page).toHaveURL(/\/login(\?|$)/)
  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.logoutPending"))).not.toBeNull()

  api.firstParty.dropLogout = false
  const sequenceBefore = api.firstParty.sequence.length
  await page.reload()

  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.logoutPending"))).toBeNull()
  // One cookie sign-out, carrying the cookie, and nothing else - no refresh.
  expect(api.firstParty.sequence.slice(sequenceBefore)).toEqual([`cookie-logout ${ORIGINS.console}`])
  expect(api.firstParty.cookieLogouts.at(-1)?.cookie).not.toBeNull()
  expect(await refreshCookie(context, CONSOLE_COOKIE)).toBeUndefined()
  await expect(page).toHaveURL(/\/login(\?|$)/)
})

test("B11 negative control: a sign-out the server answers writes no marker", async ({ page, api }) => {
  await api.useAuthenticated([])

  await page.goto(`${ORIGINS.console}/`)
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
  await signOutFromTheUserMenu(page)
  await expect(page).toHaveURL(/\/login(\?|$)/)

  expect(await page.evaluate(() => localStorage.getItem("auth.logoutPending"))).toBeNull()
})

test("B11 (F2): a sign-out refused with 401 still ends the cookie, and a reload stays signed out", async ({
  page,
  context,
  api,
}) => {
  await api.useAuthenticated([])
  // The bearer is refused, as for an idle tab whose access token expired while
  // its refresh kept failing: the bearer sign-out ends nothing on the server.
  api.firstParty.rejectLogout = true

  await page.goto(`${ORIGINS.console}/`)
  await expect(page).not.toHaveURL(/\/login(\?|$)/)
  await signOutFromTheUserMenu(page)
  await expect(page).toHaveURL(/\/login(\?|$)/)

  await expect.poll(() => api.firstParty.cookieLogouts.length).toBe(1)
  expect(api.firstParty.cookieLogouts[0].cookie).not.toBeNull()
  await expect.poll(() => refreshCookie(context, CONSOLE_COOKIE)).toBeUndefined()
  expect(await page.evaluate(() => localStorage.getItem("auth.logoutPending"))).toBeNull()

  await page.reload()
  await expect(page).toHaveURL(/\/login(\?|$)/)
})

test("B11 (F3): after a tab closed mid sign-out, the next load ends it BEFORE anything signed in renders", async ({
  page,
  context,
  api,
  requests,
}) => {
  await api.useAuthenticated([])
  // The session key survived (the tab closed before it was cleared) next to
  // the marker the sign-out wrote first.
  await context.addInitScript(() => {
    if (localStorage.getItem("harness.pending-seeded") !== null) return
    localStorage.setItem("harness.pending-seeded", "1")
    localStorage.setItem("auth.logoutPending", JSON.stringify({ at: Date.now(), sid: "harness-session" }))
  })
  await watchForTheSignedInShell(page)

  await page.goto(`${ORIGINS.console}/`)

  await expect(page).toHaveURL(/\/login(\?|$)/)
  await expect.poll(() => page.evaluate(() => localStorage.getItem("auth.logoutPending"))).toBeNull()
  expect(api.firstParty.cookieLogouts).toHaveLength(1)
  expect(JSON.parse(api.firstParty.cookieLogouts[0].body)).toEqual({ sessionId: "harness-session" })
  expect(await page.evaluate(() => localStorage.getItem("auth.refreshToken"))).toBeNull()
  expect(await refreshCookie(context, CONSOLE_COOKIE)).toBeUndefined()
  // Never signed in: no profile request, and the shell never rendered.
  expect(requests.to(HOSTS.api).map((entry) => entry.path.toLowerCase())).not.toContain("/api/v1/auth/me")
  expect(await signedInShown(page)).toBeUndefined()
})

test("B11 (F3) negative control: the shell detector does fire on a signed-in load", async ({ page, api }) => {
  await api.useAuthenticated([])
  await watchForTheSignedInShell(page)

  await page.goto(`${ORIGINS.console}/`)
  await expect(page).not.toHaveURL(/\/login(\?|$)/)

  await expect.poll(() => signedInShown(page)).toBe(true)
})
