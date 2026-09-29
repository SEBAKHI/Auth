import { HOSTS, expect, test } from "./fixtures"

/**
 * example.com is a real domain; the harness must never touch the network. A
 * request to a host outside the topology is refused at the proxy, recorded,
 * and fails the test at its end, naming the host and the page that asked.
 */
test("a request outside the topology is refused, recorded and fails the test", async ({
  page,
  attacker,
  egress,
}) => {
  // An attacker page has no CSP, so nothing but the proxy stands in the way.
  await page.goto(attacker.serve(HOSTS.attacker, "/", "<!doctype html><title>egress</title>"))
  const outcome = await page.evaluate(() =>
    fetch("https://not-in-topology.example.org/", { mode: "no-cors" }).then(
      () => "reached",
      (error: Error) => error.name
    )
  )
  expect(outcome).toBe("TypeError")
  expect(egress.list()).toEqual([{ target: "not-in-topology.example.org:443", kind: "CONNECT" }])
  expect(() => egress.verify()).toThrow(
    /CONNECT not-in-topology\.example\.org:443 requested by https:\/\/attacker\.example\.net\//
  )
  // Proven; clear it so this test's own end-of-test check passes.
  egress.clear()
})

test("a context or page a test builds itself, and the request fixture, go through the proxy too", async ({
  browser,
  request,
  egress,
}) => {
  const context = await browser.newContext()
  const page = await context.newPage()
  const outcome = await page.evaluate(() =>
    fetch("https://own-context.example.org/", { mode: "no-cors" }).then(
      () => "reached",
      (error: Error) => error.name
    )
  )
  expect(outcome).toBe("TypeError")
  await context.close()

  const loose = await browser.newPage()
  await loose.evaluate(() => fetch("https://own-page.example.org/", { mode: "no-cors" }).catch(() => undefined))
  await loose.close()

  expect((await request.get("https://node-side.example.org/")).status()).toBe(403)

  expect(egress.list().map((attempt) => attempt.target)).toEqual([
    "own-context.example.org:443",
    "own-page.example.org:443",
    "node-side.example.org:443",
  ])
  egress.clear()
})

test("the fixture fails a test at its end when the browser tried to leave", async ({ page, attacker }) => {
  // Expected to FAIL - in the egress fixture's teardown, not in this body.
  test.fail()
  await page.goto(attacker.serve(HOSTS.attacker, "/", "<!doctype html><title>egress</title>"))
  await page.evaluate(() => fetch("https://left-for-teardown.example.org/", { mode: "no-cors" }).catch(() => undefined))
})
