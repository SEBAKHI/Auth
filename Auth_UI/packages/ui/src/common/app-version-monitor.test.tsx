import { render, waitFor } from "@testing-library/react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { detectAvailableAppUpdate, moduleEntryFromHtml } from "./app-version"
import { AppVersionMonitor } from "./app-version-monitor"

const ORIGIN = "http://localhost:3000"
const CURRENT_ENTRY = `${ORIGIN}/assets/index-old.js`
const LATEST_ENTRY = `${ORIGIN}/assets/index-new.js`

function response(
  body: string | null,
  contentType: string,
  status = 200
): Response {
  return new Response(body, {
    status,
    headers: { "content-type": contentType },
  })
}

describe("application version detection", () => {
  it("extracts and normalizes the fingerprinted module entry", () => {
    expect(
      moduleEntryFromHtml(
        '<script type="module" src="/assets/index-new.js"></script>',
        ORIGIN
      )
    ).toBe(LATEST_ENTRY)
  })

  it("reports a new entry only after its JavaScript asset is available", async () => {
    const fetcher = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        response(
          '<script type="module" src="/assets/index-new.js"></script>',
          "text/html"
        )
      )
      .mockResolvedValueOnce(response(null, "application/javascript"))

    await expect(
      detectAvailableAppUpdate(CURRENT_ENTRY, ORIGIN, fetcher)
    ).resolves.toBe(LATEST_ENTRY)
    expect(fetcher).toHaveBeenLastCalledWith(
      LATEST_ENTRY,
      expect.objectContaining({ method: "HEAD", cache: "no-store" })
    )
  })

  it("keeps the current app running when the new asset is unavailable", async () => {
    const fetcher = vi
      .fn<typeof fetch>()
      .mockResolvedValueOnce(
        response(
          '<script type="module" src="/assets/index-new.js"></script>',
          "text/html"
        )
      )
      .mockResolvedValueOnce(response(null, "text/html", 404))

    await expect(
      detectAvailableAppUpdate(CURRENT_ENTRY, ORIGIN, fetcher)
    ).resolves.toBeNull()
  })
})

describe("AppVersionMonitor", () => {
  beforeEach(() => {
    document.head.innerHTML =
      '<script type="module" src="/assets/index-old.js"></script>'
    try {
      window.sessionStorage.clear()
    } catch {
      // The test runtime may disable storage.
    }
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    document.head.innerHTML = ""
  })

  it("requests an automatic reload when a ready deployment is detected", async () => {
    const reload = vi.fn()
    vi.stubGlobal(
      "fetch",
      vi
        .fn<typeof fetch>()
        .mockResolvedValueOnce(
          response(
            '<script type="module" src="/assets/index-new.js"></script>',
            "text/html"
          )
        )
        .mockResolvedValueOnce(response(null, "application/javascript"))
    )

    render(<AppVersionMonitor reload={reload} />)

    await waitFor(() => expect(reload).toHaveBeenCalledOnce())
  })
})
