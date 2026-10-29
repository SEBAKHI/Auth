import { expect, type Frame, type Page, test } from "@playwright/test"

import { fulfillJson, installAnonymousApi } from "./mock-anonymous-api"

/**
 * The panel half of the sign-in pages (phase 6): an application's design, else the
 * platform's, else the plain panel; shown in a sandboxed frame whose links open a new
 * tab, whose unset colours follow the live appearance and mode, and which cannot reach
 * past its own box. Measured in the built app, in a real browser: none of this is
 * visible to a unit test.
 *
 * Deliberate breaks, each turning cases red: drop target="_blank" from the receiver; drop
 * the base style; send the platform's design on an application's page; render the panel
 * under lg; drop `loginDesign` from the organization step; put the signed-out page back on
 * its own branding call.
 */

const AUTHORIZE = "https://auth.example.com/api/v1/auth/authorize?client_id=app&state=xyz"
const FLOW = `/login?returnTo=${encodeURIComponent(AUTHORIZE)}`
/** The organization step of the same application's flow, named by its stored request. */
const SETUP = `/create-organization?client_id=app&auth_request=${"0123456789abcdef".repeat(4)}`
const APP_LOGO = "https://auth.example.com/uploads/images/partner.svg"

const PLATFORM_DESIGN = { id: "p1", contentHash: "a".repeat(64), showPlatformLogo: true }
const OWN_DESIGN = { id: "a1", contentHash: "b".repeat(64), showPlatformLogo: false }

/** A design that sets nothing of its own: background, colour and font follow the appearance. */
const PLAIN_HTML = `<main id="design"><h2 id="headline">Welcome</h2><p lang="ar">مرحبًا</p>
<a id="link" href="https://partner.example/help">Help</a></main>`

interface Setup {
  language?: "en" | "ar"
  width?: number
  platform?: typeof PLATFORM_DESIGN | null
  /** What the application's public branding names (its own design, else the platform's). */
  app?: typeof PLATFORM_DESIGN | null
  appHtml?: string
  platformHtml?: string
  renderStatus?: number
}

async function open(page: Page, path: string, setup: Setup = {}) {
  const renders: string[] = []
  // The page only: init scripts run in every frame, and the design's frame has no storage.
  await page.addInitScript((lang) => {
    if (window.top === window) localStorage.setItem("auth.language", lang)
  }, setup.language ?? "en")
  await installAnonymousApi(page, async (route, url) => {
    const path = url.pathname.toLowerCase()
    if (path === "/api/v1/applications/app/public-branding") {
      await fulfillJson(route, {
        name: "Partner App",
        logoUrl: APP_LOGO,
        logoUrlDark: null,
        homeUrl: null,
        loginDesign: setup.app === undefined ? PLATFORM_DESIGN : setup.app,
      })
      return true
    }
    if (path === "/api/v1/applications/app/login-design" || path === "/api/v1/platform/login-design") {
      renders.push(`${path}?${url.searchParams.toString()}`)
      if (setup.renderStatus) {
        await fulfillJson(route, { title: "broken" }, setup.renderStatus)
        return true
      }
      const own = path.includes("applications")
      const html = own ? (setup.appHtml ?? PLAIN_HTML) : (setup.platformHtml ?? PLAIN_HTML)
      const language = url.searchParams.get("lang") ?? "en"
      await fulfillJson(route, {
        id: own ? (setup.app?.id ?? "x") : PLATFORM_DESIGN.id,
        contentHash: "c".repeat(64),
        language,
        dir: language === "ar" ? "rtl" : "ltr",
        html,
      })
      return true
    }
    // The organization step, open: a signed-in person who may create one.
    if (path === "/api/v1/auth/organization-setup") {
      await fulfillJson(route, {
        canCreate: true,
        limit: 1,
        ownedOrganizations: [],
        email: "customer@example.test",
        emailConfirmed: true,
      })
      return true
    }
    return false
  })
  await page.route("**/api/v1/Platform/branding", (route) =>
    fulfillJson(route, {
      platformName: "AuthSystem",
      logoUrl: null,
      staySignedInOffered: true,
      loginDesign: setup.platform === undefined ? PLATFORM_DESIGN : setup.platform,
    })
  )
  await page.route("https://auth.example.com/uploads/images/*.svg", (route) =>
    route.fulfill({
      contentType: "image/svg+xml",
      body: `<svg xmlns="http://www.w3.org/2000/svg" width="200" height="64"><rect width="200" height="64" fill="#777"/></svg>`,
    })
  )
  // On the context: a link opens a new page, which the page's own routes do not reach.
  await page.context().route("https://partner.example/**", (route) =>
    route.fulfill({ contentType: "text/html", body: "<title>Partner help</title><p>help</p>" })
  )
  await page.setViewportSize({ width: setup.width ?? 1440, height: 900 })
  await page.goto(path)
  return { renders }
}

