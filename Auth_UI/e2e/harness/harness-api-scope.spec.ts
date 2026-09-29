import { HOSTS, ORIGINS, expect, test } from "./fixtures"

/**
 * Who answers what. /api/ on an SPA host is web.config's business (its fallback
 * rule excludes ^/api/, so IIS answers 404) and never reaches the API. On the API
 * host, anything nobody claimed is a distinct 404 carrying x-harness-unmatched.
 * And a JSON POST from console triggers a real CORS preflight that the server -
 * not Playwright - receives and answers (spike item 3).
 */
test.beforeEach(async ({ api }) => {
  await api.useAuthenticated([], async () => false)
})

test("/api/ on an SPA host is a web.config 404 that never reaches the API", async ({ page, requests }) => {
  const response = await page.goto(`${ORIGINS.console}/api/v1/x`)
  expect(response!.status()).toBe(404)
  expect(response!.headers()["x-harness-unmatched"]).toBeUndefined()
  expect(response!.headers()["content-security-policy"]).toBeTruthy()
  expect(requests.to(HOSTS.console).map((entry) => entry.path)).toContain("/api/v1/x")
})

test("a handler cannot set CORS headers; the API host alone answers CORS", async ({ page, api }) => {
  await api.useAnonymous(async (route, url) => {
    if (url.pathname !== "/api/v1/harness-cors") return false
    await route.fulfill({ headers: { "Access-Control-Allow-Origin": "*" }, json: {} })
    return true
  })
  const response = await page.goto(`${ORIGINS.api}/api/v1/harness-cors`)
  expect(response!.status()).toBe(500)
  expect(await response!.text()).toContain("CORS headers belong to the harness API host")
})

test("an unclaimed API request gets 404 with x-harness-unmatched", async ({ page }) => {
  const response = await page.goto(`${ORIGINS.api}/api/v1/unknown`)
  expect(response!.status()).toBe(404)
  expect(response!.headers()["x-harness-unmatched"]).toBe("1")
})

test("a JSON POST from console sends its CORS preflight to the server", async ({ page, requests }) => {
  await page.goto(`${ORIGINS.console}/login`)
  requests.clear()
  const status = await page.evaluate(async (target) => {
    const response = await fetch(target, {
      method: "POST",
      credentials: "include",
      headers: { "content-type": "application/json" },
      body: "{}",
    })
    return response.status
  }, `${ORIGINS.api}/api/v1/harness-probe`)
  expect(status).toBe(404)
  const seen = requests
    .to(HOSTS.api)
    .filter((entry) => entry.path === "/api/v1/harness-probe")
    .map((entry) => entry.method)
  expect(seen).toEqual(["OPTIONS", "POST"])
})
