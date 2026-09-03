import type { Page, Route } from "@playwright/test"

function accessToken() {
  const encode = (value: object) =>
    Buffer.from(JSON.stringify(value)).toString("base64url")
  return `${encode({ alg: "none", typ: "JWT" })}.${encode({
    exp: Math.floor(Date.now() / 1000) + 3600,
  })}.signature`
}

export async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  })
}

export async function installAuthenticatedApi(
  page: Page,
  permissions: string[],
  handle: (route: Route, url: URL) => Promise<boolean>,
  options?: { preferredLanguage?: string }
) {
  await page.addInitScript(() => {
    localStorage.setItem("auth.refreshToken", "isolated-refresh")
  })

  await page.route("**/api/v1/**", async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname.toLowerCase() === "/api/v1/auth/refresh") {
      await fulfillJson(route, {
        accessToken: accessToken(),
        refreshToken: "rotated-isolated-refresh",
      })
      return
    }
    if (url.pathname.toLowerCase() === "/api/v1/auth/me") {
      await fulfillJson(route, {
        id: "99999999-9999-9999-9999-999999999999",
        email: "isolated@example.test",
        firstName: "Isolated",
        lastName: "Operator",
        preferredLanguage: options?.preferredLanguage ?? "en",
        timeZone: "UTC",
        roles: [],
        permissions,
      })
      return
    }
    if (url.pathname.toLowerCase() === "/api/v1/platform/branding") {
      await fulfillJson(route, { platformName: "AuthSystem" })
      return
    }
    if (url.pathname.toLowerCase() === "/api/v1/platform/password-policy") {
      await fulfillJson(route, {
        minimumLength: 8,
        requireUppercase: true,
        requireLowercase: true,
        requireDigit: true,
        requireSpecialCharacter: true,
      })
      return
    }
    if (await handle(route, url)) return

    await fulfillJson(route, { title: "Unexpected isolated API request" }, 404)
  })
}
