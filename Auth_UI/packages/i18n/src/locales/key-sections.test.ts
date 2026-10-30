import { describe, expect, it } from "vitest"

import { ar } from "./ar"
import { en } from "./en"
import { fa } from "./fa"
import { ur } from "./ur"

/**
 * What the key sections promise (OI-264).
 *
 * The console's lines about API and webhook keys are read as facts by the
 * person deciding who gets a key, so each one is pinned to what the code does:
 * - a revoked key is refused only once the application's SDK cache of its last
 *   valid check expires (ApiKeyCacheDuration 60 s, WebhookKeyCacheDuration
 *   5 min by default), so no line may promise an immediate refusal;
 * - a key's scopes reach the application as claims, which the application
 *   enforces, so no line may say this platform holds the key "within" them;
 * - in a right-to-left paragraph the neutral `?` and `=` of `?whk=` take the
 *   paragraph's direction and the token reads `=whk?`, so it is isolated.
 */

describe("the revoke texts", () => {
  it("promise no immediate refusal", () => {
    for (const text of [en.apiKeys.revokeBody, en.webhookKeys.revokeBody]) {
      expect(text).not.toMatch(/immediately/i)
    }
    for (const text of [ar.apiKeys.revokeBody, ar.webhookKeys.revokeBody]) {
      expect(text).not.toContain("فورًا")
    }
  })

  it("name the default delay of each key type", () => {
    expect(en.apiKeys.revokeBody).toContain("by default within 1 minute")
    expect(en.webhookKeys.revokeBody).toContain("by default within 5 minutes")
    expect(ar.apiKeys.revokeBody).toContain("خلال دقيقة واحدة افتراضيًّا")
    expect(ar.webhookKeys.revokeBody).toContain("خلال 5 دقائق افتراضيًّا")
  })
})

describe("the API-key direction line", () => {
  it("leaves the scopes to the application that enforces them", () => {
    expect(en.apiKeys.subtitle).not.toContain("within its scopes")
    expect(en.apiKeys.subtitle).toContain("which your application enforces")
  })
})

describe("the webhook-key direction line", () => {
  it.each([
    ["ar", ar],
    ["fa", fa],
    ["ur", ur],
  ])("isolates ?whk= in %s, so it reads as typed", (_lang, locale) => {
    expect(locale.webhookKeys.subtitle).toContain("⁦?whk=⁩")
  })
})
