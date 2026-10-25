import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render } from "@testing-library/react"
import * as React from "react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { post, get } = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  SESSION_EXPIRED_EVENT: "auth:session-expired",
  completePendingLogout: async () => undefined,
  api: {
    POST: (...args: unknown[]) => post(...args),
    GET: (...args: unknown[]) => get(...args),
  },
}))

import { clearTokens } from "@authsystem/api/token-store"

import { AuthProvider, useAuth } from "./auth-context"
import {
  clearPendingTwoFactorChallenge,
  getPendingEmailCodeSent,
} from "./pending-challenge"

/**
 * OI-243 T3 (review of #108, F5): the code the server emailed with a challenge
 * (`twoFactorEmailCode`) rides with that challenge's token, in memory, and is
 * dropped with it — so the second step opens in the sent state for that challenge
 * and no other.
 */

const SENT = {
  expiresAt: "2026-10-26T12:10:00Z",
  maskedEmail: "j***e@one.example",
  sendsRemaining: 2,
}

function challenged(token: string, extra: Record<string, unknown> = {}) {
  return {
    data: {
      requiresTwoFactor: true,
      twoFactorChallengeToken: token,
      twoFactorMethods: ["email", "recovery"],
      ...extra,
    },
  }
}

type Auth = ReturnType<typeof useAuth>

function Capture({ onAuth }: { onAuth: (auth: Auth) => void }) {
  const auth = useAuth()
  React.useEffect(() => onAuth(auth), [auth, onAuth])
  return null
}

function renderProvider(): Auth {
  let captured: Auth | null = null
  render(
    <QueryClientProvider client={new QueryClient()}>
      <AuthProvider>
        <Capture
          onAuth={(auth) => {
            captured = auth
          }}
        />
      </AuthProvider>
    </QueryClientProvider>
  )
  expect(captured).not.toBeNull()
  return captured as unknown as Auth
}

describe("the code emailed with a challenge (D-243)", () => {
  beforeEach(() => {
    post.mockReset()
    get.mockReset()
    get.mockResolvedValue({ data: {} })
    clearTokens()
    clearPendingTwoFactorChallenge()
    window.localStorage.clear()
    window.sessionStorage.clear()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it("is kept with its own challenge's token, for that token alone", async () => {
    post.mockResolvedValue(challenged("challenge-1", { twoFactorEmailCode: SENT }))
    const before = Date.now()
    const { login } = renderProvider()

    await act(() => login("jane@one.example", "pw").then(() => undefined))

    const record = getPendingEmailCodeSent("challenge-1")
    expect(record).toMatchObject({
      expiresAt: SENT.expiresAt,
      maskedEmail: SENT.maskedEmail,
      sendsRemaining: 2,
    })
    expect(record!.receivedAt).toBeGreaterThanOrEqual(before)
    expect(getPendingEmailCodeSent("challenge-0")).toBeNull()
  })

  it("is replaced by the next challenge's answer, and gone when it carries none", async () => {
    const { login } = renderProvider()
    post.mockResolvedValueOnce(challenged("challenge-1", { twoFactorEmailCode: SENT }))
    await act(() => login("jane@one.example", "pw").then(() => undefined))
    expect(getPendingEmailCodeSent("challenge-1")).not.toBeNull()

    post.mockResolvedValueOnce(challenged("challenge-2"))
    await act(() => login("jane@one.example", "pw").then(() => undefined))

    expect(getPendingEmailCodeSent("challenge-2")).toBeNull()
    expect(getPendingEmailCodeSent("challenge-1")).toBeNull()
  })

  it("is dropped with the challenge", async () => {
    post.mockResolvedValue(challenged("challenge-1", { twoFactorEmailCode: SENT }))
    const { login } = renderProvider()
    await act(() => login("jane@one.example", "pw").then(() => undefined))

    clearPendingTwoFactorChallenge()

    expect(getPendingEmailCodeSent("challenge-1")).toBeNull()
  })

  it("is not kept when the challenge does not offer the emailed code", async () => {
    post.mockResolvedValue(
      challenged("challenge-1", { twoFactorMethods: ["totp", "recovery"], twoFactorEmailCode: SENT })
    )
    const { login } = renderProvider()
    await act(() => login("jane@one.example", "pw").then(() => undefined))

    expect(getPendingEmailCodeSent("challenge-1")).toBeNull()
  })
})
