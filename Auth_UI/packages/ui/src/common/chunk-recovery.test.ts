import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import {
  canRecoverFromChunkLoadError,
  isChunkLoadError,
  recoverFromChunkLoadError,
} from "./chunk-recovery"

/**
 * jsdom here ships no Storage implementation, and the module deliberately
 * swallows storage failures — so without a stub the guard would silently test
 * its degraded path instead of its real one.
 */
function stubSessionStorage(): void {
  const entries = new Map<string, string>()
  Object.defineProperty(window, "sessionStorage", {
    configurable: true,
    value: {
      getItem: (key: string) => entries.get(key) ?? null,
      setItem: (key: string, value: string) => void entries.set(key, value),
      removeItem: (key: string) => void entries.delete(key),
      clear: () => entries.clear(),
      key: () => null,
      length: 0,
    },
  })
}

function runningEntry(file: string): void {
  document.head.innerHTML = `<script type="module" src="/assets/${file}"></script>`
}

describe("isChunkLoadError", () => {
  it.each([
    "Failed to fetch dynamically imported module: /assets/privacy-a1b2c3.js",
    "error loading dynamically imported module",
    "Importing a module script failed.",
    // What the SPA-fallback rewrite actually produced: the index document
    // returned with 200 where a module was expected.
    "Failed to load module script: Expected a JavaScript module script but the server responded with a MIME type of \"text/html\".",
    "Unexpected token '<'",
  ])("recognises %s as a stale build", (message) => {
    expect(isChunkLoadError(new Error(message))).toBe(true)
  })

  it("leaves an ordinary application error alone", () => {
    expect(isChunkLoadError(new Error("Request failed with status 500"))).toBe(
      false
    )
  })

  it("tolerates a thrown value that is not an Error", () => {
    expect(isChunkLoadError(undefined)).toBe(false)
    expect(isChunkLoadError({ message: "Importing a module script failed." })).toBe(
      true
    )
  })
})

describe("recoverFromChunkLoadError", () => {
  beforeEach(() => {
    stubSessionStorage()
    runningEntry("index-old.js")
  })

  afterEach(() => {
    document.head.innerHTML = ""
  })

  it("reloads once for a build it has not yet recovered from", () => {
    const reload = vi.fn()

    expect(recoverFromChunkLoadError(reload)).toBe(true)
    expect(reload).toHaveBeenCalledOnce()
  })

  it("refuses a second reload on the same build so it cannot loop", () => {
    const reload = vi.fn()

    recoverFromChunkLoadError(reload)
    // The reload landed on the same entry, so the deployment never changed and
    // reloading again would spin forever. The caller must show the fault.
    expect(recoverFromChunkLoadError(reload)).toBe(false)
    expect(reload).toHaveBeenCalledOnce()
  })

  it("recovers again once a deploy changes the module entry", () => {
    const reload = vi.fn()

    recoverFromChunkLoadError(reload)
    runningEntry("index-new.js")

    expect(recoverFromChunkLoadError(reload)).toBe(true)
    expect(reload).toHaveBeenCalledTimes(2)
  })

  it("answers canRecover without spending the guard or reloading", () => {
    const reload = vi.fn()

    // The error boundary calls this during render, so it must stay pure —
    // otherwise deciding what to show would itself trigger the navigation.
    expect(canRecoverFromChunkLoadError()).toBe(true)
    expect(canRecoverFromChunkLoadError()).toBe(true)
    expect(reload).not.toHaveBeenCalled()

    recoverFromChunkLoadError(reload)
    expect(canRecoverFromChunkLoadError()).toBe(false)
  })
})
