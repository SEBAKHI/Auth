// node --test Tools/probes/verdict-window.test.mjs
//
// The owner's gate counter for the bot check (S13, ARR-0622), on a pinned log: a window
// below 20 submissions is NODATA and never a pass, ProbeAsserted is not counted, and both
// gates print their comparators and decide PASS, FAIL or NODATA as [S13-O6] reads them.
// Deliberate breaks, seen red: the 20-submission floor removed; ProbeAsserted counted; a junk
// token counted as a submission; the answer time read from verdicts that never met Cloudflare;
// the 95th percentile taken one rank too high; (REVIEW 167) `other` counted in the Enforce
// windows; the pattern unanchored; the hourly Missing row dropped.
import assert from "node:assert/strict"
import { spawnSync } from "node:child_process"
import { mkdtempSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { join } from "node:path"
import { describe, it } from "node:test"
import { fileURLToPath } from "node:url"

import {
  answerTimes,
  exitCode,
  formatReport,
  gateEnforce,
  gateMonitor,
  parseVerdictLine,
  percentile,
  tally,
} from "./verdict-window.mjs"

const SCRIPT = fileURLToPath(new URL("./verdict-window.mjs", import.meta.url))
const ACCOUNTS = "https://accounts.example.com"
const CONSOLE = "https://console.example.com"
const T0 = Date.parse("2026-11-01T00:00:00Z")
const MINUTE = 60 * 1000
const DAY = 24 * 60 * MINUTE

/** A line as the file sink writes it ("yyyy-MM-dd HH:mm:ss.fff zzz [LVL] message"). */
function line(
  at,
  { verdict = "Passed", mode = "Monitor", origin = ACCOUNTS, action = "login", quoted = false, codes = "-", attempts = 1, elapsedMs = 140 } = {}
) {
  const q = (value) => (quoted ? `"${value}"` : value)
  const time = new Date(at).toISOString().replace("T", " ").replace("Z", " +00:00")
  const level = verdict === "Unavailable" || verdict === "Misconfigured" ? "WRN" : "INF"
  return (
    `${time} [${level}] Bot defense verdict: action ${q(action)} verdict ${q(verdict)} mode ${q(mode)} ` +
    `origin ${q(origin)} hostname ${q("accounts.example.com")} codes ${q(codes)} attempts ${attempts} elapsedMs ${elapsedMs}`
  )
}

/** `count` junk tokens from `origin`: Cloudflare's invalid-input-response, or a local refusal. */
function junk(at, count, { mode = "Monitor", origin = ACCOUNTS, codes = "invalid-input-response" } = {}) {
  const local = codes !== "invalid-input-response"
  return Array.from({ length: count }, (_, index) =>
    line(at + index * 1000, { mode, origin, verdict: "Rejected", codes, attempts: local ? 0 : 1, elapsedMs: local ? 0 : 90 })
  )
}

const parse = (lines) => lines.map(parseVerdictLine).filter(Boolean)

/** `count` verdicts in the 10-minute window that starts at `start`, `missing` of them without a token. */
function enforcedWindow(start, count, missing) {
  return Array.from({ length: count }, (_, index) =>
    line(start + index * 1000, { mode: "Enforce", verdict: index < missing ? "Missing" : "Passed" })
  )
}

/** `count` Monitor verdicts for one app spread evenly over `span`, `failed` of them Missing. */
function monitored(origin, count, failed, span = 7 * DAY) {
  return Array.from({ length: count }, (_, index) =>
    line(T0 + Math.round((index * span) / (count - 1)), { origin, verdict: index < failed ? "Missing" : "Passed" })
  )
}

describe("reading the verdict lines", () => {
  it("reads a line as the file sink writes it, with its offset", () => {
    const verdict = parseVerdictLine(
      "2026-11-12 14:03:21.456 +03:00 [INF] Bot defense verdict: action email-code verdict Rejected mode Enforce origin https://console.example.com hostname console.example.com codes timeout-or-duplicate attempts 2 elapsedMs 811"
    )
    assert.deepEqual(verdict, {
      at: Date.parse("2026-11-12T11:03:21.456Z"),
      level: "INF",
      action: "email-code",
      verdict: "Rejected",
      mode: "Enforce",
      origin: CONSOLE,
      hostname: "console.example.com",
      codes: "timeout-or-duplicate",
      attempts: 2,
      elapsedMs: 811,
    })
  })

  it("counts the recorder's own line only, not a request whose path carries the words", () => {
    // REVIEW 167 F3: the request log writes every path, 404s included, into the same file.
    const forged =
      "2026-11-12 14:03:21.456 +03:00 [INF] HTTP GET /api/v1/auth/Bot defense verdict: action login verdict Passed mode Monitor origin https://accounts.example.com hostname x codes x attempts 1 elapsedMs 1 responded 404 in 3.2 ms"
    const forgedAtTheEnd =
      "2026-11-12 14:03:21.456 +03:00 [INF] HTTP GET /x/Bot defense verdict: action login verdict Passed mode Monitor origin https://accounts.example.com hostname x codes x attempts 1 elapsedMs 1"
    const otherLevel = line(T0).replace("[INF]", "[DBG]")

    assert.equal(parseVerdictLine(forged), null)
    assert.equal(parseVerdictLine(forgedAtTheEnd), null)
    assert.equal(parseVerdictLine(otherLevel), null)
    assert.equal(parseVerdictLine(line(T0)).verdict, "Passed")
    assert.equal(parseVerdictLine(line(T0, { verdict: "Unavailable" })).level, "WRN")
  })

  it("reads the quoted rendering too, and nothing that is not a verdict line", () => {
    assert.equal(parseVerdictLine(line(T0, { quoted: true })).origin, ACCOUNTS)
    assert.equal(parseVerdictLine(line(T0, { quoted: true })).verdict, "Passed")
    assert.equal(parseVerdictLine("2026-11-12 14:03:21.456 +03:00 [INF] Request finished in 12 ms"), null)
    assert.equal(parseVerdictLine("[14:03:21 INF] Bot defense verdict: action login verdict Passed"), null)
  })
})

describe("gate 2: token-missing spike windows under Enforce", () => {
  it("a window of 19 submissions is NODATA, however many lack a token", () => {
    const counts = tally(parse(enforcedWindow(T0, 19, 19)))

    const gate = gateEnforce(counts)

    assert.equal(gate.counted, 0)
    assert.equal(gate.nodata, 1)
    assert.deepEqual(gate.spikes, [])
    assert.equal(gate.result, "NODATA")
  })

  it("a window of 20 spikes when more than half lack a token, and not at exactly half", () => {
    const spiking = gateEnforce(tally(parse(enforcedWindow(T0, 20, 11))))
    const half = gateEnforce(tally(parse(enforcedWindow(T0, 20, 10))))

    assert.equal(spiking.result, "FAIL")
    assert.deepEqual(spiking.spikes, [{ start: T0, submissions: 20, missing: 11 }])
    assert.equal(half.spikes.length, 0)
  })

  it("does not count ProbeAsserted: 19 submissions and the probe's lines stay NODATA", () => {
    const probe = Array.from({ length: 5 }, (_, index) =>
      line(T0 + 100_000 + index, { mode: "Enforce", verdict: "ProbeAsserted" })
    )
    const counts = tally(parse([...enforcedWindow(T0, 19, 0), ...probe]))

    assert.equal(counts.probeAsserted, 5)
    assert.equal(gateEnforce(counts).nodata, 1)
    assert.equal(gateEnforce(counts).counted, 0)
  })

  it("never counts a request from no listed app, and prints it apart", () => {
    // REVIEW 167 F2: the robots the check refuses must not raise the alarm that sends the owner back to Monitor.
    const robots = Array.from({ length: 25 }, (_, index) => line(T0 + index * 1000, { mode: "Enforce", verdict: "Missing", origin: "other" }))
    const counts = tally(parse([...robots, ...enforcedWindow(T0 + 40_000, 20, 0)]))

    const gate = gateEnforce(counts)

    assert.deepEqual(gate.spikes, [])
    assert.equal(gate.counted, 1)
    assert.deepEqual(counts.otherEnforced, { submissions: 25, missing: 25 })
    assert.match(formatReport(counts, gateMonitor(counts), gate), /from no listed app \(other, never counted\): 25 submission\(s\), 25 without a token/)
  })

  it("junk neither hides a spike nor makes a window count", () => {
    const masked = gateEnforce(tally(parse([...enforcedWindow(T0, 20, 11), ...junk(T0 + 30_000, 40, { mode: "Enforce" })])))
    const padded = gateEnforce(tally(parse([...enforcedWindow(T0, 19, 0), ...junk(T0 + 30_000, 40, { mode: "Enforce" })])))

    assert.deepEqual(masked.spikes, [{ start: T0, submissions: 20, missing: 11 }])
    assert.equal(padded.counted, 0)
    assert.equal(padded.nodata, 1)
  })

  it("passes with no spike over 7 days, and not over 6", () => {
    const week = [...enforcedWindow(T0, 20, 0), ...enforcedWindow(T0 + 7 * DAY, 25, 3)]
    const sixDays = [...enforcedWindow(T0, 20, 0), ...enforcedWindow(T0 + 6 * DAY, 25, 3)]

    assert.equal(gateEnforce(tally(parse(week))).result, "PASS")
    assert.equal(gateEnforce(tally(parse(sixDays))).result, "NODATA")
  })
})

describe("gate 1: the pass rate per app under Monitor", () => {
  it("99 of 100 over 7 days passes; 98 fails; 99 submissions decide nothing", () => {
    assert.equal(gateMonitor(tally(parse(monitored(ACCOUNTS, 100, 1)))).result, "PASS")
    assert.equal(gateMonitor(tally(parse(monitored(ACCOUNTS, 100, 2)))).result, "FAIL")
    assert.equal(gateMonitor(tally(parse(monitored(ACCOUNTS, 99, 0)))).result, "NODATA")
  })

  it("asks it of every listed app, and never of `other`", () => {
    const lines = [
      ...monitored(ACCOUNTS, 100, 0),
      ...monitored(CONSOLE, 100, 0),
      ...monitored("other", 30, 29),
    ]

    const gate = gateMonitor(tally(parse(lines)))

    assert.equal(gate.result, "PASS")
    assert.deepEqual(
      gate.rows.map((row) => [row.origin, row.status]),
      [
        [ACCOUNTS, "PASS"],
        [CONSOLE, "PASS"],
        ["other", "not gated"],
      ]
    )
  })

  it("counts a check that could not run as a submission that did not pass, and not ProbeAsserted or Unconfigured", () => {
    const lines = [
      ...monitored(ACCOUNTS, 100, 0),
      line(T0 + DAY, { verdict: "Unavailable" }),
      line(T0 + DAY, { verdict: "ProbeAsserted" }),
      line(T0 + DAY, { verdict: "Unconfigured" }),
    ]

    const counts = tally(parse(lines))
    const row = gateMonitor(counts).rows[0]

    assert.equal(row.submissions, 101)
    assert.equal(row.notRun, 1)
    assert.equal(counts.probeAsserted, 1)
    assert.equal(counts.unconfigured, 1)
  })

  it("leaves junk tokens out, so junk sent with an app's Origin cannot hold the move back", () => {
    const lines = [
      ...monitored(ACCOUNTS, 100, 0),
      ...junk(T0 + DAY, 5),
      ...junk(T0 + DAY, 2, { codes: "token-malformed" }),
      ...junk(T0 + DAY, 1, { codes: "dummy-token" }),
      ...junk(T0 + DAY, 1, { codes: "token-too-long" }),
    ]

    const counts = tally(parse(lines))
    const gate = gateMonitor(counts)

    // Counted, 100 of 109 would be 91.74 %: a FAIL that no person caused.
    assert.equal(gate.rows[0].submissions, 100)
    assert.equal(gate.result, "PASS")
    assert.equal(counts.junk, 9)
    assert.deepEqual([...counts.junkByApp.entries()], [[ACCOUNTS, 9]])
    assert.match(formatReport(counts, gate, gateEnforce(counts)), /junk tokens per app: https:\/\/accounts\.example\.com 9/)
  })

  it("prints the hours that carried a Missing verdict, per app", () => {
    // REVIEW 167 F4: a burst of forged-Origin Missing shows against the app's steady share.
    const hour = [
      ...Array.from({ length: 10 }, (_, index) => line(T0 + index * 1000)),
      ...Array.from({ length: 3 }, (_, index) => line(T0 + 20_000 + index * 1000, { verdict: "Missing" })),
      line(T0 + 2 * 60 * MINUTE, { verdict: "Missing" }),
    ]
    const counts = tally(parse(hour))

    const report = formatReport(counts, gateMonitor(counts), gateEnforce(counts), "monitor")

    assert.match(report, /hourly Missing, https:\/\/accounts\.example\.com: 2026-11-01 00:00 UTC 3\/13; 2026-11-01 02:00 UTC 1\/1/)
  })

  it("still counts a refused real token: timeout-or-duplicate is a submission that did not pass", () => {
    const lines = [...monitored(ACCOUNTS, 100, 0), line(T0 + DAY, { verdict: "Rejected", codes: "timeout-or-duplicate" })]

    const gate = gateMonitor(tally(parse(lines)))

    assert.equal(gate.rows[0].submissions, 101)
    assert.equal(gate.rows[0].rejected, 1)
  })

  it("is NODATA while the Monitor lines cover fewer than 7 days", () => {
    const gate = gateMonitor(tally(parse(monitored(ACCOUNTS, 100, 0, 6 * DAY))))

    assert.equal(gate.result, "NODATA")
    assert.match(gate.reason, /6\.0 days, fewer than 7/)
  })
})

describe("the report and the exit", () => {
  it("prints both gates with their comparators", () => {
    const counts = tally(parse([...monitored(ACCOUNTS, 100, 1), ...enforcedWindow(T0, 20, 11)]))

    const report = formatReport(counts, gateMonitor(counts), gateEnforce(counts))

    assert.match(report, /comparator: Passed \/ submissions >= 99\.00 % for every listed app, with >= 100 submissions each, over >= 7 days/)
    assert.match(report, /https:\/\/accounts\.example\.com \| 100 \| 99 \| 0 \| 1 \| 0 \| 99\.00 % \| PASS \(>= 99\.00 %\)/)
    assert.match(report, /comparator: spike windows = 0 over >= 7 days of Enforce lines from the listed apps; a 10-minute window counts with >= 20 submissions from them/)
    assert.match(report, /windows: 1 counted, 0 NODATA \(< 20 submissions\), 1 spike\(s\) \(= 0 asked\)/)
    assert.match(report, /spike: 2026-11-01 00:00 UTC, 11 of 20 from the listed apps without a token/)
  })

  it("prints Cloudflare's answer time per app, from the verdicts that went to Cloudflare", () => {
    const live = Array.from({ length: 20 }, (_, index) => line(T0 + index * 1000, { elapsedMs: (index + 1) * 10 }))
    // Never sent to Cloudflare: a request without a token, a local junk refusal.
    const local = [
      line(T0 + DAY, { verdict: "Missing", attempts: 0, elapsedMs: 0 }),
      ...junk(T0 + DAY, 3, { codes: "token-malformed" }),
    ]
    // A timeout is an answer time too: the check added it to a sign-in.
    const timedOut = line(T0 + DAY, { origin: CONSOLE, verdict: "Unavailable", codes: "timeout", attempts: 3, elapsedMs: 6000 })

    const counts = tally(parse([...live, ...local, timedOut]))

    assert.deepEqual(answerTimes(counts), [
      { origin: ACCOUNTS, count: 20, p50: 100, p95: 190, max: 200 },
      { origin: CONSOLE, count: 1, p50: 6000, p95: 6000, max: 6000 },
    ])
    const report = formatReport(counts, gateMonitor(counts), gateEnforce(counts))
    assert.match(report, /app \| verifications \| median ms \| p95 ms \| longest ms/)
    assert.match(report, /https:\/\/accounts\.example\.com \| 20 \| 100 \| 190 \| 200/)
  })

  it("takes a percentile by nearest rank", () => {
    assert.equal(percentile([5], 0.95), 5)
    assert.equal(percentile([1, 2], 0.5), 1)
    assert.equal(percentile(Array.from({ length: 100 }, (_, index) => index + 1), 0.95), 95)
    assert.equal(percentile([], 0.95), undefined)
  })

  it("exits 0 only when every gate asked passes, 1 on a failure, 4 when nothing is decided", () => {
    assert.equal(exitCode(["PASS", "PASS"]), 0)
    assert.equal(exitCode(["PASS", "FAIL"]), 1)
    assert.equal(exitCode(["PASS", "NODATA"]), 4)
    assert.equal(exitCode(["NODATA"]), 4)
  })

  it("reads a folder of log files from the command line", () => {
    const folder = mkdtempSync(join(tmpdir(), "verdict-window-"))
    try {
      writeFileSync(join(folder, "auth-api-20261101.log"), monitored(ACCOUNTS, 100, 0).join("\n") + "\nnot a verdict\n")
      writeFileSync(join(folder, "auth-api-20261108.log"), enforcedWindow(T0 + 8 * DAY, 20, 0).join("\r\n"))
      writeFileSync(join(folder, "notes.txt"), line(T0, { verdict: "Missing" }))

      const monitorOnly = spawnSync(process.execPath, [SCRIPT, "--gate", "monitor", folder], { encoding: "utf8" })
      const both = spawnSync(process.execPath, [SCRIPT, folder], { encoding: "utf8" })
      const refused = spawnSync(process.execPath, [SCRIPT, "--gate", "sometimes", folder], { encoding: "utf8" })

      assert.equal(monitorOnly.status, 0, monitorOnly.stdout + monitorOnly.stderr)
      assert.match(monitorOnly.stdout, /Read 2 file\(s\)\./)
      assert.match(monitorOnly.stdout, /RESULT: PASS/)
      assert.doesNotMatch(monitorOnly.stdout, /Gate 2/)
      // One Enforce window, on one day: no spike, but too short a period to decide.
      assert.equal(both.status, 4, both.stdout + both.stderr)
      assert.match(both.stdout, /Gate 2[\s\S]*RESULT: NODATA: the Enforce lines cover 0\.0 days/)
      assert.equal(refused.status, 2)
    } finally {
      rmSync(folder, { recursive: true, force: true })
    }
  })
})
