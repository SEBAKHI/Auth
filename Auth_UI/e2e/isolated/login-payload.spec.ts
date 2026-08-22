import { expect, test } from "@playwright/test"

/**
 * What a person downloads before they have signed in.
 *
 * This exists because chunking here has gone wrong before in exactly one
 * direction: an earlier attempt to group vendors swept transitive dependencies
 * in with their parents, and because some of those are also used by eager code
 * the whole group became a static dependency of the entry - so the editor and
 * the charting library ended up preloaded on the login screen, the opposite of
 * the intent. Nothing caught it.
 *
 * So this measures the real thing: the bytes the browser actually fetches for
 * `/login`, with no session and no API. A budget alone would be noisy, so the
 * assertion that carries the meaning is the second one - that the heavy
 * route-only libraries are absent by name.
 */

/** Libraries that belong to one screen each and must never be on this path. */
const ROUTE_ONLY = [
  { name: "the source editor (CodeMirror)", pattern: /\/(lib|codemirror-view)-/ },
  { name: "the charting library (recharts)", pattern: /\/stat-tile-/ },
  // The signed-in interface itself: sidebar, command palette, and the menus
  // that belong to them. Reachable only behind a session, so it has no business
  // on this screen.
  { name: "the authenticated shell", pattern: /\/app-shell-/ },
]

/**
 * Headroom over the measured payload, not a target. Raise it deliberately and
 * say why; a change that needs it raised is a change worth a second look.
 */
const JS_BUDGET_BYTES = 850_000 // measured 789,175; ~8% headroom

test("the login screen downloads only what it needs", async ({ page }) => {
  // The bodies, not the headers: this preview server sends no content-length,
  // so a header-based total reads about a kilobyte for a megabyte of
  // JavaScript - a budget that could never fail.
  const bodies: Array<Promise<{ url: string; bytes: number }>> = []

  page.on("response", (response) => {
    const url = response.url()
    if (!url.endsWith(".js")) return
    bodies.push(
      response
        .body()
        .then((buffer) => ({ url, bytes: buffer.length }))
        .catch(() => ({ url, bytes: 0 }))
    )
  })

  await page.goto("/login")
  await expect(page.getByRole("button", { name: /sign in/i })).toBeVisible()

  const fetched = await Promise.all(bodies)
  const total = fetched.reduce((sum, item) => sum + item.bytes, 0)
  const names = fetched.map((item) => item.url)

  // A measurement that reads near zero is a broken measurement, not a light page.
  expect(total, "no JavaScript was measured at all").toBeGreaterThan(500_000)

  for (const library of ROUTE_ONLY) {
    const leaked = names.filter((url) => library.pattern.test(url))
    expect(
      leaked,
      `${library.name} was fetched before sign-in: ${leaked.join(", ")}`
    ).toEqual([])
  }

  expect(
    total,
    `login JavaScript grew to ${total} bytes across ${fetched.length} files`
  ).toBeLessThan(JS_BUDGET_BYTES)
})
