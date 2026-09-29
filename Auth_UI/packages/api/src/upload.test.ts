import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

/**
 * The multipart upload bypasses the client middleware, so it re-implements the
 * 401 rule itself. In cookie mode there is no readable refresh token, so the
 * retry must be conditioned on "there is a session" (hasSession), not on reading
 * a token — or every upload after an access token is revoked fails for good.
 */

vi.mock("@authsystem/i18n", () => ({
  default: { language: "en", t: (key: string) => key },
}))

// Canvas downscaling is irrelevant here and jsdom has no canvas.
vi.mock("@authsystem/api/image-downscale", () => ({
  prepareImageForUpload: async (file: File) => file,
}))

const SENTINEL = "__cookie__"

function accessToken(sequence: number): string {
  const payload = btoa(JSON.stringify({ jti: `a${sequence}`, exp: Math.floor(Date.now() / 1000) + 900 }))
  return `h.${payload.replace(/=+$/, "")}.s`
}

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } })
}

let uploads: (string | null)[]
let refreshes: number

function installServer(options: { rejectFirstUpload: boolean }) {
  let rejected = !options.rejectFirstUpload
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: unknown, init?: RequestInit) => {
      const url = typeof input === "string" ? input : String((input as { url?: string }).url)
      if (url.includes("/Auth/refresh")) {
        refreshes += 1
        return json(200, { accessToken: accessToken(refreshes), refreshToken: SENTINEL })
      }
      uploads.push(new Headers(init?.headers as HeadersInit).get("Authorization"))
      if (!rejected) {
        rejected = true
        return json(401, { code: "Http.Unauthenticated" })
      }
      return json(200, { key: "k", url: "https://cdn.example.com/k" })
    })
  )
}

async function load() {
  vi.resetModules()
  const tokenStore = await import("@authsystem/api/token-store")
  const upload = await import("@authsystem/api/upload")
  return { tokenStore, upload }
}

beforeEach(() => {
  window.localStorage.clear()
  uploads = []
  refreshes = 0
  Object.defineProperty(navigator, "locks", { value: undefined, configurable: true, writable: true })
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.resetModules()
})

const file = () => new File(["x"], "a.png", { type: "image/png" })

describe("uploadImage in a cookie session", () => {
  it("refreshes and retries once after a 401, although no refresh token is readable", async () => {
    window.localStorage.setItem("auth.refreshToken", SENTINEL)
    installServer({ rejectFirstUpload: true })
    const { tokenStore, upload } = await load()
    tokenStore.setAccessToken(accessToken(0))

    await expect(upload.uploadImage(file())).resolves.toEqual({ key: "k", url: "https://cdn.example.com/k" })

    expect(tokenStore.legacyRefreshToken()).toBeNull()
    // Two sends: the rejected one and the retry the session made possible.
    expect(uploads).toHaveLength(2)
    expect(uploads[1]).toMatch(/^Bearer /)
  })

  it("does not retry when there is no session at all", async () => {
    installServer({ rejectFirstUpload: true })
    const { tokenStore, upload } = await load()
    tokenStore.setAccessToken(accessToken(0))

    await expect(upload.uploadImage(file())).rejects.toBeTruthy()

    expect(uploads).toHaveLength(1)
    expect(refreshes).toBe(0)
  })
})
