import { HOSTS, ORIGINS, expect, test } from "./fixtures"

/**
 * E1 and E2 need several pages of both applications in ONE browser context,
 * sharing one cookie jar and the browser's real Web Locks and BroadcastChannel.
 * This proves the harness gives them that: every request any of the three pages
 * sends to the API host - the applications' own calls included - carries the
 * cookie, and the coordination primitives are the native ones.
 */
test("two console pages and one accounts page share the API cookie and native coordination APIs", async ({
  page,
  context,
  api,
  requests,
}) => {
  await api.useAnonymous(async (route, url) => {
    if (url.pathname !== "/harness/probe/set") return false
    await route.fulfill({
      headers: { "set-cookie": "harness_probe=1; Secure; HttpOnly; SameSite=Lax; Path=/" },
      json: { set: true },
    })
    return true
  })

  await page.goto(`${ORIGINS.console}/login`)
  await page.evaluate((target) => fetch(target, { credentials: "include" }), `${ORIGINS.api}/harness/probe/set`)
  requests.clear()

  const secondConsole = await context.newPage()
  const accounts = await context.newPage()
  const pages = [page, secondConsole, accounts]
  await page.reload()
  await secondConsole.goto(`${ORIGINS.console}/login`)
  await accounts.goto(`${ORIGINS.accounts}/login`)
  for (const [index, each] of pages.entries()) {
    await each.evaluate(
      (target) => fetch(target, { credentials: "include" }).catch(() => undefined),
      `${ORIGINS.api}/harness/probe/page-${index}`
    )
  }

  // Preflights never carry credentials (Fetch standard), so they are left out.
  const toApi = requests.to(HOSTS.api).filter((entry) => entry.method !== "OPTIONS")
  const origins = new Set(toApi.map((entry) => entry.origin))
  expect(origins).toEqual(new Set([ORIGINS.console, ORIGINS.accounts]))
  // Floor: the three explicit probes plus the applications' own start-up calls.
  expect(toApi.length).toBeGreaterThan(3)
  for (const entry of toApi) {
    expect(entry.cookie ?? "", `${entry.method} ${entry.url} from ${entry.origin}`).toContain(
      "harness_probe=1"
    )
  }

  for (const each of pages) {
    const native = await each.evaluate(() => {
      const isNative = (value: unknown) =>
        typeof value === "function" && /\{\s*\[native code\]\s*\}/.test(Function.prototype.toString.call(value))
      return {
        locks: isNative(navigator.locks?.request),
        broadcast: isNative(window.BroadcastChannel),
      }
    })
    expect(native).toEqual({ locks: true, broadcast: true })
  }
})
