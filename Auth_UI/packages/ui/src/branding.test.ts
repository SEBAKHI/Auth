import { describe, expect, it } from "vitest"

import { pickLogo, toSignInOptions } from "./branding"

/**
 * OI-280 (T-A4, V5): whether passkeys may run on the sign-in page. A hint the
 * page shapes its first screen by; an absent field (an older API, or a copy
 * cached before it) reads false, the page every visitor saw before.
 * Deliberate break, verified red: the hint defaulted to true.
 */
describe("toSignInOptions: the passkey hint", () => {
  it("reads true only when the payload says true", () => {
    expect(
      toSignInOptions({ codeSignInOffered: true, passkeySignInOffered: true })
        .passkeySignInOffered
    ).toBe(true)
    expect(
      toSignInOptions({ codeSignInOffered: true, passkeySignInOffered: false })
        .passkeySignInOffered
    ).toBe(false)
  })

  it("reads an absent field as false, with or without the code options", () => {
    expect(
      toSignInOptions({ codeSignInOffered: true }).passkeySignInOffered
    ).toBe(false)
    expect(toSignInOptions({}).passkeySignInOffered).toBe(false)
  })

  it("keeps the hint where the payload predates the code options", () => {
    const options = toSignInOptions({ passkeySignInOffered: true })
    expect(options.passkeySignInOffered).toBe(true)
    expect(options.codeSignInOffered).toBe(false)
    expect(options.primaryMethod).toBe("password")
  })
})

describe("pickLogo", () => {
  it("prefers the dark logo in dark mode", () => {
    expect(pickLogo("light.webp", "dark.webp", "dark")).toBe("dark.webp")
  })

  it("prefers the light logo in light mode", () => {
    expect(pickLogo("light.webp", "dark.webp", "light")).toBe("light.webp")
  })

  it("falls back to the light logo in dark mode when no dark logo is set", () => {
    expect(pickLogo("light.webp", null, "dark")).toBe("light.webp")
  })

  it("falls back to the dark logo in light mode when no light logo is set", () => {
    expect(pickLogo(null, "dark.webp", "light")).toBe("dark.webp")
  })

  it("returns null when neither logo is set", () => {
    expect(pickLogo(null, null, "light")).toBeNull()
    expect(pickLogo(null, null, "dark")).toBeNull()
  })
})
