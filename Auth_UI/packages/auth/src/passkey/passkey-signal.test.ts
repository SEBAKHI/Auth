import { afterEach, describe, expect, it, vi } from "vitest"

import { isApplePlatform, signalAcceptedPasskeys } from "./passkey-signal"

/**
 * S20 PR C, IN 12 (owner decision D-S20-7): after every fresh read of the
 * passkey list, the browser is told which passkeys the account still accepts,
 * on every device but Apple's (OI-279), and only with a user handle, where the
 * browser has the call. A hint: nothing waits on it, and it never throws.
 */

const WINDOWS = {
  userAgent:
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36",
  userAgentData: { platform: "Windows" },
}

const ACCEPTED = { rpId: "localhost", userHandle: "dXNlci1oYW5kbGU", credentialIds: ["a1", "b2"] }

function withSignal(signal: unknown) {
  vi.stubGlobal("PublicKeyCredential", Object.assign(function PublicKeyCredential() {}, {
    signalAllAcceptedCredentials: signal,
  }))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("isApplePlatform", () => {
  it.each([
    ["an iPhone", "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"],
    ["an iPad", "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"],
    ["an iPod", "Mozilla/5.0 (iPod touch; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148"],
    ["a Mac, or an iPad in desktop mode", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15"],
  ])("is true for %s by its user agent", (_label, userAgent) => {
    expect(isApplePlatform({ userAgent })).toBe(true)
  })

  it.each(["macOS", "iOS"])("is true where the client hints say %s", (platform) => {
    expect(isApplePlatform({ userAgent: WINDOWS.userAgent, userAgentData: { platform } })).toBe(true)
  })

  it("believes the client hints over the user agent", () => {
    expect(
      isApplePlatform({ userAgent: "Mozilla/5.0 (Macintosh)", userAgentData: { platform: "Windows" } })
    ).toBe(false)
  })

  it.each([
    ["Windows", WINDOWS],
    ["Android", { userAgent: "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Mobile Safari/537.36" }],
    ["Linux", { userAgent: "Mozilla/5.0 (X11; Linux x86_64; rv:140.0) Gecko/20100101 Firefox/140.0" }],
  ])("is false on %s", (_label, nav) => {
    expect(isApplePlatform(nav)).toBe(false)
  })

  it("falls back to the user agent when the hints are empty", () => {
    expect(
      isApplePlatform({ userAgent: "Mozilla/5.0 (Macintosh)", userAgentData: { platform: "" } })
    ).toBe(true)
  })
})

describe("signalAcceptedPasskeys", () => {
  it("tells the browser the accepted ids, with the RP ID and the user handle", () => {
    const signal = vi.fn().mockResolvedValue(undefined)
    withSignal(signal)

    signalAcceptedPasskeys(ACCEPTED, WINDOWS)

    expect(signal).toHaveBeenCalledTimes(1)
    expect(signal).toHaveBeenCalledWith({
      rpId: "localhost",
      userId: "dXNlci1oYW5kbGU",
      allAcceptedCredentialIds: ["a1", "b2"],
    })
  })

  it("says an empty list too: the account has no passkey left, on purpose", () => {
    const signal = vi.fn().mockResolvedValue(undefined)
    withSignal(signal)

    signalAcceptedPasskeys({ ...ACCEPTED, credentialIds: [] }, WINDOWS)

    expect(signal).toHaveBeenCalledWith(expect.objectContaining({ allAcceptedCredentialIds: [] }))
  })

  it.each([
    ["an iPhone", { userAgent: "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)" }],
    ["an iPad", { userAgent: "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)" }],
    ["a Mac", { userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)" }],
    ["client hints that say macOS", { userAgent: WINDOWS.userAgent, userAgentData: { platform: "macOS" } }],
  ])("tells nothing on %s (OI-279)", (_label, nav) => {
    const signal = vi.fn().mockResolvedValue(undefined)
    withSignal(signal)

    signalAcceptedPasskeys(ACCEPTED, nav)

    expect(signal).not.toHaveBeenCalled()
  })

  it.each([
    ["no user handle", { ...ACCEPTED, userHandle: null }],
    ["an empty user handle", { ...ACCEPTED, userHandle: "" }],
    ["no RP ID", { ...ACCEPTED, rpId: "" }],
  ])("tells nothing with %s", (_label, accepted) => {
    const signal = vi.fn().mockResolvedValue(undefined)
    withSignal(signal)

    signalAcceptedPasskeys(accepted, WINDOWS)

    expect(signal).not.toHaveBeenCalled()
  })

  it("does nothing where the browser lacks the call, or WebAuthn", () => {
    withSignal(undefined)
    expect(() => signalAcceptedPasskeys(ACCEPTED, WINDOWS)).not.toThrow()

    vi.unstubAllGlobals()
    expect(() => signalAcceptedPasskeys(ACCEPTED, WINDOWS)).not.toThrow()
  })

  it("stays silent when the browser refuses or throws", async () => {
    const rejecting = vi.fn().mockRejectedValue(new Error("refused"))
    withSignal(rejecting)
    expect(() => signalAcceptedPasskeys(ACCEPTED, WINDOWS)).not.toThrow()
    await Promise.resolve()

    withSignal(() => {
      throw new Error("thrown")
    })
    expect(() => signalAcceptedPasskeys(ACCEPTED, WINDOWS)).not.toThrow()
  })
})
