import { describe, expect, it, vi } from "vitest"

/**
 * The account bar's frames never load the accounts app's API client. Its import joins the
 * session's cross-tab sync, and a request through it refreshes the accounts session first:
 * inside a linked application that would rotate the person's refresh token behind their
 * accounts tabs. The client and the session that wraps it fail to import here, so a frame
 * module that reaches either, however indirectly, fails this test.
 */
vi.mock("@authsystem/api/client", () => {
  throw new Error("the account bar's frames must not import @authsystem/api/client")
})
vi.mock("@authsystem/auth/auth-context", () => {
  throw new Error("the account bar's frames must not import @authsystem/auth/auth-context")
})

describe("the account bar's frames", () => {
  it.each(["./bar-main", "./panel-main"])("%s loads without the API client", async (entry) => {
    vi.doMock("./embed-boot", async (original) => ({
      ...(await original<typeof import("./embed-boot")>()),
      // The entry's own call; the frame itself is not under test here.
      bootEmbed: vi.fn(async () => undefined),
    }))
    await expect(import(entry)).resolves.toBeDefined()
    // A cold import of the whole frame (React, Radix, the tiles): seconds under a full run.
  }, 20_000)
})
