import type { Page } from "@playwright/test"

import { HOSTS, ORIGINS, expect, test } from "./fixtures"
import type { CspChannel, CspDisposition, CspObserver } from "./csp-observer"
import { headerValue, type WebConfigModel } from "./web-config-model"

/**
 * The CSP observer's deliberate-break self-test. It breaks, on console /login
 * under the policy read from web.config, every directive enforced today -
 * script-src (inline script), img-src (cross-site image, in the top document AND
 * in a sandbox="" srcdoc frame where no script runs), connect-src (fetch to a
 * disallowed origin) - and checks each lands. style-src is NOT among them while
 * the enforced policy still carries 'unsafe-inline': an inline <style> is the
 * negative control. Card S02 ships its target policy first as a
 * Content-Security-Policy-Report-Only header (stage A), so today the control is
 * REPORTED (disposition "report") and still applied; stage B removes
 * 'unsafe-inline' from the enforced policy and flips it to "enforce".
 *
 * While that Report-Only header exists it repeats every other directive too, so
 * each deliberate break below is reported twice: once enforced, once
 * report-only. The test reads which headers the built web.config sends instead
 * of assuming, so it stays right in both stages.
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

/** The dispositions a violation of a directive both headers share is reported with. */
function dispositions(model: WebConfigModel): CspDisposition[] {
  return headerValue(model, "Content-Security-Policy-Report-Only") ? ["enforce", "report"] : ["enforce"]
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
  harness,
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
  for (const disposition of dispositions(harness.models.console)) {
    csp.expect({ directive: "script-src", blocked: "inline", disposition, reason })
    csp.expect({ directive: "img-src", blocked: BLOCKED.topImage, disposition, reason })
    csp.expect({ directive: "img-src", blocked: BLOCKED.frameImage, disposition, reason })
    csp.expect({ directive: "connect-src", blocked: BLOCKED.connect, disposition, reason })
  }
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

test("the fixture fails a test at its end on a violation it never declared", async ({ page, csp }) => {
  // Expected to FAIL - in the csp fixture's teardown, not in this body. If the
  // fixture stopped judging at the end of each test, this would pass and the
  // run would report it as an unexpected pass.
  test.fail()
  await openLogin(page)
  await page.evaluate((src) => {
    const image = document.createElement("img")
    image.src = src
    document.body.append(image)
  }, `${ORIGINS.attacker}/selftest-left-for-teardown.png`)
  await csp.settle()
  expect(channelsOf(csp, "img-src", `${ORIGINS.attacker}/selftest-left-for-teardown.png`).size).toBeGreaterThan(0)
})

test("eval is caught with its source, and the register excuses zod's source only", async ({
  page,
  csp,
  attacker,
}) => {
  // page.evaluate runs through DevTools, which CSP exempts, so eval must come
  // from a real script: a page with its own policy loading /eval.js.
  const evalScript = `${ORIGINS.attacker}/selftest-eval.js`
  attacker.serve(HOSTS.attacker, "/selftest-eval.js", 'try { new Function("") } catch (_) {}', {
    "content-type": "text/javascript; charset=utf-8",
  })
  await page.goto(
    attacker.serve(HOSTS.attacker, "/eval", '<!doctype html><script src="/selftest-eval.js"></script>', {
      "content-security-policy": "script-src 'self'",
    })
  )
  await csp.settle()

  // Chromium writes no console line for eval; the event and Audits channels
  // carry it, and both name the script.
  const evals = csp
    .violations()
    .filter((violation) => violation.directive === "script-src" && violation.blocked === "eval")
  expect(new Set(evals.map((violation) => violation.channel))).toEqual(new Set(["event", "audits"]))
  for (const violation of evals) expect(violation.source).toBe(evalScript)

  // Same directive and blocked value as the zod entry, another source: not excused.
  expect(() => csp.verify()).toThrow(/violations nobody declared:[\s\S]*script-src eval[\s\S]*selftest-eval\.js/)
  csp.reset()
})

test("negative control: an inline style is reported, not refused (S02 stage A: 'unsafe-inline' is still enforced)", async ({
  page,
  csp,
  harness,
}) => {
  await openLogin(page)
  const applied = await page.evaluate((src) => {
    const style = document.createElement("style")
    style.textContent = "body { outline: 5px solid rgb(1, 2, 3) }"
    document.head.append(style)
    document.body.setAttribute("style", "outline-offset: 7px")
    // Floor: a real violation in the same page, so the style verdict below is
    // a reading of a working observer, not of one that saw nothing.
    const image = document.createElement("img")
    image.src = src
    document.body.append(image)
    const computed = getComputedStyle(document.body)
    // outline-style, not outline-width: Chromium reports the initial width
    // (3px) even while the style is none.
    return { style: computed.outlineStyle, offset: computed.outlineOffset }
  }, `${ORIGINS.attacker}/selftest-floor.png`)
  await csp.settle()

  expect(channelsOf(csp, "img-src", `${ORIGINS.attacker}/selftest-floor.png`).size).toBeGreaterThan(0)
  const styles = csp.violations().filter((violation) => violation.directive === "style-src")
  // Both the <style> element and the style attribute are reported by the
  // Report-Only header, on every channel, and refused by none: the enforced
  // policy still allows them, so they applied.
  expect(new Set(styles.map((violation) => violation.channel))).toEqual(new Set(["event", "console", "audits"]))
  expect(styles.filter((violation) => violation.disposition !== "report")).toEqual([])
  expect(applied).toEqual({ style: "solid", offset: "7px" })

  csp.expect({
    directive: "style-src-elem",
    blocked: "inline",
    disposition: "report",
    reason: "S02 stage A: the Report-Only target policy refuses inline styles",
  })
  for (const disposition of dispositions(harness.models.console)) {
    csp.expect({
      directive: "img-src",
      blocked: `${ORIGINS.attacker}/selftest-floor.png`,
      disposition,
      reason: "self-test floor",
    })
  }
})
