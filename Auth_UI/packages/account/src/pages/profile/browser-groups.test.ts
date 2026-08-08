import { describe, expect, it } from "vitest"

import type { Schemas } from "@authsystem/api/types"
import { groupSessionsByBrowser } from "./browser-groups"

type Device = Schemas["KnownDeviceDto"]
type Session = Schemas["SessionDto"]

const chrome: Device = { id: "dev-chrome", deviceName: "Chrome on Windows" }
const firefox: Device = { id: "dev-firefox", deviceName: "Firefox on Windows" }

function session(id: string, knownDeviceId?: string): Session {
  return { id, knownDeviceId }
}

describe("groupSessionsByBrowser", () => {
  it("files each session under the browser that started it", () => {
    const { groups } = groupSessionsByBrowser(
      [chrome, firefox],
      [
        session("s1", "dev-chrome"),
        session("s2", "dev-chrome"),
        session("s3", "dev-firefox"),
      ]
    )

    expect(groups.map((g) => g.sessions.length)).toEqual([2, 1])
  })

  it("keeps a browser with no live sessions", () => {
    // The ledger outlives the credential: a browser signed out of is still one
    // the user has used, and is exactly what they may want to forget.
    const { groups } = groupSessionsByBrowser([chrome], [])

    expect(groups).toHaveLength(1)
    expect(groups[0].sessions).toEqual([])
  })

  it("puts sessions that named no browser in their own bucket", () => {
    const { groups, unattributed } = groupSessionsByBrowser(
      [chrome],
      [session("s1", "dev-chrome"), session("s2")]
    )

    expect(groups[0].sessions.map((s) => s.id)).toEqual(["s1"])
    expect(unattributed.map((s) => s.id)).toEqual(["s2"])
  })

  it("does not hide a session whose browser is missing from the list", () => {
    // Cannot happen while both queries agree, but a session vanishing from a
    // security page is the wrong way to fail.
    const { unattributed } = groupSessionsByBrowser(
      [chrome],
      [session("s1", "dev-chrome"), session("s2", "dev-vanished")]
    )

    expect(unattributed.map((s) => s.id)).toEqual(["s2"])
  })

  it("returns nothing to show when the account has neither", () => {
    const { groups, unattributed } = groupSessionsByBrowser([], [])

    expect(groups).toEqual([])
    expect(unattributed).toEqual([])
  })

  it("treats two browsers on one machine as two groups", () => {
    // The signature covers the browser family, so this is by design — and is
    // why the UI must not promise a row is a physical device.
    const { groups } = groupSessionsByBrowser(
      [chrome, firefox],
      [session("s1", "dev-chrome"), session("s2", "dev-firefox")]
    )

    expect(groups).toHaveLength(2)
    expect(groups.every((g) => g.sessions.length === 1)).toBe(true)
  })
})
