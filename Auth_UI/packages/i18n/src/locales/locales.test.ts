import { describe, expect, it } from "vitest"

import { ar } from "./ar"
import { en } from "./en"
import { fa } from "./fa"
import { fr } from "./fr"
import { tr } from "./tr"
import { ur } from "./ur"
import { zh } from "./zh"

/**
 * Guards catalog parity: every locale must mirror en.ts exactly (the
 * TranslationResources type enforces this at compile time too — this test
 * exists so a failure lists the offending keys) and must keep the same
 * {{placeholder}} tokens so interpolation never silently breaks.
 */

type Tree = Record<string, unknown>

const locales: Record<string, Tree> = { ar, tr, fr, zh, ur, fa }

function collectKeys(obj: Tree, prefix = ""): string[] {
  return Object.entries(obj).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key
    return typeof value === "string"
      ? [path]
      : collectKeys(value as Tree, path)
  })
}

function leafAt(obj: Tree, path: string): string | undefined {
  let node: unknown = obj
  for (const part of path.split(".")) {
    if (typeof node !== "object" || node === null) return undefined
    node = (node as Tree)[part]
  }
  return typeof node === "string" ? node : undefined
}

function placeholders(value: string): string[] {
  return [...value.matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1]).sort()
}

const enKeys = collectKeys(en as Tree)

const applicationSources = import.meta.glob(
  [
    "../../../../apps/**/*.{ts,tsx}",
    "../../../../packages/**/*.{ts,tsx}",
    "!../../../../**/*.test.{ts,tsx}",
    "!../../../../**/*.d.ts",
  ],
  { eager: true, query: "?raw", import: "default" }
) as Record<string, string>

describe.each(Object.entries(locales))("locale %s", (_name, locale) => {
  const localeKeys = new Set(collectKeys(locale))

  it("contains every key from en", () => {
    const missing = enKeys.filter((key) => !localeKeys.has(key))
    expect(missing).toEqual([])
  })

  it("contains no keys absent from en", () => {
    const enSet = new Set(enKeys)
    const extra = [...localeKeys].filter((key) => !enSet.has(key))
    expect(extra).toEqual([])
  })

  it("preserves interpolation placeholders", () => {
    const broken = enKeys.filter((key) => {
      const enValue = leafAt(en as Tree, key)
      const localeValue = leafAt(locale, key)
      if (enValue === undefined || localeValue === undefined) return false
      return placeholders(enValue).join(",") !== placeholders(localeValue).join(",")
    })
    expect(broken).toEqual([])
  })
})

describe("translation key usage", () => {
  it("defines every literal key passed to t() in application source", () => {
    const knownKeys = new Set(enKeys)
    const missing = new Set<string>()
    const literalTranslationCall = /\bt\(\s*["']([\w.-]+)["']/g

    for (const source of Object.values(applicationSources)) {
      for (const match of source.matchAll(literalTranslationCall)) {
        const key = match[1]
        if (!knownKeys.has(key)) missing.add(key)
      }
    }

    expect([...missing].sort()).toEqual([])
  })
})
