import { ORIGINS, expect, test, type AppName } from "./fixtures"
import { cacheControlValue, clientCacheFor } from "./web-config-model"

/**
 * Every response from an SPA host carries what its dist-harness/web.config makes
 * IIS send: each customHeaders entry (CSP included, compared with the model read
 * from that file - no copy here), Cache-Control from clientCache for the shell
 * and from the assets <location> for fingerprinted files, and 404 for a missing
 * asset and for web.config itself.
 */
for (const app of ["console", "accounts"] as const satisfies readonly AppName[]) {
  test(`${app}: headers and cache policy come from its web.config`, async ({ page, harness }) => {
    const origin = ORIGINS[app]
    const model = harness.models[app]
    expect(model.headers.length).toBeGreaterThanOrEqual(5)

    const shell = await page.goto(`${origin}/`)
    const entry = await page.locator('script[type="module"][src^="/assets/"]').first().getAttribute("src")
    expect(entry, "the built index.html names its entry chunk").toMatch(/^\/assets\/.+\.js$/)

    const login = await page.goto(`${origin}/login`)
    const asset = await page.goto(`${origin}${entry}`)

    for (const [name, response, path] of [
      ["/", shell, "/index.html"],
      ["/login", login, "/index.html"],
      [entry!, asset, entry!],
    ] as const) {
      expect(response, name).not.toBeNull()
      expect(response!.status(), name).toBe(200)
      const headers = response!.headers()
      for (const [header, value] of model.headers) {
        expect(headers[header.toLowerCase()], `${header} on ${name}`).toBe(value)
      }
      expect(headers["cache-control"], `Cache-Control on ${name}`).toBe(
        cacheControlValue(clientCacheFor(model, path))
      )
    }
    expect(shell!.headers()["cache-control"]).toBe("no-cache")
    expect(asset!.headers()["cache-control"]).toBe("max-age=31536000")
    expect(asset!.headers()["content-type"]).toMatch(/^text\/javascript/)

    const missing = await page.goto(`${origin}/assets/nope.js`)
    expect(missing!.status()).toBe(404)
    expect(missing!.headers()["content-type"]).not.toMatch(/text\/html/)
    const webConfig = await page.goto(`${origin}/web.config`)
    expect(webConfig!.status()).toBe(404)
  })
}
