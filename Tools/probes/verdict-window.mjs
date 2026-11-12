/**
 * The owner's two gates for the bot check (S13, [S13-O6]), counted from Auth_API's text log.
 *
 * Every bot-check verdict writes one line (BotDefenseVerdictRecorder.Template):
 *   Bot defense verdict: action {action} verdict {verdict} mode {mode} origin {origin}
 *   hostname {hostname} codes {codes} attempts {n} elapsedMs {ms}
 * at Information (Warning when the check could not run), so the log must be written at
 * Information for the whole period: Serilog:MinimumLevel:Default = Information. Until S16
 * reads these verdicts as signals, this script is how the gates are read.
 *
 * What counts. A submission is a verdict that judged a request: every verdict except
 * Unconfigured (no check ran), ProbeAsserted (S17's sign-in probe, which never meets
 * Cloudflare), and a junk token: a Rejected verdict with a code no real widget makes
 * (JUNK_TOKEN_CODES), which is counted apart and printed. Junk can be sent by anyone with
 * a listed app's Origin; counted, eleven junk tokens among a thousand sign-ins would hold
 * the move to Enforce back, and a flood of them would hide a token-missing spike. This is
 * the posture's own definition (BotDefenseVerdictCounts), so the console's
 * bot.monitor-pass-rate and this script agree. A check that could not run (Unavailable,
 * Misconfigured) is a submission that did not pass.
 *
 * Cloudflare's answer time. Every verdict that went to Cloudflare (attempts >= 1, whatever
 * its outcome, timeouts included) gives its elapsedMs; the report prints, per app, the
 * count, the median, the 95th percentile (nearest rank) and the longest. Not a gate: it is
 * how long the check adds to a sign-in.
 *
 * Gate 1, Monitor -> Enforce: bot.monitor-pass-rate per app. Over the Monitor lines,
 * for every listed app (origin), Passed / submissions >= 99 %, with at least 100
 * submissions, the lines covering at least 7 days. An app below 100 submissions is
 * NODATA: the production observation decides it (BD11). `other` (a request from no
 * listed app) is shown and never gated: bots are expected there.
 *
 * Gate 2, after Enforce: bot.token-missing-spike windows = 0. Over the Enforce lines, in
 * 10-minute windows (aligned to the clock: :00, :10, ...): a window with fewer than 20
 * submissions is NODATA and proves nothing; one with 20 or more spikes when more than
 * half carry no token (the posture's rule). Passes with no spike, at least one window
 * counted, and the lines covering at least 7 days.
 *
 * NODATA is never a pass: too few submissions, or too short a period, decides nothing.
 *
 * Input: log files, or folders whose *.log files are read (Logs/auth-api-YYYYMMDD.log).
 * A line is read with its own time and offset (the file sink's default
 * "yyyy-MM-dd HH:mm:ss.fff zzz"); a line without an offset is read as this machine's
 * local time. The count holds no token, secret or address: the lines carry none.
 *
 * Usage: node Tools/probes/verdict-window.mjs [--gate monitor|enforce|both]
 *          [--from <ISO time>] [--to <ISO time>] <file or folder>...
 * Exit:  0 PASS (every gate asked) · 1 FAIL · 4 NODATA (no decision) · 2 refused
 */
import { createReadStream, readdirSync, statSync } from "node:fs"
import { join, resolve } from "node:path"
import { createInterface } from "node:readline"
import { fileURLToPath } from "node:url"

export const WINDOW_MS = 10 * 60 * 1000
export const WINDOW_MINIMUM = 20
export const PASS_RATE_MINIMUM = 0.99
export const APP_MINIMUM = 100
export const GATE_DAYS = 7
const DAY_MS = 24 * 60 * 60 * 1000

/** Codes no real widget's token gets: TurnstileBotDefenseVerifier.JunkTokenCodes. Change both together. */
export const JUNK_TOKEN_CODES = ["invalid-input-response", "token-too-long", "token-malformed", "dummy-token"]

const isJunk = (verdict) =>
  verdict.verdict === "Rejected" && verdict.codes.split(",").some((code) => JUNK_TOKEN_CODES.includes(code))

const HEAD = /^(\d{4}-\d{2}-\d{2})[ T](\d{2}:\d{2}:\d{2})(?:\.(\d+))?\s*(Z|[+-]\d{2}:?\d{2})?\s+\[(\w{3})\]\s+(.*)$/
// Quotes are optional: the file sink renders the message with :lj (no quotes), a template
// without it quotes every string value.
const VERDICT =
  /Bot defense verdict: action "?([\w-]+)"? verdict "?(\w+)"? mode "?(\w+)"? origin "?([^"\s]+)"? hostname "?([^"\s]+)"? codes "?([^"\s]*)"? attempts (\d+) elapsedMs (\d+)/

