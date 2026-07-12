import { describe, expect, it } from "vitest"

import { pickLogo } from "./branding"

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