const panel = (page: Page) => page.locator('[data-slot="auth-panel"]')

/** The design's frame, once it has rendered. */
async function designFrame(page: Page): Promise<Frame> {
  await expect(panel(page)).toHaveAttribute("data-design-state", "rendered")
  const handle = await panel(page).locator("iframe").elementHandle()
  const frame = await handle!.contentFrame()
  expect(frame).not.toBeNull()
  return frame!
}

test("an application's page shows the design its branding names, rendered for the page's language", async ({ page }) => {
  const { renders } = await open(page, FLOW, {
    language: "ar",
    app: OWN_DESIGN,
    appHtml: `<p id="who">{{ own }}</p>`.replace("{{ own }}", "Own design"),
  })
  const frame = await designFrame(page)

  await expect(frame.locator("#who")).toHaveText("Own design")
  expect(renders).toEqual(["/api/v1/applications/app/login-design?lang=ar"])
  // The panel counts for assistive technology once the design shows, and is named.
  await expect(panel(page)).not.toHaveAttribute("aria-hidden", "true")
  await expect(panel(page).locator("iframe")).toHaveAttribute("title", "قالب صفحة الدخول")
  expect(await frame.evaluate(() => [document.documentElement.lang, document.documentElement.dir])).toEqual(["ar", "rtl"])
})

test("an application without a design of its own shows the platform's, through its own render", async ({ page }) => {
  const { renders } = await open(page, FLOW, { app: PLATFORM_DESIGN, appHtml: `<p id="who">Platform design for Partner App</p>` })
  const frame = await designFrame(page)
  await expect(frame.locator("#who")).toHaveText("Platform design for Partner App")
  expect(renders).toEqual(["/api/v1/applications/app/login-design?lang=en"])
})

test("the platform's own pages show the platform's design", async ({ page }) => {
  const { renders } = await open(page, "/login", { platformHtml: `<p id="who">Platform design</p>` })
  const frame = await designFrame(page)
  await expect(frame.locator("#who")).toHaveText("Platform design")
  expect(renders).toEqual(["/api/v1/platform/login-design?lang=en"])
})

test("with no design anywhere the panel stays plain, and nothing is fetched", async ({ page }) => {
  const { renders } = await open(page, FLOW, { app: null, platform: null })
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible()
  await expect(panel(page).locator("iframe")).toHaveCount(0)
  await expect(panel(page)).toHaveAttribute("aria-hidden", "true")
  expect(renders).toEqual([])
})

test("under 1024px the panel is hidden and no design is fetched", async ({ page }) => {
  const { renders } = await open(page, FLOW, { width: 1023 })
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible()
  await page.waitForTimeout(500)
  await expect(panel(page).locator("iframe")).toHaveCount(0)
  expect(renders).toEqual([])
})

test("links open a new tab, and the sign-in page stays where it is", async ({ page, context }) => {
  await open(page, FLOW)
  const frame = await designFrame(page)
  const link = frame.locator("#link")
  await expect(link).toHaveAttribute("target", "_blank")
  await expect(link).toHaveAttribute("rel", /\bnoopener\b/)

  const [popup] = await Promise.all([context.waitForEvent("page"), link.click()])
  await popup.waitForURL("https://partner.example/help")
  await expect(popup).toHaveTitle("Partner help")
  expect(new URL(page.url()).pathname).toBe("/login")
})

test("a design without colours follows the appearance, in light and dark and when the mode changes", async ({ page }) => {
  await open(page, FLOW)
  const frame = await designFrame(page)

  const pageColours = () =>
    page.evaluate(() => {
      const body = getComputedStyle(document.body)
      return { background: body.backgroundColor, color: body.color }
    })
  const frameColours = () =>
    frame.evaluate(() => {
      const html = getComputedStyle(document.documentElement)
      return { background: html.backgroundColor, color: html.color, scheme: html.colorScheme }
    })

  const light = await pageColours()
  expect(await frameColours()).toEqual({ ...light, scheme: "light" })

  // The visitor switches the mode with the page's own control.
  await page.getByRole("button", { name: "Theme" }).click()
  await page.getByRole("menuitem", { name: "Dark" }).click()
  await expect.poll(() => page.evaluate(() => document.documentElement.classList.contains("dark"))).toBe(true)
  const dark = await pageColours()
  expect(dark.background).not.toBe(light.background)
  await expect.poll(frameColours).toEqual({ ...dark, scheme: "dark" })
})

