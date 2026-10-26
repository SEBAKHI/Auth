import { beforeEach, describe, expect, it, vi } from "vitest"

const { post } = vi.hoisted(() => ({ post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  api: { POST: (...args: unknown[]) => post(...args) },
}))

import { startSignUpCode, toEmailCodeNextStep } from "./email-code"

const STARTED = {
  pendingId: "handle-1",
  maskedEmail: "j***@one.example",
  expiresAt: "2099-09-10T10:05:00.000Z",
}
const ok = (data: object) => ({ data, response: new Response(null, { status: 200 }) })

describe("startSignUpCode", () => {
  beforeEach(() => post.mockReset())

  it("starts through the sign-in page's start while it sends codes", async () => {
    post.mockResolvedValue(ok(STARTED))

    const pending = await startSignUpCode("jane@one.example", "en")

    expect(pending).toEqual({ ...STARTED, email: "jane@one.example" })
    expect(post.mock.calls.map(([path]) => path)).toEqual([
      "/api/v1/Auth/email-code/start",
    ])
  })

  it("falls back to the registration's start while email sending is off", async () => {
    post.mockImplementation(async (path: string) =>
      path === "/api/v1/Auth/email-code/start"
        ? { error: { status: 403, code: "User.EmailCodeSignInUnavailable" } }
        : ok(STARTED)
    )

    const pending = await startSignUpCode("jane@one.example", "en")

    expect(pending.pendingId).toBe("handle-1")
    expect(post.mock.calls).toEqual([
      [
        "/api/v1/Auth/email-code/start",
        { body: { email: "jane@one.example", preferredLanguage: "en" } },
      ],
      [
        "/api/v1/Auth/registration/start",
        { body: { email: "jane@one.example", preferredLanguage: "en" } },
      ],
    ])
  })

  it("does not fall back for any other refusal", async () => {
    post.mockResolvedValue({ error: { status: 429, code: "RateLimit.Exceeded" } })

    await expect(startSignUpCode("jane@one.example", "en")).rejects.toMatchObject({
      status: 429,
    })
    expect(post).toHaveBeenCalledTimes(1)
  })
})

describe("toEmailCodeNextStep", () => {
  it("reads the server's step, and guesses no step it does not know", () => {
    expect(toEmailCodeNextStep({ step: "completeAccount", passwordRequired: false, nameRequired: true })).toEqual({
      step: "completeAccount",
      passwordRequired: false,
      nameRequired: true,
    })
    expect(toEmailCodeNextStep({ step: "somethingNew" })).toBeNull()
    expect(toEmailCodeNextStep(null)).toBeNull()
  })

  it("asks for the fields when the server does not say", () => {
    expect(toEmailCodeNextStep({ step: "completeAccount" })).toMatchObject({
      passwordRequired: true,
      nameRequired: true,
    })
  })
})
