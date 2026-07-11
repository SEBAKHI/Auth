import { ensureFreshAccessToken, sharedRefresh } from "@/lib/api/client"
import { getRefreshToken } from "@/lib/auth/token-store"
import { API_BASE_URL } from "@/lib/env"

/**
 * Uploads an image to the generic image endpoint and returns its storage key +
 * composed URL. Uses a raw multipart request (so the browser sets the boundary)
 * with the current bearer token. Because this bypasses the openapi-fetch
 * middleware, it must mirror its token handling itself: refresh a stale token
 * before sending, and retry once if the server still rejects it (e.g. revoked).
 */
export async function uploadImage(
  file: File
): Promise<{ key: string; url: string }> {
  const body = new FormData()
  body.append("file", file)

  const send = (token: string | null) =>
    fetch(`${API_BASE_URL}/api/v1/Images`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token ?? ""}` },
      body,
    })

  let res = await send(await ensureFreshAccessToken())

  if (res.status === 401 && getRefreshToken() && (await sharedRefresh())) {
    res = await send(await ensureFreshAccessToken())
  }

  if (!res.ok) {
    const payload = (await res.json().catch(() => null)) as {
      error?: string
    } | null
    throw new Error(payload?.error ?? `Image upload failed (HTTP ${res.status})`)
  }

  return (await res.json()) as { key: string; url: string }
}
