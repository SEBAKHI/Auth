import { existsSync, readFileSync } from "node:fs"
import { join } from "node:path"

import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import {
  captureEntryHints,
  dropFlowHint,
  FLOW_HINTS_LIFETIME_MS,
  HINT_THEMES,
  readFlowHints,
  resolveUiLocales,
  resolveUiTheme,
} from "./flow-hints"
import { SUPPORTED_LANGUAGES } from "./languages"

/**
 * The look an application asked for, kept for its flow in this tab. The server
 * resolves the same two parameters; `docs/api/ui-locales-cases.json` is the one
 * table both resolvers are tested against, so they cannot drift.
 */

function repositoryFile(relative: string): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, relative)
    // A C# file is commonly saved with a byte order mark; it would end up in the match.
    if (existsSync(candidate)) {
      const text = readFileSync(candidate, "utf8")
      return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text
    }
    dir = join(dir, "..")
  }
  throw new Error(`${relative} was not found walking up from ${process.cwd()}`)
}

interface Case {
  input: string | null
  expected: string | null
}

const cases = JSON.parse(repositoryFile("docs/api/ui-locales-cases.json")) as {
  uiLocales: Case[]
  uiTheme: Case[]
}

const NOW = Date.UTC(2026, 9, 27, 12, 0, 0)
const HANDLE = "a".repeat(64)
const OTHER_HANDLE = "b".repeat(64)

/** The pending request a page serves, as return-to.ts hands it over. */
function pending(handle: string, client = "CRM") {
  return `https://auth.example.com/api/v1/auth/authorize/resume?client_id=${client}&auth_request=${handle}`
}

function arriveAt(path: string) {
  window.history.replaceState(null, "", path)
}

describe("the shared resolution table", () => {
  it("is not empty", () => {
    expect(cases.uiLocales.length).toBeGreaterThan(10)
    expect(cases.uiTheme.length).toBeGreaterThan(5)
  })

  it.each(cases.uiLocales.map((row) => [row.input, row.expected]))(
    "ui_locales %j resolves to %j",
    (input, expected) => {
      expect(resolveUiLocales(input)).toBe(expected)
    }
  )

  it.each(cases.uiTheme.map((row) => [row.input, row.expected]))(
    "ui_theme %j resolves to %j",
    (input, expected) => {
      expect(resolveUiTheme(input)).toBe(expected)
    }
  )
})

describe("the language list", () => {
  it("is the server's", () => {
    const source = repositoryFile("Auth/Auth.Domain/Constants/Languages.cs")
    const match = /Supported\s*=\s*\[([^\]]*)\]/.exec(source)
    expect(match, "Languages.Supported in Languages.cs").not.toBeNull()
    const server = [...match![1].matchAll(/"([a-z]+)"/g)].map((m) => m[1])

    expect(server.length).toBeGreaterThan(0)
    expect(SUPPORTED_LANGUAGES.map((lang) => lang.code)).toEqual(server)
  })

  it("names the server's colour modes", () => {
    const source = repositoryFile("Auth/Auth.Domain/ValueObjects/PresentationHints.cs")
    const match = /Themes\s*=\s*\[([^\]]*)\]/.exec(source)
    expect(match, "PresentationHints.Themes").not.toBeNull()

    expect([...HINT_THEMES]).toEqual([...match![1].matchAll(/"([a-z]+)"/g)].map((m) => m[1]))
  })
})

