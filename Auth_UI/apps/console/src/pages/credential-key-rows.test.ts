import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import i18next, { type TFunction } from "i18next"
import { beforeAll, describe, expect, it } from "vitest"

import { ar } from "@authsystem/i18n/locales/ar"
import { en } from "@authsystem/i18n/locales/en"
import { fa } from "@authsystem/i18n/locales/fa"
import { fr } from "@authsystem/i18n/locales/fr"
import { tr } from "@authsystem/i18n/locales/tr"
import { ur } from "@authsystem/i18n/locales/ur"
import { zh } from "@authsystem/i18n/locales/zh"
import {
  KEY_REVOCATION_REASON_CODES,
  revokeReasonForReader,
  revokeReasonText,
} from "./credential-key-rows"

const catalogs = { en, ar, tr, fr, zh, ur, fa }

let tFor: Record<"en" | "ar", TFunction>

beforeAll(async () => {
  const instance = i18next.createInstance()
  await instance.init({
    lng: "en",
    resources: { en: { translation: en }, ar: { translation: ar } },
  })
  tFor = { en: instance.getFixedT("en"), ar: instance.getFixedT("ar") }
})

/**
 * A key's row panel lists every field it is given, so the revocation reason
 * reaches it only on a revoked key that has one (owner, 2026-10-31).
 */
describe("revokeReasonForReader", () => {
  it("keeps the reason of a revoked key that has one, as typed", () => {
    const revoked = { id: "a", isRevoked: true, revokeReason: "Leaked" }

    expect(revokeReasonForReader(tFor.en)([revoked])).toEqual([revoked])
  })

  it("drops the field from a live key and from a revoked key without a reason", () => {
    const rows = revokeReasonForReader(tFor.en)([
      { id: "live", isRevoked: false, revokeReason: null },
      { id: "no-reason", isRevoked: true, revokeReason: null },
      { id: "blank", isRevoked: true, revokeReason: "   " },
      {
        id: "live-coded",
        isRevoked: false,
        revokeReasonCode: "ApplicationDeleted",
      },
    ])

    for (const row of rows) {
      expect(row).not.toHaveProperty("revokeReason")
      expect(row).not.toHaveProperty("revokeReasonCode")
    }
    expect(rows.map((row) => row.id)).toEqual([
      "live",
      "no-reason",
      "blank",
      "live-coded",
    ])
  })

  // Q6 (owner, 2026-10-31): a deleted application's keys said "Application
  // deleted" in English in every language.
  it.each(["en", "ar"] as const)(
    "names a reason the system wrote in the reader's language (%s), with no code row",
    (language) => {
      const [row] = revokeReasonForReader(tFor[language])([
        {
          id: "orphan",
          isRevoked: true,
          revokeReason: null,
          revokeReasonCode: "ApplicationDeleted",
        },
      ])

      expect(row.revokeReason).toBe(
        catalogs[language].apiKeys.revokeReasonCodes.ApplicationDeleted
      )
      expect(row).not.toHaveProperty("revokeReasonCode")
    }
  )
})

describe("revokeReasonText", () => {
  it("shows a code this console does not know as the code itself", () => {
    expect(
      revokeReasonText(
        { isRevoked: true, revokeReasonCode: "OrganizationDeleted" },
        tFor.ar
      )
    ).toBe("OrganizationDeleted")
  })

  it("never translates an administrator's reason", () => {
    expect(
      revokeReasonText(
        { isRevoked: true, revokeReason: "Application deleted" },
        tFor.ar
      )
    ).toBe("Application deleted")
  })
})

/**
 * The console does not decide which codes the system writes:
 * `KeyRevocationReasons.cs` does, and this reads that file. A code added there
 * without a name here would reach the reader as a bare code.
 */
function reasonsSource(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(
      dir,
      "Auth/Auth.Domain/Constants/KeyRevocationReasons.cs"
    )
    if (existsSync(candidate)) {
      return readFileSync(candidate, "utf8")
        .replace(/^\uFEFF/, "")
        .replace(/\r\n/g, "\n")
    }
    dir = dirname(dir)
  }
  throw new Error("KeyRevocationReasons.cs not found above " + process.cwd())
}

describe("the reason codes mirror KeyRevocationReasons.cs", () => {
  const source = reasonsSource()
  const constants = new Map(
    [...source.matchAll(/public const string (\w+) = "([^"]+)";/g)].map(
      ([, name, value]) => [name, value]
    )
  )
  const all = /All\s*=\s*\[([^\]]*)\]/.exec(source)?.[1] ?? ""
  const backend = all
    .split(",")
    .map((name) => name.trim())
    .filter(Boolean)
    .map((name) => constants.get(name))

  it("reads a non-empty list from the C# file", () => {
    expect(backend.length).toBeGreaterThan(0)
    expect(backend).not.toContain(undefined)
  })

  it("holds exactly the codes the system writes, once each", () => {
    expect([...KEY_REVOCATION_REASON_CODES].sort()).toEqual(
      [...(backend as string[])].sort()
    )
    expect(new Set(KEY_REVOCATION_REASON_CODES).size).toBe(
      KEY_REVOCATION_REASON_CODES.length
    )
  })

  it.each(Object.keys(catalogs) as (keyof typeof catalogs)[])(
    "names every code in %s",
    (language) => {
      const names = catalogs[language].apiKeys.revokeReasonCodes as Record<
        string,
        string
      >
      for (const code of KEY_REVOCATION_REASON_CODES) {
        expect(names[code]?.trim()).toBeTruthy()
      }
      expect(Object.keys(names).sort()).toEqual(
        [...KEY_REVOCATION_REASON_CODES].sort()
      )
    }
  )
})
