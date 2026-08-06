import { describe, expect, it } from "vitest"

import { privacyPolicyUrl } from "@authsystem/api/env"

describe("privacyPolicyUrl", () => {
  it("keeps the public notice on the accounts origin", () => {
    expect(privacyPolicyUrl()).toBe("https://localhost:5174/privacy")
    expect(privacyPolicyUrl("ar-SA")).toBe(
      "https://localhost:5174/privacy/ar-SA"
    )
  })
})
