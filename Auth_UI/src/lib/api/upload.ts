import { getAccessToken } from "@/lib/auth/token-store"
import { API_BASE_URL } from "@/lib/env"

/**
 * Uploads an image to the generic image endpoint and returns its storage key +
 * composed URL. Uses a raw multipart request (so the browser sets the boundary)
 * with the current bearer token.
 */
export async function uploadImage(
  file: File
): Promise<{ key: string; url: string }> {
  const body = new FormData()
  body.append("file", file)

  const res = await fetch(`${API_BASE_URL}/api/v1/Images`, {
    method: "POST",
    headers: { Authorization: `Bearer ${getAccessToken() ?? ""}` },
    body,
  })

  if (!res.ok) {
    const payload = (await res.json().catch(() => null)) as {
      error?: string
    } | null
    throw new Error(payload?.error ?? "Image upload failed")
  }

  return (await res.json()) as { key: string; url: string }
}
