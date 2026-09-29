import { mkdtempSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"

import { ORIGINS, expect, test } from "./fixtures"
import { headerValue } from "./web-config-model"

/**
 * /privacy is served from a mounted folder with the ACCOUNTS site's rewrite
 * rules and headers, as the production virtual directory is. The documents here
 * are synthetic: they prove the mapping and the headers, nothing about what
 * PolicyDocumentRenderer writes (that is S02's check, on a rendered document).
 */
test("/privacy maps to the mounted documents under the accounts headers and CSP", async ({
  page,
  privacy,
  harness,
}) => {
  const dir = mkdtempSync(join(tmpdir(), "harness-privacy-"))
  try {
    writeFileSync(join(dir, "index.html"), "<!doctype html><title>index</title><p>synthetic privacy index</p>")
    writeFileSync(join(dir, "ar.html"), '<!doctype html><html lang="ar"><title>ar</title><p>synthetic ar</p>')
    privacy.mount(dir)

    const redirects: string[] = []
    page.on("response", (response) => {
      if (response.status() >= 300 && response.status() < 400) redirects.push(response.url())
    })
    const index = await page.goto(`${ORIGINS.accounts}/privacy`)
    expect(redirects).toEqual([`${ORIGINS.accounts}/privacy`])
    expect(page.url()).toBe(`${ORIGINS.accounts}/privacy/`)
    expect(index!.status()).toBe(200)
    await expect(page.getByText("synthetic privacy index")).toBeVisible()

    const csp = headerValue(harness.models.accounts, "Content-Security-Policy")
    expect(csp).toBeTruthy()
    expect(index!.headers()["content-security-policy"]).toBe(csp)
    for (const [name, value] of harness.models.accounts.headers) {
      expect(index!.headers()[name.toLowerCase()], name).toBe(value)
    }

    const arabic = await page.goto(`${ORIGINS.accounts}/privacy/ar`)
    expect(arabic!.status()).toBe(200)
    await expect(page.getByText("synthetic ar")).toBeVisible()
    expect(arabic!.headers()["content-security-policy"]).toBe(csp)

    const unknown = await page.goto(`${ORIGINS.accounts}/privacy/xx`)
    expect(unknown!.status()).toBe(404)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
})
