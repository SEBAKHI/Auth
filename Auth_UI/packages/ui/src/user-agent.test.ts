import { describe, expect, it } from "vitest"

import { parseUserAgent } from "./user-agent"

describe("parseUserAgent", () => {
  it("classifies desktop Edge on Windows", () => {
    const ua =
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36 Edg/150.0.0.0"
    expect(parseUserAgent(ua)).toEqual({
      browser: "Microsoft Edge",
      os: "Windows",
      deviceType: "desktop",
    })
  })

  it("classifies Chrome on Android phones as mobile", () => {
    const ua =
      "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Mobile Safari/537.36"
    expect(parseUserAgent(ua)).toEqual({
      browser: "Chrome",
      os: "Android",
      deviceType: "mobile",
    })
  })

  it("classifies Safari on iPhone as mobile", () => {
    const ua =
      "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1"
    expect(parseUserAgent(ua)).toEqual({
      browser: "Safari",
      os: "iOS",
      deviceType: "mobile",
    })
  })

  it("classifies iPad as tablet", () => {
    const ua =
      "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1"
    expect(parseUserAgent(ua).deviceType).toBe("tablet")
  })

  it("classifies Android without Mobile token as tablet", () => {
    const ua =
      "Mozilla/5.0 (Linux; Android 14; SM-X910) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
    expect(parseUserAgent(ua).deviceType).toBe("tablet")
  })

  it("classifies Firefox on macOS as desktop", () => {
    const ua =
      "Mozilla/5.0 (Macintosh; Intel Mac OS X 14.5; rv:127.0) Gecko/20100101 Firefox/127.0"
    expect(parseUserAgent(ua)).toEqual({
      browser: "Firefox",
      os: "macOS",
      deviceType: "desktop",
    })
  })

  it("handles missing input", () => {
    expect(parseUserAgent(null)).toEqual({
      browser: null,
      os: null,
      deviceType: "desktop",
    })
  })
})
