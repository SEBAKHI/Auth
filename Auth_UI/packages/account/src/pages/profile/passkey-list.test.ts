import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it, vi } from "vitest"

vi.mock("@authsystem/api/client", () => ({ api: {} }))

import { PASSKEY_NAME_MAX_CODE_POINTS } from "./passkey-list"

/**
 * The add dialog refuses a long name before the device asks for a fingerprint,
 * since the server checks it only after the ceremony, having spent the challenge.
 * The server does not publish its limit, so this holds the two together: a limit
 * that moved there would fail here, not surprise a person with a spent ceremony.
 */
function userPasskeySource(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, "Auth/Auth.Domain/Entities/UserPasskey.cs")
    if (existsSync(candidate)) return readFileSync(candidate, "utf8")
    dir = dirname(dir)
  }
  throw new Error("UserPasskey.cs not found from the working directory")
}

describe("the longest passkey name", () => {
  it("is the server's", () => {
    const declared = /MaxNameCodePoints\s*=\s*(\d+)\s*;/.exec(userPasskeySource())

    expect(declared, "the C# constant moved: update this test's pattern").not.toBeNull()
    expect(PASSKEY_NAME_MAX_CODE_POINTS).toBe(Number(declared?.[1]))
  })
})
