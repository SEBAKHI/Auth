import { afterEach, describe, expect, it, vi } from "vitest"
import {
  MAX_UPLOAD_EDGE_PX,
  prepareImageForUpload,
} from "@authsystem/api/image-downscale"

/**
 * jsdom decodes no images and has no canvas, so the browser primitives are
 * stubbed and the decisions around them are what is under test: which files
 * are touched, the target geometry, the encoder fallback, and the promise that
 * this module can only ever make an upload smaller.
 */

type FakeBitmap = { width: number; height: number; close: () => void }

function makeFile(type: string, bytes = 5_000_000, name = "photo.jpg") {
  return new File([new Uint8Array(bytes)], name, { type })
}

function stubDecoder(bitmap: FakeBitmap | Error) {
  vi.stubGlobal(
    "createImageBitmap",
    vi.fn(async () => {
      if (bitmap instanceof Error) throw bitmap
      return bitmap
    })
  )
}

/** A canvas whose encoder honours `type` unless told to fall back to PNG. */
function stubCanvas(options: {
  encodedBytes: number
  webpSupported?: boolean
}) {
  const drawImage = vi.fn()
  const requestedTypes: string[] = []
  class FakeOffscreenCanvas {
    width: number
    height: number
    constructor(width: number, height: number) {
      this.width = width
      this.height = height
    }
    getContext() {
      return { drawImage }
    }
    async convertToBlob({ type }: { type: string }) {
      requestedTypes.push(type)
      const produced =
        type === "image/webp" && options.webpSupported === false
          ? "image/png"
          : type
      return new Blob([new Uint8Array(options.encodedBytes)], {
        type: produced,
      })
    }
  }
  vi.stubGlobal("OffscreenCanvas", FakeOffscreenCanvas)
  return { drawImage, requestedTypes }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("prepareImageForUpload", () => {
  it("leaves a GIF untouched without even decoding it", async () => {
    const decode = vi.fn()
    vi.stubGlobal("createImageBitmap", decode)
    const file = makeFile("image/gif", 1_000, "anim.gif")

    const result = await prepareImageForUpload(file)

    expect(result).toBe(file)
    expect(decode).not.toHaveBeenCalled()
  })

  it("sends the original when the browser cannot decode images at all", async () => {
    vi.stubGlobal("createImageBitmap", undefined)
    const file = makeFile("image/jpeg")

    expect(await prepareImageForUpload(file)).toBe(file)
  })

  it("sends the original when decoding fails", async () => {
    stubDecoder(new Error("corrupt"))
    const file = makeFile("image/png", 1_000, "bad.png")

    expect(await prepareImageForUpload(file)).toBe(file)
  })

  it("sends a small image byte-for-byte and releases the bitmap", async () => {
    const close = vi.fn()
    stubDecoder({ width: 1024, height: 768, close })
    const { drawImage } = stubCanvas({ encodedBytes: 10 })
    const file = makeFile("image/jpeg", 200_000)

    const result = await prepareImageForUpload(file)

    expect(result).toBe(file)
    expect(drawImage).not.toHaveBeenCalled()
    expect(close).toHaveBeenCalledOnce()
  })

  it("brings a landscape photo down to the edge ceiling, keeps the ratio, encodes WebP", async () => {
    const close = vi.fn()
    stubDecoder({ width: 6000, height: 4000, close })
    const { drawImage, requestedTypes } = stubCanvas({ encodedBytes: 300_000 })
    const file = makeFile("image/jpeg", 9_000_000, "IMG_0001.jpg")

    const result = await prepareImageForUpload(file)

    expect(drawImage).toHaveBeenCalledWith(
      expect.anything(),
      0,
      0,
      MAX_UPLOAD_EDGE_PX,
      1365
    )
    expect(requestedTypes).toEqual(["image/webp"])
    expect(result).not.toBe(file)
    expect(result.type).toBe("image/webp")
    expect(result.name).toBe("IMG_0001.webp")
    expect(result.size).toBe(300_000)
    expect(result.lastModified).toBe(file.lastModified)
    expect(close).toHaveBeenCalledOnce()
  })

  it("scales a portrait on its longest edge", async () => {
    stubDecoder({ width: 3000, height: 6000, close: vi.fn() })
    const { drawImage } = stubCanvas({ encodedBytes: 100 })

    await prepareImageForUpload(makeFile("image/png", 8_000_000, "tall.png"))

    expect(drawImage).toHaveBeenCalledWith(
      expect.anything(),
      0,
      0,
      1024,
      MAX_UPLOAD_EDGE_PX
    )
  })

  it("falls back to JPEG for a photo when the browser cannot encode WebP", async () => {
    stubDecoder({ width: 6000, height: 4000, close: vi.fn() })
    const { requestedTypes } = stubCanvas({
      encodedBytes: 400_000,
      webpSupported: false,
    })

    const result = await prepareImageForUpload(
      makeFile("image/jpeg", 9_000_000)
    )

    expect(requestedTypes).toEqual(["image/webp", "image/jpeg"])
    expect(result.type).toBe("image/jpeg")
    expect(result.name).toBe("photo.jpg")
  })

  it("falls back to PNG for a logo when the browser cannot encode WebP, keeping alpha", async () => {
    stubDecoder({ width: 5000, height: 5000, close: vi.fn() })
    const { requestedTypes } = stubCanvas({
      encodedBytes: 400_000,
      webpSupported: false,
    })

    const result = await prepareImageForUpload(
      makeFile("image/png", 9_000_000, "logo.png")
    )

    expect(requestedTypes).toEqual(["image/webp", "image/png"])
    expect(result.type).toBe("image/png")
  })

  it("never sends more bytes than it was given", async () => {
    stubDecoder({ width: 6000, height: 4000, close: vi.fn() })
    stubCanvas({ encodedBytes: 5_000_001 })
    const file = makeFile("image/jpeg", 5_000_000)

    expect(await prepareImageForUpload(file)).toBe(file)
  })
})
