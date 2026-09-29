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
