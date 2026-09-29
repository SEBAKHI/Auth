import type { Page } from "@playwright/test"

import { ORIGINS, expect, test } from "./fixtures"
import type { CspChannel, CspObserver } from "./csp-observer"

/**
 * The CSP observer's deliberate-break self-test. It breaks, on console /login
 * under the policy read from web.config, every directive enforced today -
 * script-src (inline script), img-src (cross-site image, in the top document AND
 * in a sandbox="" srcdoc frame where no script runs), connect-src (fetch to a
 * disallowed origin) - and checks each lands. style-src is NOT among them: it
 * still carries 'unsafe-inline' (both web.config files), so an inline <style> is
 * the negative control. When S02 removes 'unsafe-inline' that control flips to a
 * violation and S02 flips this assertion in the same commit.
 */

const BLOCKED = {
  topImage: `${ORIGINS.attacker}/selftest-top.png`,
  frameImage: `${ORIGINS.attacker}/selftest-sandboxed.png`,
  connect: `${ORIGINS.attacker}/selftest-connect`,
}

function channelsOf(csp: CspObserver, directive: string, blocked: string) {
  return new Set<CspChannel>(
    csp
      .violations()
      .filter((violation) => violation.directive === directive && violation.blocked === blocked)
      .map((violation) => violation.channel)
  )
}

async function openLogin(page: Page) {
  await page.goto(`${ORIGINS.console}/login`)
  await expect(page.locator("#root")).not.toBeEmpty()
}

test.beforeEach(async ({ api }) => {
  await api.useAnonymous()
})

test("every enforced directive is caught, in the top document and in a sandboxed srcdoc frame", async ({
  page,
  csp,
}) => {
  await openLogin(page)

  const inlineRan = await page.evaluate(() => {
    const script = document.createElement("script")
    script.textContent = "window.__harnessInlineRan = true"
    document.body.append(script)
    return (window as unknown as { __harnessInlineRan?: boolean }).__harnessInlineRan === true
  })
  expect(inlineRan, "the policy blocks inline script").toBe(false)

  await page.evaluate((blocked) => {
    const image = document.createElement("img")
    image.src = blocked.topImage
    document.body.append(image)
    const frame = document.createElement("iframe")
    frame.setAttribute("sandbox", "")
    frame.srcdoc = `<img src="${blocked.frameImage}">`
    document.body.append(frame)
    return fetch(blocked.connect).then(
      () => "reached",
      () => "blocked"
    )
  }, BLOCKED)
  await csp.settle()

  const all = new Set<CspChannel>(["event", "console", "audits"])
  // The top-document image proves each channel is alive on its own.
  expect(channelsOf(csp, "img-src", BLOCKED.topImage)).toEqual(all)
  expect(channelsOf(csp, "script-src", "inline").size).toBeGreaterThan(0)
  expect(channelsOf(csp, "connect-src", BLOCKED.connect).size).toBeGreaterThan(0)
  // No script runs in sandbox="", so only channels 2 and 3 can see this one.
  const framed = channelsOf(csp, "img-src", BLOCKED.frameImage)
  expect(framed.size).toBeGreaterThan(0)
  expect(framed.has("event")).toBe(false)

  const reason = "deliberate break (self-test)"
  csp.expect({ directive: "script-src", blocked: "inline", reason })
  csp.expect({ directive: "img-src", blocked: BLOCKED.topImage, reason })
  csp.expect({ directive: "img-src", blocked: BLOCKED.frameImage, reason })
  csp.expect({ directive: "connect-src", blocked: BLOCKED.connect, reason })
  expect(() => csp.verify()).not.toThrow()
})

test("the verdict fails on an undeclared violation and on a declared one that never happened", async ({
  page,
  csp,
}) => {
  await openLogin(page)

  csp.expect({
    directive: "img-src",
    blocked: `${ORIGINS.attacker}/selftest-never.png`,
    reason: "self-test: declared, never triggered",
  })
  expect(() => csp.verify()).toThrow(/declared violations that never happened/)
  csp.reset()

  await page.evaluate((src) => {
    const image = document.createElement("img")
    image.src = src
    document.body.append(image)
  }, `${ORIGINS.attacker}/selftest-undeclared.png`)
  await csp.settle()
  expect(() => csp.verify()).toThrow(
    /violations nobody declared:[\s\S]*img-src https:\/\/attacker\.example\.net\/selftest-undeclared\.png/
  )
  csp.reset()
})

test("negative control: inline style does not violate today ('unsafe-inline' is still in style-src)", async ({
  page,
  csp,
}) => {
  await openLogin(page)
  await page.evaluate((src) => {
    const style = document.createElement("style")
    style.textContent = "body { outline: 0 }"
    document.head.append(style)
    document.body.setAttribute("style", "outline: 0")
    // Floor: a real violation in the same page, so an empty style list below
    // means "allowed", not "the observer saw nothing".
    const image = document.createElement("img")
    image.src = src
    document.body.append(image)
  }, `${ORIGINS.attacker}/selftest-floor.png`)
  await csp.settle()

  expect(channelsOf(csp, "img-src", `${ORIGINS.attacker}/selftest-floor.png`).size).toBeGreaterThan(0)
  expect(csp.violations().filter((violation) => violation.directive === "style-src")).toEqual([])
  csp.expect({
    directive: "img-src",
    blocked: `${ORIGINS.attacker}/selftest-floor.png`,
    reason: "self-test floor",
  })
})
