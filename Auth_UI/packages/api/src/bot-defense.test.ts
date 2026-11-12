import { describe, expect, it } from "vitest"

import { normalizeBotDefenseConfig } from "./bot-defense"

/**
 * `GET /Platform/bot-defense` read strictly (S13): whatever the answer, a page
 * either knows a mode and a sitekey or loads nothing from Cloudflare.
 */
describe("normalizeBotDefenseConfig", () => {
  it.each([
    [{ mode: "Enforce", siteKey: "1x00000000000000000000AA" }, { mode: "Enforce", siteKey: "1x00000000000000000000AA" }],
    [{ mode: "Monitor", siteKey: " 0x4AAAAAAA " }, { mode: "Monitor", siteKey: "0x4AAAAAAA" }],
    // Off carries no key, whatever the answer says: nothing is loaded.
    [{ mode: "Off", siteKey: "1x00000000000000000000AA" }, { mode: "Off", siteKey: null }],
    [{ mode: "Enforce", siteKey: "" }, { mode: "Enforce", siteKey: null }],
    [{ mode: "Enforce", siteKey: null }, { mode: "Enforce", siteKey: null }],
    [{ mode: "Enforce", siteKey: 42 }, { mode: "Enforce", siteKey: null }],
    // An unknown mode is no check at all, never a guessed one.
    [{ mode: "enforce", siteKey: "1x00000000000000000000AA" }, { mode: "Off", siteKey: null }],
    [{ mode: 2, siteKey: "1x00000000000000000000AA" }, { mode: "Off", siteKey: null }],
    [{}, { mode: "Off", siteKey: null }],
  ])("reads %o as %o", (raw, expected) => {
    expect(normalizeBotDefenseConfig(raw)).toEqual(expected)
  })
})