test("the platform's fonts reach the design", async ({ page }) => {
  await open(page, FLOW)
  const frame = await designFrame(page)
  const fonts = await frame.evaluate(async () => {
    await document.fonts.ready
    return {
      family: getComputedStyle(document.documentElement).fontFamily,
      latin: document.fonts.check("16px 'IBM Plex Sans Variable'", "Welcome"),
      arabic: document.fonts.check("16px 'IBM Plex Sans Arabic'", "مرحبًا"),
      loaded: [...document.fonts].filter((face) => face.status === "loaded").map((face) => face.family),
    }
  })
  expect(fonts.family).toContain("IBM Plex Sans")
  expect(fonts.latin).toBe(true)
  expect(fonts.arabic).toBe(true)
  expect(fonts.loaded.join("|")).toMatch(/IBM Plex Sans Variable/)
  expect(fonts.loaded.join("|")).toMatch(/IBM Plex Sans Arabic/)
})

test("a design that hides the platform's mark gives the corner to the application's logo", async ({ page }) => {
  await open(page, FLOW, { app: OWN_DESIGN })
  await expect(page.locator("header").getByRole("img", { name: "Partner App" })).toBeVisible()
  await expect(page.locator("main").getByRole("img", { name: "Partner App" })).toHaveCount(0)
  // "Secured by" still names the platform.
  await expect(page.getByText("Secured by AuthSystem")).toBeVisible()
})

// The organization step has a header of its own rather than the flow's: it must still take the
// application's design and corner, or the panel and the corner change on the flow's last step.
for (const language of ["en", "ar"] as const) {
  test(`${language}: the organization step of an application's flow shows that application's design and corner`, async ({
    page,
  }) => {
    const { renders } = await open(page, SETUP, {
      language,
      app: OWN_DESIGN,
      appHtml: `<p id="who">Own design</p>`,
    })
    const frame = await designFrame(page)

    await expect(frame.locator("#who")).toHaveText("Own design")
    expect(renders).toEqual([`/api/v1/applications/app/login-design?lang=${language}`])
    await expect(page.locator("header").getByRole("img", { name: "Partner App" })).toBeVisible()
    await expect(page.locator("main").getByRole("img", { name: "Partner App" })).toHaveCount(0)
  })
}

// The pages that name an application by the client id of their own address, with no request
// to resume, take its design and corner from the same hook (owner, 2026-10-29): they used to
// fetch the name and logo themselves and show the platform's design.
const NAMED_PAGES = ["/logout", "/signed-out", "/sign-in-expired"] as const
const SECURED_BY = { en: "Secured by AuthSystem", ar: "محمي بواسطة AuthSystem" }

for (const language of ["en", "ar"] as const) {
  for (const path of NAMED_PAGES) {
    test(`${language}: ${path} of an application shows that application's design, corner and trust marker`, async ({
      page,
    }) => {
      const { renders } = await open(page, `${path}?client_id=app`, {
        language,
        app: OWN_DESIGN,
        appHtml: `<p id="who">Own design</p>`,
      })
      const frame = await designFrame(page)

      await expect(frame.locator("#who")).toHaveText("Own design")
      expect(renders).toEqual([`/api/v1/applications/app/login-design?lang=${language}`])
      await expect(page.locator("header").getByRole("img", { name: "Partner App" })).toBeVisible()
      await expect(page.locator("main").getByRole("img", { name: "Partner App" })).toHaveCount(0)
      await expect(page.getByText(SECURED_BY[language])).toBeVisible()
    })
  }
}

for (const language of ["en", "ar"] as const) {
  for (const width of [375, 1440]) {
    test(`${language} at ${width}px: the sign-out pages of an application`, async ({ page }) => {
      for (const path of NAMED_PAGES) {
        await open(page, `${path}?client_id=app`, { language, width, app: OWN_DESIGN })
        // Under lg the panel is gone and the logo sits above the title: the form half alone
        // says which application this is.
        if (width >= 1024) await designFrame(page)
        await expect(page.getByRole("img", { name: "Partner App" })).toBeVisible()
        await page.screenshot({
          path: test.info().outputPath(`named-${path.slice(1)}-${language}-${width}.png`),
          fullPage: true,
        })
        const spill = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
        expect(spill, path).toBeLessThanOrEqual(0)
        await page.unrouteAll({ behavior: "wait" })
      }
    })
  }
}

test("a page naming a client id the server does not know is the platform's own, with the platform's design", async ({
  page,
}) => {
  // The catch-all answers this application's branding 404, as the API does for an unknown or
  // inactive one.
  const { renders } = await open(page, "/signed-out?client_id=unknown-app", { app: OWN_DESIGN })
  await designFrame(page)

  expect(renders).toEqual(["/api/v1/platform/login-design?lang=en"])
  await expect(page.locator("main").getByRole("img")).toHaveCount(0)
  await expect(page.getByText(/Secured by/)).toHaveCount(0)
})

