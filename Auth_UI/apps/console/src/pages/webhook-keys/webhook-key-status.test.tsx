import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import "@authsystem/i18n"
import type { Schemas } from "@authsystem/api/types"
import { formatDateTime } from "@authsystem/ui/format"

import { WebhookKeyStatus } from "./webhook-key-status"

type WebhookKey = Schemas["WebhookKeyDto"]

function webhookKey(overrides: Partial<WebhookKey>): WebhookKey {
  return {
    id: "6f2c1e40-0000-0000-0000-000000000001",
    applicationId: "6f2c1e40-0000-0000-0000-000000000002",
    name: "Billing events",
    keyPrefix: "whk_ab12",
    targetUrl: "https://hooks.example.com/billing",
    environment: "production",
    createdAt: "2026-10-01T08:00:00Z",
    isRevoked: false,
    needsReissue: false,
    ...overrides,
  } as WebhookKey
}

describe("WebhookKeyStatus", () => {
  it("marks a key stored under an old HMAC key, with the date it stops working unused", () => {
    const reissueBy = "2027-01-30T12:00:00Z"
    render(<WebhookKeyStatus webhookKey={webhookKey({ needsReissue: true, reissueBy })} />)

    expect(screen.getByText("Active")).toBeInTheDocument()
    expect(screen.getByText("Old HMAC key")).toBeInTheDocument()
    expect(
      screen.getByText(
        `Its next use re-fingerprints it; unused, it stops working on ${formatDateTime(reissueBy)}.`
      )
    ).toBeInTheDocument()
  })

  it("says to rotate a key whose old HMAC key is gone", () => {
    render(<WebhookKeyStatus webhookKey={webhookKey({ needsReissue: true, reissueBy: null })} />)

    expect(
      screen.getByText("The HMAC key it was fingerprinted under is gone: rotate it.")
    ).toBeInTheDocument()
  })

  it("shows no re-issue badge for a key under the issuing HMAC key", () => {
    render(<WebhookKeyStatus webhookKey={webhookKey({ needsReissue: false })} />)

    expect(screen.queryByText("Old HMAC key")).not.toBeInTheDocument()
  })
})
