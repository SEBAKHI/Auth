import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * OI-280 (T-A3, D7, V7): the sign-in page's question after the address, and the
 * four things the page makes of its answer. Only the route's 404 means passkeys
 * do not run here; every other failure, and silence past three seconds, is
 * `unknown`, which the page treats as if it had never asked.
 *
 * Deliberate break, verified red: the 404 read as `unknown` (the page would then
 * keep asking while passkeys are off).
 */

const mocks = vi.hoisted(() => ({ post: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({ api: { POST: mocks.post } }))

import { PASSKEY_LOOKUP_TIMEOUT_MS, lookUpPasskey } from "./passkey-lookup"

const PATH = "/api/v1/auth/passkeys/lookup"

function answer(status: number, data?: unknown) {
  return {
    data,
    error: status >= 400 ? { status } : undefined,
    response: { status } as Response,
  }
}

beforeEach(() => {
  mocks.post.mockReset()
})

afterEach(() => {
  vi.useRealTimers()
})

describe("lookUpPasskey", () => {
  it("asks the route for the address, with a signal it can be given up by", async () => {
    mocks.post.mockResolvedValue(answer(200, { passkey: false }))

    await lookUpPasskey("ada@example.com")

    expect(mocks.post).toHaveBeenCalledTimes(1)
    const [path, init] = mocks.post.mock.calls[0] as [
      string,
      { body: unknown; signal: AbortSignal },
    ]
    expect(path).toBe(PATH)
    expect(init.body).toEqual({ email: "ada@example.com" })
    expect(init.signal).toBeInstanceOf(AbortSignal)
  })

  it("is `passkey` for 200 true and `none` for 200 false", async () => {
    mocks.post.mockResolvedValueOnce(answer(200, { passkey: true }))
    mocks.post.mockResolvedValueOnce(answer(200, { passkey: false }))

    expect(await lookUpPasskey("ada@example.com")).toBe("passkey")
    expect(await lookUpPasskey("bea@example.com")).toBe("none")
  })

  it("is `off` for the route's 404, and only for it", async () => {
    mocks.post.mockResolvedValue(answer(404))

    expect(await lookUpPasskey("ada@example.com")).toBe("off")
  })

  it.each([
    ["a 500", () => Promise.resolve(answer(500))],
    ["a 429", () => Promise.resolve(answer(429))],
    ["a 400", () => Promise.resolve(answer(400))],
    ["a 403", () => Promise.resolve(answer(403))],
    [
      "a 200 without the boolean",
      () => Promise.resolve(answer(200, { passkey: "yes" })),
    ],
    ["a 200 with no body", () => Promise.resolve(answer(200))],
    [
      "a network failure",
      () => Promise.reject(new TypeError("Failed to fetch")),
    ],
  ])("is `unknown` for %s", async (_, reply) => {
    mocks.post.mockImplementation(reply)

    expect(await lookUpPasskey("ada@example.com")).toBe("unknown")
  })

  it("is `unknown` once three seconds pass without an answer, and gives the request up", async () => {
    vi.useFakeTimers()
    let signal: AbortSignal | undefined
    // A transport that never settles, abort or not: the deadline alone decides.
    mocks.post.mockImplementation(
      (_path: string, init: { signal: AbortSignal }) => {
        signal = init.signal
        return new Promise(() => {})
      }
    )

    let settled: string | undefined
    void lookUpPasskey("ada@example.com").then((outcome) => (settled = outcome))

    await vi.advanceTimersByTimeAsync(PASSKEY_LOOKUP_TIMEOUT_MS - 1)
    expect(settled).toBeUndefined()
    expect(signal?.aborted).toBe(false)

    await vi.advanceTimersByTimeAsync(1)
    expect(settled).toBe("unknown")
    expect(signal?.aborted).toBe(true)
    expect(PASSKEY_LOOKUP_TIMEOUT_MS).toBe(3_000)
  })

  it("keeps an answer that comes in time, and leaves no timer behind", async () => {
    vi.useFakeTimers()
    mocks.post.mockResolvedValue(answer(200, { passkey: true }))

    expect(await lookUpPasskey("ada@example.com")).toBe("passkey")
    expect(vi.getTimerCount()).toBe(0)
  })
})
