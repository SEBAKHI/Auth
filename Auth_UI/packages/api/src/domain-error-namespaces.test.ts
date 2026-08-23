import { existsSync, readdirSync, readFileSync } from "node:fs"
import path from "node:path"
import { describe, expect, it } from "vitest"

import { getErrorFeedback } from "./errors"

/**
 * The UI's list of domain error namespaces must cover the backend's.
 *
 * `getErrorFeedback` prefers the server's localized sentence for a code in one
 * of those namespaces, and falls back to local copy for anything else. A new
 * error class on the server would otherwise be silently downgraded to a generic
 * status sentence - and, worse, a namespace added here by hand without a class
 * behind it would open the door to rendering text nobody wrote for a reader.
 */
function repositoryRoot(): string {
  let directory = process.cwd()
  for (let depth = 0; depth < 10; depth += 1) {
    if (existsSync(path.join(directory, "Auth", "Auth.sln"))) return directory
    const parent = path.dirname(directory)
    if (parent === directory) break
    directory = parent
  }
  throw new Error("Auth/Auth.sln not found above " + process.cwd())
}

function csharpFiles(directory: string): string[] {
  const found: string[] = []
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name)
    if (entry.isDirectory()) found.push(...csharpFiles(full))
    else if (entry.name.endsWith(".cs")) found.push(full)
  }
  return found
}

/**
 * Every dotted error code the backend can raise, from BOTH places that raise
 * them.
 *
 * Scanning only `Auth.Domain/Errors` for the named-argument form was how
 * `Password.*` escaped: the password policy lives in
 * `Auth.Application/Validators/PasswordValidator.cs` and raises its codes
 * positionally - `Error.Validation("Password.TooShort", "Validation.…")`. Both
 * the directory and the argument form had to be missed for it to slip through,
 * and both were, so the UI suppressed the one sentence that names the broken
 * rule and told the reader only that "some information wasn't accepted".
 */
function backendNamespaces(): string[] {
  const root = repositoryRoot()
  const namespaces = new Set<string>()
  const sources = [
    ...csharpFiles(path.join(root, "Auth/Auth.Domain/Errors")),
    ...csharpFiles(path.join(root, "Auth/Auth.Application")),
  ]
  for (const file of sources) {
    const source = readFileSync(file, "utf8")
    // `code: "Ns.Code"` (named) and `Error.Xxx("Ns.Code"` (positional).
    for (const [, code] of source.matchAll(/code: "([A-Za-z]+)\./g)) {
      namespaces.add(code)
    }
    for (const [, code] of source.matchAll(
      /Error\.[A-Za-z]+\(\s*"([A-Z][A-Za-z]+)\./g
    )) {
      namespaces.add(code)
    }
  }
  return [...namespaces].sort()
}

describe("domain error namespaces", () => {
  const namespaces = backendNamespaces()

  it("finds the backend's error classes", () => {
    expect(namespaces.length).toBeGreaterThan(15)
    expect(namespaces).toContain("User")
  })

  it.each(namespaces)("%s is recognised as a domain namespace", (namespace) => {
    const detail = "A sentence the catalog localized."
    expect(
      getErrorFeedback({
        status: 409,
        title: `${namespace}.SomethingSpecific`,
        detail,
      }).description
    ).toBe(detail)
  })

  it.each([
    ["System.DatabaseUnavailableException", "an unhandled exception type"],
    ["Microsoft.Data.SqlClient.SqlException", "a driver exception type"],
    ["Some.Unregistered.Code", "a namespace nobody registered"],
  ])("%s is not trusted (%s)", (title) => {
    const feedback = getErrorFeedback({
      status: 500,
      title,
      detail: "private database host and stack trace",
    })
    expect(feedback.description).not.toContain("stack trace")
    expect(feedback.description).not.toContain("private database")
  })
})
