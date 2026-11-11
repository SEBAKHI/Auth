import { webcrypto } from "node:crypto"
import { beforeAll, describe, expect, it, vi } from "vitest"

import {
  base64Url,
  buildAuthorizeUrl,
  GOOGLE_AUTHORIZE_URL,
  googleLocale,
  isGoogleCallbackMessage,
  pkceChallenge,
  randomBase64Url,
  readCallback,
} from "./google-code-flow"

/**
 * S02 PR D: the pure parts of the Google sign-in's code flow, over Node's WebCrypto (the
 * same `crypto.subtle` a browser has; jsdom's `crypto` has none).
 *
 * Deliberate breaks, each verified red: the challenge computed over the verifier's base64url
 * decoding instead of its ASCII; padding left on base64url; one more authorize parameter
 * (`access_type=offline`); `prompt` dropped; a message without a state accepted.
 */
beforeAll(() => {
  vi.stubGlobal("crypto", webcrypto)
})

describe("PKCE (RFC 7636)", () => {
  it("derives the S256 challenge of Appendix B's verifier", async () => {
    // RFC 7636 Appendix B: the example verifier and its challenge.
    await expect(pkceChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")).resolves.toBe(
      "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"
    )
  })

  it("encodes base64url without padding (RFC 4648 §5)", () => {
    expect(base64Url(new Uint8Array([0xfb, 0xff]))).toBe("-_8")
    expect(base64Url(new Uint8Array([0x00]))).toBe("AA")
  })

  it("makes a fresh 43-character unreserved value each time", () => {
    const values = new Set(Array.from({ length: 50 }, () => randomBase64Url()))
    expect(values.size).toBe(50)
    for (const value of values) expect(value).toMatch(/^[A-Za-z0-9_-]{43}$/)
  })
})

describe("the authorize URL", () => {
  const request = {
    clientId: "test-client.apps.googleusercontent.com",
    redirectUri: "https://accounts.example.com/google-callback.html",
    state: "the-state",
    nonce: "server-nonce",
    codeChallenge: "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
    locale: "ar",
  }

  it("goes to Google's authorization endpoint with exactly the ten parameters", () => {
    const url = new URL(buildAuthorizeUrl(request))

    expect(`${url.origin}${url.pathname}`).toBe(GOOGLE_AUTHORIZE_URL)
    expect(Object.fromEntries(url.searchParams)).toEqual({
      client_id: "test-client.apps.googleusercontent.com",
      redirect_uri: "https://accounts.example.com/google-callback.html",
      response_type: "code",
      scope: "openid email profile",
      state: "the-state",
      nonce: "server-nonce",
      code_challenge: "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
      code_challenge_method: "S256",
      prompt: "select_account",
      hl: "ar",
    })
    expect([...url.searchParams.keys()]).toHaveLength(10)
  })

  it("maps the page language to Google's two-letter hl", () => {
    expect(googleLocale("zh-CN")).toBe("zh")
    expect(googleLocale("AR")).toBe("ar")
  })
})

describe("the callback's message", () => {
  it("reads the code or Google's error, each with the state it echoed", () => {
    expect(readCallback("?state=s1&code=4%2F0Ab&scope=email")).toEqual({
      type: "google-code",
      state: "s1",
      code: "4/0Ab",
    })
    expect(readCallback("?error=access_denied&state=s2")).toEqual({
      type: "google-error",
      state: "s2",
      error: "access_denied",
    })
  })

  it("reads nothing without a state, or without a code or an error", () => {
    expect(readCallback("?code=abc")).toBeNull()
    expect(readCallback("?state=s")).toBeNull()
    expect(readCallback("")).toBeNull()
  })

  it("recognizes only the two message shapes", () => {
    expect(isGoogleCallbackMessage({ type: "google-code", state: "s", code: "c" })).toBe(true)
    expect(isGoogleCallbackMessage({ type: "google-error", state: "s", error: "x" })).toBe(true)
    expect(isGoogleCallbackMessage({ type: "google-code", state: "", code: "c" })).toBe(false)
    expect(isGoogleCallbackMessage({ type: "google-code", state: "s" })).toBe(false)
    expect(isGoogleCallbackMessage({ type: "other", state: "s", code: "c" })).toBe(false)
    expect(isGoogleCallbackMessage("google-code")).toBe(false)
    expect(isGoogleCallbackMessage(null)).toBe(false)
  })
})
