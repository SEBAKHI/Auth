import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import { en } from "@authsystem/i18n/locales/en"

import {
  NOTIFICATION_TYPE_CODES,
  notificationTypeI18nKey,
} from "./notification-catalog"

/**
 * The console does not decide what the system sends — `NotificationTypeCodes.cs`
 * does. This test reads that C# file and holds the two sides together, exactly
 * as `audit-catalog.test.ts` does for audit actions.
 *
 * The seam is invisible from inside either half: the backend suite only knows
 * about C#, the frontend suite only about TypeScript, and a type added to one
 * and not the other compiles, deploys, and shows up in the delivery log as a
 * raw code nobody meant to ship untranslated.
 */
function catalogueSource(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(
      dir,
      "Auth/Auth.Domain/Constants/NotificationTypeCodes.cs"
    )
    if (existsSync(candidate)) return readFileSync(candidate, "utf8")
    dir = dirname(dir)
  }
  throw new Error("NotificationTypeCodes.cs not found above " + process.cwd())
}

const SOURCE = catalogueSource()

/**
 * Every type code in the catalogue, in declaration order.
 *
 * Filtered by shape rather than by name: the class also holds `RedactedBody`,
 * whose value `[redacted]` is a placeholder written into a message body and not
 * a type at all. Type codes are kebab-case, and nothing else in the file is —
 * so the shape is the discriminator, and a new non-code constant added later
 * cannot silently enter the list.
 */
function csharpCodes(): string[] {
  return [...SOURCE.matchAll(/public const string \w+ = "([^"]+)";/g)]
    .map((match) => match[1])
    .filter((value) => /^[a-z][a-z0-9]*(-[a-z0-9]+)*$/.test(value))
}

type Tree = Record<string, unknown>

const notifications = en.notifications as unknown as Tree

describe("the console mirror matches the catalogue it mirrors", () => {
  it("reads the C# catalogue at all", () => {
    // Guards the file walk and the regex: if either stops matching, every
    // assertion below would pass over an empty list and prove nothing.
    expect(csharpCodes().length).toBeGreaterThan(15)
  })

  it("excludes the constants that are not type codes", () => {
    expect(csharpCodes()).not.toContain("[redacted]")
  })

  it("lists the same codes, in the same order", () => {
    expect([...NOTIFICATION_TYPE_CODES]).toEqual(csharpCodes())
  })
})

describe("every catalogue entry can be shown in words", () => {
  it("derives a unique i18n key per type", () => {
    // Two codes collapsing to one key would silently make one type wear the
    // other's name, in all seven languages at once.
    const keys = NOTIFICATION_TYPE_CODES.map(notificationTypeI18nKey)
    expect(new Set(keys).size).toBe(keys.length)
  })

  it.each([...NOTIFICATION_TYPE_CODES])("%s has an English name", (code) => {
    const block = notifications.types as Tree | undefined
    expect(block, "notifications.types is missing from en.ts").toBeDefined()
    expect(
      block?.[notificationTypeI18nKey(code)],
      `notifications.types.${notificationTypeI18nKey(code)} is missing — the type renders as its raw code`
    ).toBeTruthy()
  })

  it("carries no name for a type the catalogue does not have", () => {
    // The other direction: a key left behind after a type was removed would
    // never be seen, and would quietly rot in seven files at once.
    const block = (notifications.types ?? {}) as Tree
    const known = new Set(NOTIFICATION_TYPE_CODES.map(notificationTypeI18nKey))
    expect(Object.keys(block).filter((key) => !known.has(key))).toEqual([])
  })
})
