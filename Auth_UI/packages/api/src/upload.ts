import { ensureFreshAccessToken, sharedRefresh } from "@authsystem/api/client"
import { prepareImageForUpload } from "@authsystem/api/image-downscale"
import { getRefreshToken } from "@authsystem/api/token-store"
import { API_BASE_URL } from "@authsystem/api/env"
import i18n from "@authsystem/i18n"

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
  // Shrink in the browser first: the server keeps 1024 px at most and refuses
  // anything over its byte and megapixel limits, so a phone photo sent as-is
  // was rejected for exceeding what the server was about to discard anyway.
  // This is the single upload path, so every surface gets it. The original is
  // sent unchanged whenever shrinking is impossible or would not save bytes.
  const prepared = await prepareImageForUpload(file)

  const body = new FormData()
  body.append("file", prepared, prepared.name)

  const send = (token: string | null) =>
    fetch(`${API_BASE_URL}/api/v1/Images`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token ?? ""}`,
        "Accept-Language": i18n.language,
      },
      body,
    })

  const token = await ensureFreshAccessToken()
  let res = await send(token)

  // Retry only when we actually presented a token. A 401 on a request that
  // carried none is a foregone conclusion, and refreshing here would spend the
  // same dead refresh token a second time — which the server reports as reuse
  // and answers by revoking every session the account has.
  if (res.status === 401 && token && getRefreshToken() && (await sharedRefresh())) {
    res = await send(await ensureFreshAccessToken())
  }

  if (!res.ok) {
    const payload = (await res.json().catch(() => null)) as {
      error?: string
    } | null
    throw new Error(
      payload?.error ?? i18n.t("errors.uploadFailed", { status: res.status })
    )
  }

  return (await res.json()) as { key: string; url: string }
}
