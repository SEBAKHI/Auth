import type { Page, Route } from "@playwright/test"

import { HOSTS, ORIGINS, expect, test } from "./fixtures"

/**
 * E5, proved on every run: the three HTTPS origins are same-site with one
 * another and with the apex, attacker.example.net is not, and a Secure;
 * HttpOnly; SameSite=Lax cookie from the API host is stored, sent with
 * credentialed fetches from same-site pages only, and invisible to script.
 * If a Chromium update changes any of this, the harness fails here instead of
 * letting S01 pass on a cookie that production would never send.
 */

const PROBE_COOKIE = "harness_probe=1; Secure; HttpOnly; SameSite=Lax; Path=/"

async function probeApi(route: Route, url: URL) {
  if (url.pathname === "/harness/probe/set") {
    await route.fulfill({ status: 200, headers: { "set-cookie": PROBE_COOKIE }, json: { set: true } })
    return true
  }
  if (url.pathname === "/harness/probe/echo") {
    await route.fulfill({ json: { echo: true } })
    return true
  }
  return false
}

/** A credentialed fetch from the page; resolves to the status, or the error name when unreadable. */
function credentialedFetch(page: Page, path: string) {
  return page.evaluate(async (target) => {
    try {
      const response = await fetch(target, { credentials: "include" })
      return String(response.status)
    } catch (error) {
      return (error as Error).name
    }
  }, `${ORIGINS.api}${path}`)
}

test("the origins are secure, same-site, and share the API cookie; the cross-site origin does not", async ({
  page,
  context,
  api,
  attacker,
  requests,
}) => {
  await api.useAnonymous((route, url) => probeApi(route, url))

  await page.goto(`${ORIGINS.console}/login`)
  expect(await page.evaluate(() => window.isSecureContext)).toBe(true)
  expect(await credentialedFetch(page, "/harness/probe/set")).toBe("200")

  const [cookie] = (await context.cookies(ORIGINS.api)).filter((c) => c.name === "harness_probe")
  expect(cookie).toMatchObject({ value: "1", httpOnly: true, secure: true, sameSite: "Lax", path: "/" })

  const accounts = await context.newPage()
  await accounts.goto(`${ORIGINS.accounts}/login`)
  expect(await accounts.evaluate(() => window.isSecureContext)).toBe(true)

  const apex = await context.newPage()
  await apex.goto(attacker.serve(HOSTS.apex, "/", "<!doctype html><title>apex</title>"))

  const crossSite = await context.newPage()
  await crossSite.goto(attacker.serve(HOSTS.attacker, "/", "<!doctype html><title>attacker</title>"))

  for (const [origin, from] of [
    [ORIGINS.console, page],
    [ORIGINS.accounts, accounts],
    [ORIGINS.apex, apex],
  ] as const) {
    requests.clear()
    expect(await credentialedFetch(from, "/harness/probe/echo")).toBe("200")
    const seen = requests.to(HOSTS.api).filter((entry) => entry.path === "/harness/probe/echo")
    expect(seen, `one echo from ${origin}`).toHaveLength(1)
    expect(seen[0].cookie).toContain("harness_probe=1")
    expect(seen[0].origin).toBe(origin)
    expect(seen[0].secFetchSite).toBe("same-site")
  }

  requests.clear()
  // The API answers no CORS for this origin, so the page cannot read the reply...
  expect(await credentialedFetch(crossSite, "/harness/probe/echo")).toBe("TypeError")
  // ...and SameSite=Lax kept the cookie off the request that did reach the server.
  const crossSeen = requests.to(HOSTS.api).filter((entry) => entry.path === "/harness/probe/echo")
  expect(crossSeen).toHaveLength(1)
  expect(crossSeen[0].cookie ?? "").not.toContain("harness_probe")
  expect(crossSeen[0].origin).toBe(ORIGINS.attacker)
  expect(crossSeen[0].secFetchSite).toBe("cross-site")

  for (const app of [page, accounts]) {
    expect(await app.evaluate(() => document.cookie)).not.toContain("harness_probe")
  }
})