/** One verdict line, or null for any other line. */
export function parseVerdictLine(line) {
  const head = HEAD.exec(line)
  if (!head) return null
  const body = VERDICT.exec(head[6])
  if (!body) return null
  const fraction = (head[3] ?? "0").slice(0, 3).padEnd(3, "0")
  const offset = head[4] === undefined ? "" : head[4] === "Z" ? "Z" : head[4].replace(/^([+-]\d{2})(\d{2})$/, "$1:$2")
  const at = Date.parse(`${head[1]}T${head[2]}.${fraction}${offset}`)
  if (Number.isNaN(at)) return null
  return {
    at,
    level: head[5],
    action: body[1],
    verdict: body[2],
    mode: body[3],
    origin: body[4],
    hostname: body[5],
    codes: body[6],
    attempts: Number(body[7]),
    elapsedMs: Number(body[8]),
  }
}

/** The counts both gates read, from parsed verdicts, within [from, to). */
export function tally(verdicts, { from, to } = {}) {
  const apps = new Map()
  const windows = new Map()
  const result = {
    read: 0,
    probeAsserted: 0,
    unconfigured: 0,
    junk: 0,
    junkByApp: new Map(),
    answerTimes: new Map(),
    monitor: { first: undefined, last: undefined },
    enforce: { first: undefined, last: undefined },
    apps,
    windows,
  }
  const stretch = (span, at) => {
    span.first = span.first === undefined ? at : Math.min(span.first, at)
    span.last = span.last === undefined ? at : Math.max(span.last, at)
  }
  for (const verdict of verdicts) {
    if (from !== undefined && verdict.at < from) continue
    if (to !== undefined && verdict.at >= to) continue
    result.read += 1
    if (verdict.verdict === "ProbeAsserted") {
      result.probeAsserted += 1
      continue
    }
    if (verdict.verdict === "Unconfigured") {
      result.unconfigured += 1
      continue
    }
    if (verdict.attempts >= 1) {
      const times = result.answerTimes.get(verdict.origin) ?? []
      times.push(verdict.elapsedMs)
      result.answerTimes.set(verdict.origin, times)
    }
    if (isJunk(verdict)) {
      result.junk += 1
      result.junkByApp.set(verdict.origin, (result.junkByApp.get(verdict.origin) ?? 0) + 1)
      continue
    }
    if (verdict.mode === "Monitor") {
      stretch(result.monitor, verdict.at)
      const app = apps.get(verdict.origin) ?? { submissions: 0, passed: 0, rejected: 0, missing: 0, notRun: 0 }
      app.submissions += 1
      if (verdict.verdict === "Passed") app.passed += 1
      else if (verdict.verdict === "Rejected") app.rejected += 1
      else if (verdict.verdict === "Missing") app.missing += 1
      else app.notRun += 1
      apps.set(verdict.origin, app)
    } else if (verdict.mode === "Enforce") {
      stretch(result.enforce, verdict.at)
      const start = Math.floor(verdict.at / WINDOW_MS) * WINDOW_MS
      const window = windows.get(start) ?? { submissions: 0, missing: 0 }
      window.submissions += 1
      if (verdict.verdict === "Missing") window.missing += 1
      windows.set(start, window)
    }
  }
  return result
}

const days = (span) => (span.first === undefined ? 0 : (span.last - span.first) / DAY_MS)

/** Gate 1: the pass rate of every listed app under Monitor. */
export function gateMonitor(counts) {
  const rows = [...counts.apps.entries()]
    .sort(([a], [b]) => (a === "other") - (b === "other") || a.localeCompare(b))
    .map(([origin, app]) => {
      const rate = app.submissions === 0 ? 0 : app.passed / app.submissions
      const status =
        origin === "other"
          ? "not gated"
          : app.submissions < APP_MINIMUM
            ? "NODATA"
            : rate >= PASS_RATE_MINIMUM
              ? "PASS"
              : "FAIL"
      return { origin, ...app, rate, status }
    })
  const gated = rows.filter((row) => row.status !== "not gated")
  const covered = days(counts.monitor)
  let result
  let reason
  if (gated.some((row) => row.status === "FAIL")) {
    result = "FAIL"
    reason = "an app passes less than 99 %"
  } else if (gated.length === 0) {
    result = "NODATA"
    reason = "no Monitor verdict from a listed app"
  } else if (gated.some((row) => row.status === "NODATA")) {
    result = "NODATA"
    reason = `an app has fewer than ${APP_MINIMUM} submissions: the production observation decides it (BD11)`
  } else if (covered < GATE_DAYS) {
    result = "NODATA"
    reason = `the Monitor lines cover ${covered.toFixed(1)} days, fewer than ${GATE_DAYS}`
  } else {
    result = "PASS"
    reason = `every listed app passes at least 99 % over ${covered.toFixed(1)} days`
  }
  return { rows, covered, result, reason }
}

