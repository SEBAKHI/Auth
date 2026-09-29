import type { BrowserContext, ConsoleMessage, Page } from "@playwright/test"

export type CspChannel = "event" | "console" | "audits"

export interface CspViolation {
  /** Effective directive, normalised: script-src-elem → script-src. */
  directive: string
  /** The blocked URL, or "inline" / "eval" for code the policy refused. */
  blocked: string
  /** The document or frame it happened in, as the channel reports it. */
  frame: string
  /** The script that caused it, when the channel knows ("" otherwise). */
  source: string
  channel: CspChannel
}

export interface CspExpectation {
  directive: string
  blocked: string | RegExp
  /**
   * Only violations caused by a matching script. A register entry names its
   * source, so it cannot excuse the same directive from anywhere else.
   */
  source?: RegExp
  /** Why this violation is expected. Required: an unexplained entry hides a defect. */
  reason: string
}

/** script-src-elem / -attr and style-src-elem / -attr count as their parent. */
export function normaliseDirective(directive: string) {
  return directive.trim().toLowerCase().replace(/-(elem|attr)$/, "")
}

function matches(expectation: CspExpectation, violation: CspViolation) {
  if (normaliseDirective(expectation.directive) !== violation.directive) return false
  if (expectation.source && !expectation.source.test(violation.source)) return false
  return typeof expectation.blocked === "string"
    ? expectation.blocked === violation.blocked
    : expectation.blocked.test(violation.blocked)
}

/**
 * Parses Chromium's console text for a CSP violation, e.g.
 *   Loading the image 'https://x/y.png' violates the following Content Security
 *   Policy directive: "img-src 'self' ...".
 *   Executing inline script violates the following Content Security Policy
 *   directive 'script-src 'self''. Either the 'unsafe-inline' keyword ...
 * Returns null for any other console error.
 */
export function parseConsoleViolation(text: string) {
  // Chromium adds a second line for a blocked fetch, without naming the directive.
  const refusedFetch = /cannot load (\S+?)\.?\s+Refused to connect because it violates the document's Content Security Policy/i.exec(
    text
  )
  if (refusedFetch) return { directive: "connect-src", blocked: refusedFetch[1] }
  const directive = /violates the following Content Security Policy directive:?\s*["']([a-z-]+)/i.exec(
    text
  )?.[1]
  if (!directive) return null
  let blocked: string
  if (/^(Executing|Applying) inline /i.test(text)) blocked = "inline"
  else if (/eval/i.test(text.split("violates")[0])) blocked = "eval"
  else blocked = /'((?:https?|wss?|data|blob):[^']*)'/i.exec(text)?.[1] ?? "unknown"
  return { directive: normaliseDirective(directive), blocked }
}

/** How a violation is named in a verdict: what, where, and which script caused it. */
function describeViolation(violation: CspViolation) {
  return `${violation.directive} ${violation.blocked} @ ${violation.frame || "(frame not reported)"}${violation.source ? ` from ${violation.source}` : ""}`
}

const BINDING = "__harnessCspViolation"

/**
 * Records every CSP violation in a context through three independent channels,
 * because each is blind somewhere the others are not (card S30a, Q5):
 *
 *   1. event   - a `securitypolicyviolation` listener installed by an init script
 *                in every frame where scripts run, reporting through a binding;
 *   2. console - Chromium's console error for the violation, which Playwright
 *                collects from every frame session - including a sandbox=""
 *                srcdoc frame, where no script (so no channel 1) runs;
 *   3. audits  - a CDP session per page with Audits.enable, ContentSecurityPolicyIssue.
 *
 * None of them is route interception. The test declares what it expects with
 * `csp.expect(...)`; `verify()` - run by the fixture after every test - fails on
 * a violation nobody declared AND on a declared violation that never happened.
 */
export class CspObserver {
  readonly #violations: CspViolation[] = []
  readonly #expected: CspExpectation[] = []
  readonly #tolerated: CspExpectation[] = []
  readonly #watched = new WeakSet<Page>()

