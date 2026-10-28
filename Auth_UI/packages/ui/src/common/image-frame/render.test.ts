import { describe, expect, it, vi } from "vitest"

import {
  MAX_OUTPUT_BYTES,
  MIN_OUTPUT_EDGE_PX,
  defaultState,
  setZoom,
  type FrameState,
  type RenderPlan,
} from "./frame"
import { exportFrame, type Encoder } from "./render"

const IMAGE = { width: 400, height: 200 }
// jsdom decodes nothing; the encoder is what touches pixels, and it is faked.
const BITMAP = {} as ImageBitmap

function original() {
  return new File([new Uint8Array([1, 2, 3])], "brand.jpeg", {
    type: "image/jpeg",
    lastModified: 1_700_000_000_000,
  })
}

function zoomed(): FrameState {
  const initial = defaultState(IMAGE, "logo")
  return { crop: setZoom(initial.crop, 2, IMAGE, "logo"), margin: 0 }
}

function blobOf(size: number, type: string): Blob {
  return new Blob([new Uint8Array(size)], { type })
}

describe("exportFrame", () => {
  it("returns the ORIGINAL file object when the frame is untouched, and never draws", async () => {
    const file = original()
    const encode = vi.fn<Encoder>()
    const result = await exportFrame(
      {
        file,
        bitmap: BITMAP,
        image: IMAGE,
        mode: "logo",
        state: defaultState(IMAGE, "logo"),
      },
      encode
    )
    expect(result).toBe(file)
    expect(encode).not.toHaveBeenCalled()
  })

  it("does the same for an untouched photo and favicon", async () => {
    for (const mode of ["photo", "favicon"] as const) {
      const file = original()
      const encode = vi.fn<Encoder>()
      const result = await exportFrame(
        { file, bitmap: BITMAP, image: IMAGE, mode, state: defaultState(IMAGE, mode) },
        encode
      )
      expect(result).toBe(file)
      expect(encode).not.toHaveBeenCalled()
    }
  })

  it("uploads a PNG named after the original once the frame changed", async () => {
    const plans: RenderPlan[] = []
    const encode: Encoder = async (_bitmap, plan, type) => {
      plans.push(plan)
      return blobOf(100, type)
    }
    const result = await exportFrame(
      { file: original(), bitmap: BITMAP, image: IMAGE, mode: "logo", state: zoomed() },
      encode
    )
    expect(result.type).toBe("image/png")
    expect(result.name).toBe("brand.png")
    expect(result.size).toBe(100)
    expect(plans).toHaveLength(1)
    expect(plans[0].canvas).toEqual({ width: 200, height: 100 })
  })

  it("falls back to WebP, then to a smaller edge, while over the byte limit", async () => {
    const calls: Array<{ edge: number; type: string }> = []
    const encode: Encoder = async (_bitmap, plan, type) => {
      const edge = Math.max(plan.canvas.width, plan.canvas.height)
      calls.push({ edge, type })
      // Over the limit until the second size step, and only as WebP.
      const fits = type === "image/webp" && calls.length >= 4
      return blobOf(fits ? 10 : MAX_OUTPUT_BYTES + 1, type)
    }
    const big = { width: 4000, height: 4000 }
    const state: FrameState = { ...defaultState(big, "logo"), margin: 0.1 }
    const result = await exportFrame(
      { file: original(), bitmap: BITMAP, image: big, mode: "logo", state },
      encode
    )
    expect(calls).toEqual([
      { edge: 2048, type: "image/png" },
      { edge: 2048, type: "image/webp" },
      { edge: 1536, type: "image/png" },
      { edge: 1536, type: "image/webp" },
    ])
    expect(result.type).toBe("image/webp")
    expect(result.name).toBe("brand.webp")
  })

  it("ignores a WebP request the browser answered with PNG", async () => {
    const encode: Encoder = async () => blobOf(MAX_OUTPUT_BYTES + 1, "image/png")
    const big = { width: 4000, height: 4000 }
    const state: FrameState = { ...defaultState(big, "logo"), margin: 0.1 }
    const result = await exportFrame(
      { file: original(), bitmap: BITMAP, image: big, mode: "logo", state },
      encode
    )
    // Nothing fit: the smallest attempt goes up, and the server decides.
    expect(result.type).toBe("image/png")
    expect(result.size).toBe(MAX_OUTPUT_BYTES + 1)
  })

  it("stops shrinking at the floor", async () => {
    const edges = new Set<number>()
    const encode: Encoder = async (_bitmap, plan, type) => {
      edges.add(Math.max(plan.canvas.width, plan.canvas.height))
      return blobOf(MAX_OUTPUT_BYTES + 1, type)
    }
    const big = { width: 4000, height: 4000 }
    const state: FrameState = { ...defaultState(big, "logo"), margin: 0.1 }
    await exportFrame(
      { file: original(), bitmap: BITMAP, image: big, mode: "logo", state },
      encode
    )
    expect(Math.min(...edges)).toBe(MIN_OUTPUT_EDGE_PX)
  })

  it("fails loudly when nothing could be encoded", async () => {
    const encode: Encoder = async () => null
    await expect(
      exportFrame(
        { file: original(), bitmap: BITMAP, image: IMAGE, mode: "logo", state: zoomed() },
        encode
      )
    ).rejects.toThrow()
  })
})