test("a design cannot cover the form: a fixed, full-screen element stays inside the panel", async ({ page }) => {
  await open(page, FLOW, {
    app: OWN_DESIGN,
    appHtml: `<div id="cover" style="position:fixed;inset:0;width:100vw;height:100vh;z-index:2147483647;background:red"></div>`,
  })
  const frame = await designFrame(page)
  const cover = await frame.locator("#cover").boundingBox()
  const box = await panel(page).boundingBox()
  expect(cover!.width).toBeGreaterThan(100)
  expect(cover!.x).toBeGreaterThanOrEqual(box!.x - 1)
  expect(cover!.x + cover!.width).toBeLessThanOrEqual(box!.x + box!.width + 1)

  // What sits under the form's own controls is the form, not the frame.
  const email = page.getByRole("textbox").first()
  const point = await email.boundingBox()
  const hit = await page.evaluate(
    ([x, y]) => document.elementFromPoint(x, y)?.closest("iframe") === null,
    [point!.x + point!.width / 2, point!.y + point!.height / 2]
  )
  expect(hit).toBe(true)
  await email.fill("someone@example.com")
  await expect(email).toHaveValue("someone@example.com")
})

test("a render that fails leaves the plain panel", async ({ page }) => {
  await open(page, FLOW, { renderStatus: 500 })
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible()
  await page.waitForTimeout(800)
  await expect(panel(page).locator("iframe")).toHaveCount(0)
  await expect(panel(page)).toHaveAttribute("aria-hidden", "true")
})

test("a frame that never answers is removed, and the panel stays plain", async ({ page }) => {
  await page.route("**/login-design-frame.html", (route) => route.fulfill({ status: 404, body: "gone" }))
  await open(page, FLOW)
  await expect(panel(page)).toHaveAttribute("data-design-state", "failed", { timeout: 10_000 })
  await expect(panel(page).locator("iframe")).toHaveCount(0)
  await expect(panel(page)).toHaveAttribute("aria-hidden", "true")
})

test("the frame is sandboxed exactly, and runs none of the design's scripts", async ({ page }) => {
  const errors: string[] = []
  page.on("pageerror", (error) => errors.push(error.message))
  await open(page, FLOW, {
    app: OWN_DESIGN,
    appHtml: `<p id="who">Own</p><script>document.title = "ran"</script><img src="data:," onerror="document.body.dataset.ran = 'yes'">`,
  })
  const frame = await designFrame(page)
  await expect(panel(page).locator("iframe")).toHaveAttribute(
    "sandbox",
    "allow-scripts allow-popups allow-popups-to-escape-sandbox"
  )
  // An opaque origin: the design's document reaches none of the page's storage.
  expect(await frame.evaluate(() => window.origin)).toBe("null")
  expect(await frame.evaluate(() => document.title)).not.toBe("ran")
  expect(await frame.evaluate(() => document.body.dataset.ran ?? "no")).toBe("no")
  expect(errors).toEqual([])
})

for (const language of ["en", "ar"] as const) {
  for (const width of [375, 1440]) {
    test(`${language} at ${width}px: the page with its design`, async ({ page }) => {
      await open(page, FLOW, {
        language,
        width,
        app: OWN_DESIGN,
        appHtml: `<style>body{margin:0}.p{box-sizing:border-box;min-height:100vh;display:flex;align-items:flex-end;padding:3rem;background:radial-gradient(circle at 20% 20%,color-mix(in oklch,var(--primary) 25%,transparent),transparent 60%),var(--muted)}</style><div class="p"><h2>${language === "ar" ? "أهلًا بك في Partner App" : "Welcome to Partner App"}</h2></div>`,
      })
      if (width >= 1024) await designFrame(page)
      else await expect(page.getByRole("heading", { level: 1 })).toBeVisible()
      await page.screenshot({ path: test.info().outputPath(`login-design-${language}-${width}.png`), fullPage: true })
      // No horizontal spill at either width.
      const spill = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
      expect(spill).toBeLessThanOrEqual(0)
    })
  }
}

/**
 * Carried from the phase 1 review: on the logout page the application is named by its
 * logo alone; a logo that fails to load leaves its name written instead.
 */
test("logout names the application when its logo fails to load", async ({ page }) => {
  await installAnonymousApi(page, async (route, url) => {
    if (url.pathname.toLowerCase() !== "/api/v1/applications/app/public-branding") return false
    await fulfillJson(route, { name: "Partner App", logoUrl: "https://auth.example.com/uploads/images/gone.webp" })
    return true
  })
  await page.route("https://auth.example.com/uploads/images/gone.webp", (route) => route.fulfill({ status: 404 }))
  await page.setViewportSize({ width: 1024, height: 900 })
  await page.goto("/logout?client_id=app")

  await expect(page.locator("main").getByText("Partner App", { exact: true })).toBeVisible()
  await expect(page.locator("main").getByRole("img", { name: "Partner App" })).toHaveCount(0)
})
