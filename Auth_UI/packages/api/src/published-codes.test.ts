import { afterEach, describe, expect, it, vi } from "vitest"

/**
 * The code map is a chunk of its own, loaded on the first failure. These load a
 * fresh instance of the module each time, because the test setup has already
 * loaded the shared one.
 */
async function freshModule() {
  vi.resetModules()
  return import("@authsystem/api/published-codes")
}

afterEach(() => {
  vi.doUnmock("@authsystem/api/error-codes.generated")
  vi.resetModules()
})

describe("loadPublishedErrorCodes", () => {
  it("knows no code until the map has loaded, and every published code after", async () => {
    const codes = await freshModule()

    expect(codes.isPublishedCode("User.NotFound")).toBe(false)

    await codes.loadPublishedErrorCodes()

    expect(codes.isPublishedCode("User.NotFound")).toBe(true)
    expect(codes.publishedOrigin("Http.RateLimited")).toBe("transport")
    expect(codes.publishedOrigin("Email.Required")).toBe("#/email")
    expect(codes.isPublishedCode("System.DatabaseUnavailableException")).toBe(false)
  })

  it("fails closed when the chunk cannot load, and tries again next time", async () => {
    vi.doMock("@authsystem/api/error-codes.generated", () => {
      throw new Error("Failed to fetch dynamically imported module")
    })
    const codes = await freshModule()

    await expect(codes.loadPublishedErrorCodes()).resolves.toBeUndefined()
    expect(codes.isPublishedCode("User.NotFound")).toBe(false)

    vi.doUnmock("@authsystem/api/error-codes.generated")
    await codes.loadPublishedErrorCodes()

    expect(codes.isPublishedCode("User.NotFound")).toBe(true)
  })
})