  static async attach(context: BrowserContext) {
    const observer = new CspObserver()
    await context.exposeBinding(
      BINDING,
      (source, detail: { directive: string; blocked: string; documentURI: string; sourceFile: string }) => {
        observer.#record({
          directive: normaliseDirective(detail.directive),
          blocked: detail.blocked || "unknown",
          frame: detail.documentURI || source.frame.url(),
          source: detail.sourceFile || "",
          channel: "event",
        })
      }
    )
    await context.addInitScript((binding: string) => {
      document.addEventListener(
        "securitypolicyviolation",
        (event) => {
          const report = (window as unknown as Record<string, unknown>)[binding]
          if (typeof report !== "function") return
          void (report as (detail: object) => Promise<void>)({
            directive: event.effectiveDirective || event.violatedDirective,
            blocked: event.blockedURI,
            documentURI: event.documentURI,
            sourceFile: event.sourceFile,
          })
        },
        true
      )
    }, BINDING)
    for (const page of context.pages()) await observer.#watch(context, page)
    context.on("page", (page) => void observer.#watch(context, page))
    return observer
  }

  async #watch(context: BrowserContext, page: Page) {
    if (this.#watched.has(page)) return
    this.#watched.add(page)
    page.on("console", (message: ConsoleMessage) => {
      if (message.type() !== "error") return
      const text = message.text()
      const parsed = parseConsoleViolation(text)
      const where = { frame: page.url(), source: message.location().url || "", channel: "console" as const }
      if (parsed) {
        this.#record({ ...parsed, ...where })
      } else if (/Content Security Policy/i.test(text) && /violat|refused/i.test(text)) {
        // A CSP error in wording the parser does not know is recorded, not
        // dropped, so the verdict fails loudly and the parser gets extended.
        this.#record({ directive: "unparsed", blocked: text.slice(0, 160), ...where })
      }
    })
    try {
      const session = await context.newCDPSession(page)
      session.on("Audits.issueAdded", (event) => {
        if (event.issue.code !== "ContentSecurityPolicyIssue") return
        const details = event.issue.details.contentSecurityPolicyIssueDetails
        if (!details) return
        const type = details.contentSecurityPolicyViolationType
        this.#record({
          directive: normaliseDirective(details.violatedDirective),
          blocked:
            details.blockedURL ??
            (type === "kInlineViolation" ? "inline" : type === "kEvalViolation" ? "eval" : "unknown"),
          frame: "",
          source: details.sourceCodeLocation?.url ?? "",
          channel: "audits",
        })
      })
      await session.send("Audits.enable")
    } catch {
      // A page that closed before its session attached has nothing left to report.
    }
  }

  #record(violation: CspViolation) {
    this.#violations.push(violation)
  }

  /** Declares a violation this test expects, with its reason. */
  expect(expectation: CspExpectation) {
    if (!expectation.reason?.trim()) throw new Error("csp.expect needs a reason")
    this.#expected.push(expectation)
  }

  /**
   * Accepts a violation if it happens, without requiring it. Only the fixture
   * uses this, for the register in expected-csp-violations.ts.
   */
  tolerate(expectation: CspExpectation) {
    if (!expectation.reason?.trim()) throw new Error("csp.tolerate needs a reason")
    this.#tolerated.push(expectation)
  }

  /** Every report so far, from every channel (a violation usually appears in several). */
  violations(): readonly CspViolation[] {
    return [...this.#violations]
  }

  /** Reports arrive asynchronously; give them a moment before judging. */
  async settle(ms = 400) {
    await new Promise((resolve) => setTimeout(resolve, ms))
  }

  /** Throws when a violation was not declared, or a declared one did not happen. */
  verify() {
    const accepted = [...this.#expected, ...this.#tolerated]
    const unexpected = this.#violations.filter(
      (violation) => !accepted.some((expectation) => matches(expectation, violation))
    )
    const unmet = this.#expected.filter(
      (expectation) => !this.#violations.some((violation) => matches(expectation, violation))
    )
    if (!unexpected.length && !unmet.length) return
    const lines: string[] = []
    const seen = new Set<string>()
    for (const violation of unexpected) {
      const key = describeViolation(violation)
      if (seen.has(key)) continue
      seen.add(key)
      const channels = unexpected
        .filter((other) => describeViolation(other) === key)
        .map((other) => other.channel)
      lines.push(`  unexpected: ${key} [${[...new Set(channels)].join(", ")}]`)
    }
    for (const expectation of unmet) {
      lines.push(
        `  expected but did not happen: ${expectation.directive} ${String(expectation.blocked)} (${expectation.reason})`
      )
    }
    throw new Error(
      `CSP observer: ${unexpected.length ? "violations nobody declared" : ""}` +
        `${unexpected.length && unmet.length ? " and " : ""}` +
        `${unmet.length ? "declared violations that never happened" : ""}:\n${lines.join("\n")}\n` +
        "Declare a real, explained violation with csp.expect({ directive, blocked, reason }); " +
        "never widen the policy to make this pass."
    )
  }

  /**
   * Forgets reports and the test's own expectations (the self-test uses it after
   * proving verify throws). Tolerated register entries stay.
   */
  reset() {
    this.#violations.length = 0
    this.#expected.length = 0
  }
}
