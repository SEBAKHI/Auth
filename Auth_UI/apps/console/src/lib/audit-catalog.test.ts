import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { en } from "@authsystem/i18n/locales/en"

import {
  AUDIT_ACTIONS,
  AUDIT_ACTION_TYPES,
  auditActionI18nKey,
  auditActionTypeI18nKey,
} from "./audit-catalog"

/**
 * The console does not decide what the system records — `AuditActions.cs` does.
 * This test reads that C# file and holds the two sides together.
 *
 * The seam is invisible from inside either half: the backend suite only knows
 * about C#, the frontend suite only about TypeScript, and an action added to one
 * and not the other compiles, deploys, and shows up in the audit table as a raw
 * code nobody meant to ship untranslated.
 */
function catalogueSource(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, "Auth/Auth.Domain/Constants/AuditActions.cs")
    if (existsSync(candidate)) return readFileSync(candidate, "utf8")
    dir = dirname(dir)
  }
  throw new Error("AuditActions.cs not found above " + process.cwd())
}

const SOURCE = catalogueSource()

/** Every `const string Name = "value";` in the catalogue, by name. */
function constants(): Map<string, string> {
  const found = new Map<string, string>()
  for (const match of SOURCE.matchAll(
    /public const string (\w+) = "([^"]+)";/g
  )) {
    found.set(match[1], match[2])
  }
  return found
}

/** The `[Action] = AuditActionTypes.Category` rows of `ByCode`, resolved. */
function csharpPairs(): { code: string; actionType: string }[] {
  const byName = constants()
  return [...SOURCE.matchAll(/\[(\w+)\] = AuditActionTypes\.(\w+)/g)].map(
    (match) => {
      const code = byName.get(match[1])
      const actionType = byName.get(match[2])
      expect(
        code,
        `AuditActions.${match[1]} is not a const string`
      ).toBeTruthy()
      expect(
        actionType,
        `AuditActionTypes.${match[2]} is not a const string`
      ).toBeTruthy()
      return { code: code as string, actionType: actionType as string }
    }
  )
}

/** The nine categories, read from the `AuditActionTypes` class body. */
function csharpTypes(): string[] {
  const block = SOURCE.slice(
    SOURCE.indexOf("public static class AuditActionTypes"),
    SOURCE.indexOf("public static class AuditActions")
  )
  return [...block.matchAll(/public const string \w+ = "([^"]+)";/g)].map(
    (m) => m[1]
  )
}

type Tree = Record<string, unknown>

const auditLogs = en.auditLogs as unknown as Tree

describe("the console mirror matches the catalogue it mirrors", () => {
  it("reads the C# catalogue at all", () => {
    // Guards the regexes and the file walk: if either stops matching, every
    // assertion below would pass over an empty list and prove nothing.
    expect(csharpTypes().length).toBeGreaterThan(5)
    expect(csharpPairs().length).toBeGreaterThan(40)
  })

  it("lists the same categories, in the same order", () => {
    expect([...AUDIT_ACTION_TYPES]).toEqual(csharpTypes())
  })

  it("lists the same actions, filed under the same categories", () => {
    expect([...AUDIT_ACTIONS]).toEqual(csharpPairs())
  })

  it("gives every action a category the console has a name for", () => {
    for (const entry of AUDIT_ACTIONS) {
      expect(
        AUDIT_ACTION_TYPES as readonly string[],
        `${entry.code} is filed under ${entry.actionType}, which is not a known category`
      ).toContain(entry.actionType)
    }
  })
})

describe("every catalogue entry can be shown in words", () => {
  it("derives a unique i18n key per action", () => {
    // Two codes collapsing to one key would silently make one action wear the
    // other's name, in all seven languages at once.
    const keys = AUDIT_ACTIONS.map((entry) => auditActionI18nKey(entry.code))
    expect(new Set(keys).size).toBe(keys.length)
  })

  it.each([...AUDIT_ACTION_TYPES])("%s has an English name", (actionType) => {
    const block = auditLogs.actionTypes as Tree | undefined
    expect(block, "auditLogs.actionTypes is missing from en.ts").toBeDefined()
    expect(
      block?.[auditActionTypeI18nKey(actionType)],
      `auditLogs.actionTypes.${auditActionTypeI18nKey(actionType)} is missing — the category renders as its raw code`
    ).toBeTruthy()
  })

  it.each(AUDIT_ACTIONS.map((entry) => entry.code))(
    "%s has an English name",
    (code) => {
      const block = auditLogs.actions as Tree | undefined
      expect(block, "auditLogs.actions is missing from en.ts").toBeDefined()
      expect(
        block?.[auditActionI18nKey(code)],
        `auditLogs.actions.${auditActionI18nKey(code)} is missing — the action renders as its raw code`
      ).toBeTruthy()
    }
  )
})
