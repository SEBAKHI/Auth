import { describe, expect, it } from "vitest"

import { platformFaviconUrl } from "./platform-favicon"

describe("platformFaviconUrl", () => {
  it("is the stable icon under the API's uploads, whatever the trailing slash", () => {
    expect(platformFaviconUrl("https://auth.example.com")).toBe(
      "https://auth.example.com/uploads/images/platform-favicon.png"
    )
    expect(platformFaviconUrl("https://auth.example.com/")).toBe(
      "https://auth.example.com/uploads/images/platform-favicon.png"
    )
  })

  it("is null for an address the HTML cannot name", () => {
    for (const value of [undefined, "", "/api", "not a url", "javascript:alert(1)"]) {
      expect(platformFaviconUrl(value)).toBeNull()
    }
  })
})