/** Gate 2: no 10-minute window under Enforce where more than half carried no token. */
export function gateEnforce(counts) {
  const all = [...counts.windows.entries()].sort(([a], [b]) => a - b)
  const counted = all.filter(([, window]) => window.submissions >= WINDOW_MINIMUM)
  const spikes = counted.filter(([, window]) => window.missing * 2 > window.submissions)
  const covered = days(counts.enforce)
  let result
  let reason
  if (spikes.length > 0) {
    result = "FAIL"
    reason = `${spikes.length} spike window(s), the gate asks for 0`
  } else if (counted.length === 0) {
    result = "NODATA"
    reason = `no 10-minute window under Enforce reached ${WINDOW_MINIMUM} submissions`
  } else if (covered < GATE_DAYS) {
    result = "NODATA"
    reason = `the Enforce lines cover ${covered.toFixed(1)} days, fewer than ${GATE_DAYS}`
  } else {
    result = "PASS"
    reason = `0 spike windows over ${covered.toFixed(1)} days`
  }
  return {
    counted: counted.length,
    nodata: all.length - counted.length,
    spikes: spikes.map(([start, window]) => ({ start, ...window })),
    covered,
    result,
    reason,
  }
}

/** The value at `share` of the sorted list, by nearest rank: the 95th of 20 values is the 19th. */
export function percentile(values, share) {
  if (values.length === 0) return undefined
  const sorted = [...values].sort((a, b) => a - b)
  return sorted[Math.max(1, Math.ceil(share * sorted.length)) - 1]
}

/** Per app: how many verdicts went to Cloudflare, and how long its answers took. */
export function answerTimes(counts) {
  return [...counts.answerTimes.entries()]
    .sort(([a], [b]) => (a === "other") - (b === "other") || a.localeCompare(b))
    .map(([origin, times]) => ({
      origin,
      count: times.length,
      p50: percentile(times, 0.5),
      p95: percentile(times, 0.95),
      max: Math.max(...times),
    }))
}

const percent = (rate) => `${(rate * 100).toFixed(2)} %`
const stamp = (at) => new Date(at).toISOString().replace("T", " ").slice(0, 16) + " UTC"

