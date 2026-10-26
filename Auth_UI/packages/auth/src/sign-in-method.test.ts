import { beforeEach, describe, expect, it } from "vitest"

import type { SignInOptions } from "@authsystem/ui/branding"

import {
  challengeEnded,
  FULL_PROOF_KEY,
  isFullProofRequested,
  LAST_SIGN_IN_METHOD_KEY,
  primarySignInMethod,
  readLastSignInMethod,
  rememberMethodForChallenge,
  requestFullProof,
  signInEnded,
} from "./sign-in-method"

/**
 * D-215-4: the sign-in page remembers, on this browser, the method that last
 * ended in a session — one word, never the address — and the "sign in again"
 * dialog's add-only reason rides through the sign-out in this tab only.
 */

const OPTIONS: SignInOptions = {
  codeSignInOffered: true,
  primaryMethod: "code",
  selfRegistrationOpen: true,
  passwordAtSignUp: false,
  nameAtSignUp: false,
}

/** Every value in a storage area, whatever its key. */
function storedValues(storage: Storage): string[] {
  const values: string[] = []
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index)
    if (key !== null) values.push(storage.getItem(key) ?? "")
  }
  return values
}

/** Makes both storage accessors throw; returns the undo. */
function blockStorage(): () => void {
  const names = ["localStorage", "sessionStorage"] as const
  const own = names.map((name) => Object.getOwnPropertyDescriptor(window, name))
  for (const name of names) {
    Object.defineProperty(window, name, {
      configurable: true,
      get() {
        throw new DOMException("blocked", "SecurityError")
      },
    })
  }
  return () => {
    names.forEach((name, index) => {
      const descriptor = own[index]
      if (descriptor) Object.defineProperty(window, name, descriptor)
      else delete (window as unknown as Record<string, unknown>)[name]
    })
  }
}

describe("the remembered sign-in method", () => {
  beforeEach(() => {
    window.localStorage.clear()
    window.sessionStorage.clear()
  })

  it("puts the deployment's method first when nothing is remembered", () => {
    expect(primarySignInMethod(OPTIONS, null)).toBe("code")
    expect(primarySignInMethod({ ...OPTIONS, primaryMethod: "password" }, null)).toBe(
      "password"
    )
  })

  it("puts the remembered method first while the options offer it", () => {
    expect(primarySignInMethod(OPTIONS, "password")).toBe("password")
    expect(primarySignInMethod({ ...OPTIONS, primaryMethod: "password" }, "code")).toBe(
      "code"
    )
  })

  it("ignores a remembered code where no code can be sent", () => {
    const emailOff = { ...OPTIONS, codeSignInOffered: false }

    expect(primarySignInMethod(emailOff, "code")).toBe("password")
    expect(primarySignInMethod(emailOff, null)).toBe("password")
  })

  it("stores one word and nothing about the account", () => {
    signInEnded("code")

    expect(readLastSignInMethod()).toBe("code")
    expect(window.localStorage.getItem(LAST_SIGN_IN_METHOD_KEY)).toBe("code")
    // The whole browser storage holds the word, and nothing else this module
    // wrote: no address, no account.
    expect(storedValues(window.localStorage)).toEqual(["code"])
    expect(storedValues(window.sessionStorage)).toEqual([])
  })

  it("reads anything but the two words as nothing remembered", () => {
    window.localStorage.setItem(LAST_SIGN_IN_METHOD_KEY, "jane@one.example")

    expect(readLastSignInMethod()).toBeNull()
  })

  it("falls back to the deployment's setting when storage cannot be reached", () => {
    // A browser that blocks site data throws on the accessor itself.
    const restore = blockStorage()
    try {
      expect(() => signInEnded("password")).not.toThrow()
      expect(() => requestFullProof()).not.toThrow()
      expect(readLastSignInMethod()).toBeNull()
      expect(isFullProofRequested()).toBe(false)
      expect(primarySignInMethod(OPTIONS, readLastSignInMethod())).toBe("code")
    } finally {
      restore()
    }
  })

  it("remembers a challenged sign-in's method only once its challenge ends in a session", () => {
    rememberMethodForChallenge("challenge-1", "code")

    expect(readLastSignInMethod()).toBeNull()
    challengeEnded("another-challenge")
    expect(readLastSignInMethod()).toBeNull()

    challengeEnded("challenge-1")
    expect(readLastSignInMethod()).toBe("code")
  })
})

describe("the full-proof reason", () => {
  beforeEach(() => {
    window.localStorage.clear()
    window.sessionStorage.clear()
  })

  it("lives in this tab's session storage until a sign-in ends", () => {
    expect(isFullProofRequested()).toBe(false)

    requestFullProof()
    expect(window.sessionStorage.getItem(FULL_PROOF_KEY)).toBe("1")
    expect(isFullProofRequested()).toBe(true)

    signInEnded("code")
    expect(isFullProofRequested()).toBe(false)
  })
})