describe("captureEntryHints", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    window.localStorage.clear()
    arriveAt("/")
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("keeps the resolved hints of a sign-in page, and takes them off the address bar", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar-EG%20fr&ui_theme=dark`)

    captureEntryHints(pending(HANDLE), NOW)

    expect(readFlowHints(NOW)).toEqual({ language: "ar", theme: "dark" })
    expect(window.location.pathname).toBe("/login")
    expect(window.location.search).toBe(`?client_id=CRM&auth_request=${HANDLE}`)
  })

  it("writes nothing the user owns", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar&ui_theme=dark`)

    captureEntryHints(pending(HANDLE), NOW)

    expect(window.localStorage.length).toBe(0)
  })

  it("leaves an address without hints untouched", () => {
    const replace = vi.spyOn(window.history, "replaceState")
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}`)
    replace.mockClear()

    captureEntryHints(pending(HANDLE), NOW)

    expect(replace).not.toHaveBeenCalled()
    expect(readFlowHints(NOW)).toEqual({})
  })

  it("takes a direct link's hints, with no flow to tie them to", () => {
    arriveAt("/profile?ui_locales=fa&ui_theme=light#security")

    captureEntryHints(null, NOW)

    expect(readFlowHints(NOW)).toEqual({ language: "fa", theme: "light" })
    expect(window.location.search).toBe("")
    expect(window.location.hash).toBe("#security")
  })

  it("keeps the hints across a page of the same flow, and a page with no flow", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar`)
    captureEntryHints(pending(HANDLE), NOW)

    // A reload (the address no longer names them), then the expired page.
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}`)
    captureEntryHints(pending(HANDLE), NOW + 1000)
    arriveAt("/sign-in-expired?client_id=CRM")
    captureEntryHints(null, NOW + 2000)

    expect(readFlowHints(NOW + 3000)).toEqual({ language: "ar" })
  })

  it("does not let the next redirect of the same flow undo the user's own choice", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar&ui_theme=dark`)
    captureEntryHints(pending(HANDLE), NOW)

    dropFlowHint("language", NOW + 1000)
    arriveAt(`/create-organization?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar&ui_theme=dark`)
    captureEntryHints(pending(HANDLE), NOW + 2000)

    expect(readFlowHints(NOW + 3000)).toEqual({ theme: "dark" })
  })

  it("replaces another flow's hints, with nothing when the new flow names none", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ar&ui_theme=dark`)
    captureEntryHints(pending(HANDLE), NOW)

    arriveAt(`/login?client_id=SHOP&auth_request=${OTHER_HANDLE}`)
    captureEntryHints(pending(OTHER_HANDLE, "SHOP"), NOW + 1000)

    expect(readFlowHints(NOW + 2000)).toEqual({})
  })

  it("tells two pending requests apart by the request alone, never storing it", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=tr`)
    captureEntryHints(pending(HANDLE), NOW)

    expect(window.sessionStorage.getItem("auth.flow.hints")).not.toContain(HANDLE)

    captureEntryHints(pending(HANDLE), NOW + 1000)
    expect(readFlowHints(NOW + 1000)).toEqual({ language: "tr" })

    captureEntryHints(pending(OTHER_HANDLE), NOW + 2000)
    expect(readFlowHints(NOW + 2000)).toEqual({})
  })

  it("ends with the flow's lifetime", () => {
    arriveAt(`/login?client_id=CRM&auth_request=${HANDLE}&ui_locales=ur`)
    captureEntryHints(pending(HANDLE), NOW)

    expect(readFlowHints(NOW + FLOW_HINTS_LIFETIME_MS - 1)).toEqual({ language: "ur" })
    expect(readFlowHints(NOW + FLOW_HINTS_LIFETIME_MS)).toEqual({})
  })

  it("shows English for languages it does not have, and drops an unknown colour mode", () => {
    arriveAt("/login?ui_locales=de&ui_theme=blue")

    captureEntryHints(null, NOW)

    expect(readFlowHints(NOW)).toEqual({ language: "en" })
  })

  it("survives storage that refuses, and an address bar it may not rewrite", () => {
    arriveAt("/login?ui_locales=ar")
    // On the prototype: a Storage instance's own methods cannot be replaced.
    vi.spyOn(Object.getPrototypeOf(window.sessionStorage) as Storage, "setItem").mockImplementation(() => {
      throw new Error("quota")
    })
    vi.spyOn(window.history, "replaceState").mockImplementation(() => {
      throw new Error("sandboxed")
    })

    expect(() => captureEntryHints(null, NOW)).not.toThrow()
    expect(readFlowHints(NOW)).toEqual({})
  })
})

describe("readFlowHints", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  it("reads damaged or foreign storage as no hint", () => {
    window.sessionStorage.setItem("auth.flow.hints", "{not json")
    expect(readFlowHints(NOW)).toEqual({})

    window.sessionStorage.setItem("auth.flow.hints", JSON.stringify({ language: "ar" }))
    expect(readFlowHints(NOW)).toEqual({})

    window.sessionStorage.setItem(
      "auth.flow.hints",
      JSON.stringify({ flow: 7, language: "de", theme: "blue", expiresAt: NOW + 1000 })
    )
    expect(readFlowHints(NOW)).toEqual({ language: undefined, theme: undefined })
  })
})

describe("dropFlowHint", () => {
  beforeEach(() => {
    window.sessionStorage.clear()
  })

  it("does nothing when no hint is kept", () => {
    dropFlowHint("theme", NOW)

    expect(window.sessionStorage.length).toBe(0)
  })

  it("ends one hint and keeps the other", () => {
    arriveAt("/login?ui_locales=ar&ui_theme=dark")
    captureEntryHints(null, NOW)

    dropFlowHint("theme", NOW)

    expect(readFlowHints(NOW)).toEqual({ language: "ar" })
  })
})