/** The report, both gates with their comparators. */
export function formatReport(counts, monitor, enforce, gates = "both") {
  const lines = []
  lines.push(`Bot check verdicts: ${counts.read} lines read.`)
  lines.push(
    `  not counted: ${counts.probeAsserted} ProbeAsserted (S17's probe, never checked with Cloudflare), ${counts.unconfigured} Unconfigured (no check ran), ${counts.junk} junk token(s) (${JUNK_TOKEN_CODES.join(", ")}: no real widget makes one)`
  )
  if (counts.junk > 0) {
    const byApp = [...counts.junkByApp.entries()].sort(([a], [b]) => a.localeCompare(b))
    lines.push(`  junk tokens per app: ${byApp.map(([origin, count]) => `${origin} ${count}`).join("; ")}`)
  }
  if (gates !== "enforce") {
    lines.push("")
    lines.push("Gate 1, Monitor -> Enforce: bot.monitor-pass-rate per app")
    lines.push(
      `  comparator: Passed / submissions >= ${percent(PASS_RATE_MINIMUM)} for every listed app, with >= ${APP_MINIMUM} submissions each, over >= ${GATE_DAYS} days of Monitor lines`
    )
    lines.push(`  Monitor lines cover ${monitor.covered.toFixed(1)} days (>= ${GATE_DAYS} asked)`)
    lines.push("  app | submissions | passed | rejected | missing | not run | pass rate | result")
    for (const row of monitor.rows) {
      const verdict =
        row.status === "PASS"
          ? `PASS (>= ${percent(PASS_RATE_MINIMUM)})`
          : row.status === "FAIL"
            ? `FAIL (< ${percent(PASS_RATE_MINIMUM)})`
            : row.status === "NODATA"
              ? `NODATA (${row.submissions} < ${APP_MINIMUM} submissions)`
              : "not gated"
      lines.push(
        `  ${row.origin} | ${row.submissions} | ${row.passed} | ${row.rejected} | ${row.missing} | ${row.notRun} | ${percent(row.rate)} | ${verdict}`
      )
    }
    lines.push(`  RESULT: ${monitor.result}: ${monitor.reason}`)
  }
  if (gates !== "monitor") {
    lines.push("")
    lines.push("Gate 2, after Enforce: bot.token-missing-spike windows")
    lines.push(
      `  comparator: spike windows = 0 over >= ${GATE_DAYS} days of Enforce lines; a 10-minute window counts with >= ${WINDOW_MINIMUM} submissions, and spikes when more than half carry no token`
    )
    lines.push(`  Enforce lines cover ${enforce.covered.toFixed(1)} days (>= ${GATE_DAYS} asked)`)
    lines.push(
      `  windows: ${enforce.counted} counted, ${enforce.nodata} NODATA (< ${WINDOW_MINIMUM} submissions), ${enforce.spikes.length} spike(s) (= 0 asked)`
    )
    for (const spike of enforce.spikes) {
      lines.push(`    spike: ${stamp(spike.start)}, ${spike.missing} of ${spike.submissions} without a token`)
    }
    lines.push(`  RESULT: ${enforce.result}: ${enforce.reason}`)
  }
  lines.push("")
  lines.push("Cloudflare's answer time (every verdict that went to Cloudflare; not a gate)")
  const times = answerTimes(counts)
  if (times.length === 0) {
    lines.push("  no verdict went to Cloudflare")
  } else {
    lines.push("  app | verifications | median ms | p95 ms | longest ms")
    for (const row of times) {
      lines.push(`  ${row.origin} | ${row.count} | ${row.p50} | ${row.p95} | ${row.max}`)
    }
  }
  return lines.join("\n")
}

/** 0 PASS, 1 FAIL, 4 NODATA, over the gates asked. */
export function exitCode(results) {
  if (results.includes("FAIL")) return 1
  if (results.every((result) => result === "PASS")) return 0
  return 4
}

function logFiles(paths) {
  const files = []
  for (const path of paths) {
    if (statSync(path).isDirectory()) {
      for (const name of readdirSync(path).sort()) {
        if (name.endsWith(".log")) files.push(join(path, name))
      }
    } else {
      files.push(path)
    }
  }
  return files
}

async function readVerdicts(files) {
  const verdicts = []
  for (const file of files) {
    const reader = createInterface({ input: createReadStream(file, "utf8"), crlfDelay: Infinity })
    for await (const line of reader) {
      const verdict = parseVerdictLine(line)
      if (verdict) verdicts.push(verdict)
    }
  }
  return verdicts
}

function parseArgs(argv) {
  const options = { gates: "both", from: undefined, to: undefined, paths: [] }
  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index]
    if (arg === "--gate") options.gates = argv[++index]
    else if (arg === "--from") options.from = Date.parse(argv[++index])
    else if (arg === "--to") options.to = Date.parse(argv[++index])
    else options.paths.push(arg)
  }
  if (!["monitor", "enforce", "both"].includes(options.gates)) throw new Error("--gate is monitor, enforce or both")
  if (Number.isNaN(options.from) || Number.isNaN(options.to)) throw new Error("--from and --to take an ISO time")
  if (options.paths.length === 0) throw new Error("name at least one log file or folder")
  return options
}

async function main() {
  let options
  try {
    options = parseArgs(process.argv.slice(2))
  } catch (error) {
    console.error(`refused: ${error.message}`)
    console.error("usage: node Tools/probes/verdict-window.mjs [--gate monitor|enforce|both] [--from <ISO>] [--to <ISO>] <file or folder>...")
    return 2
  }
  let files
  try {
    files = logFiles(options.paths)
  } catch (error) {
    console.error(`refused: ${error.message}`)
    return 2
  }
  const counts = tally(await readVerdicts(files), options)
  const monitor = gateMonitor(counts)
  const enforce = gateEnforce(counts)
  console.log(`Read ${files.length} file(s).`)
  console.log(formatReport(counts, monitor, enforce, options.gates))
  const results =
    options.gates === "monitor" ? [monitor.result] : options.gates === "enforce" ? [enforce.result] : [monitor.result, enforce.result]
  return exitCode(results)
}

// Run as a script, not when a test imports it.
if (process.argv[1] && resolve(process.argv[1]).toLowerCase() === fileURLToPath(import.meta.url).toLowerCase()) {
  process.exitCode = await main()
}
