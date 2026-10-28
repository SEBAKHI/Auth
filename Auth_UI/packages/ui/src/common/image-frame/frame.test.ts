import { describe, expect, it } from "vitest"

import {
  MAX_MARGIN,
  MAX_OUTPUT_EDGE_PX,
  MAX_ZOOM,
  MIN_OUTPUT_EDGE_PX,
  clampCrop,
  defaultCrop,
  defaultState,
  framedFileName,
  isUntouched,
  moveEdge,
  nextSmallerEdge,
  outputRect,
  outputSize,
  panCrop,
  renderPlan,
  setZoom,
  stageLayout,
  zoomOf,
  type FrameState,
} from "./frame"

const WIDE = { width: 400, height: 200 }
const TALL = { width: 300, height: 600 }

describe("default frame", () => {
  it("is the whole image for a logo, and saving it is untouched", () => {
    const state = defaultState(WIDE, "logo")
    expect(state.crop).toEqual({ x: 0, y: 0, width: 400, height: 200 })
    expect(state.margin).toBe(0)
    expect(isUntouched(state, WIDE, "logo")).toBe(true)
    expect(outputRect(state)).toEqual(state.crop)
  })

  it("is the square containing the image for a favicon - never a crop", () => {
    const crop = defaultCrop(WIDE, "favicon")
    expect(crop).toEqual({ x: 0, y: -100, width: 400, height: 400 })
    // Nothing of the image is outside it.
    expect(crop.x).toBeLessThanOrEqual(0)
    expect(crop.y + crop.height).toBeGreaterThanOrEqual(WIDE.height)
  })

  it("is the centered short-side square for a photo - what the circle already shows", () => {
    expect(defaultCrop(TALL, "photo")).toEqual({
      x: 0,
      y: 150,
      width: 300,
      height: 300,
    })
  })
})

describe("untouched", () => {
  it("turns false on any move, zoom or margin, and true again on the default", () => {
    const initial = defaultState(WIDE, "logo")
    expect(isUntouched({ ...initial, margin: 0.01 }, WIDE, "logo")).toBe(false)
    const zoomed = setZoom(initial.crop, 2, WIDE, "logo")
    expect(isUntouched({ crop: zoomed, margin: 0 }, WIDE, "logo")).toBe(false)
    const trimmed = moveEdge(initial.crop, "right", -10, WIDE)
    expect(isUntouched({ crop: trimmed, margin: 0 }, WIDE, "logo")).toBe(false)
    // Sub-pixel noise is the same frame.
    expect(
      isUntouched(
        { crop: { ...initial.crop, x: 0.2 }, margin: 0 },
        WIDE,
        "logo"
      )
    ).toBe(true)
  })
})

describe("zoom", () => {
  it("is 1 for the largest crop and scales about the center", () => {
    const crop = defaultCrop(WIDE, "logo")
    expect(zoomOf(crop, WIDE, "logo")).toBe(1)
    const zoomed = setZoom(crop, 2, WIDE, "logo")
    expect(zoomed).toEqual({ x: 100, y: 50, width: 200, height: 100 })
    expect(zoomOf(zoomed, WIDE, "logo")).toBe(2)
  })

  it("is clamped to 1..MAX_ZOOM", () => {
    const crop = defaultCrop(WIDE, "logo")
    expect(zoomOf(setZoom(crop, 0.25, WIDE, "logo"), WIDE, "logo")).toBe(1)
    expect(zoomOf(setZoom(crop, 100, WIDE, "logo"), WIDE, "logo")).toBe(
      MAX_ZOOM
    )
  })

  it("starts above 1 for a photo, and zooming out reaches the containing square", () => {
    const crop = defaultCrop(TALL, "photo")
    expect(zoomOf(crop, TALL, "photo")).toBe(2)
    const out = setZoom(crop, 1, TALL, "photo")
    expect(out.width).toBe(600)
    expect(out.height).toBe(600)
  })
})

describe("1:1", () => {
  it("keeps the crop square whatever it is asked for", () => {
    const squared = clampCrop(
      { x: 0, y: 0, width: 300, height: 100 },
      TALL,
      "photo"
    )
    expect(squared.width).toBe(squared.height)
    expect(squared.width).toBe(300)
  })

  it("keeps the crop's center over the image, so it never holds only margin", () => {
    const crop = defaultCrop(TALL, "photo")
    const moved = panCrop(crop, -10_000, 10_000, TALL, "photo")
    expect(moved.x + moved.width / 2).toBeGreaterThanOrEqual(0)
    expect(moved.y + moved.height / 2).toBeLessThanOrEqual(TALL.height)
  })
})

describe("pan", () => {
  it("stops a logo's crop at the image's edges", () => {
    const crop = setZoom(defaultCrop(WIDE, "logo"), 2, WIDE, "logo")
    expect(panCrop(crop, 1000, 1000, WIDE, "logo")).toEqual({
      x: 200,
      y: 100,
      width: 200,
      height: 100,
    })
    expect(panCrop(crop, -1000, -1000, WIDE, "logo")).toEqual({
      x: 0,
      y: 0,
      width: 200,
      height: 100,
    })
  })
})

describe("free aspect for a logo", () => {
  it("moves one edge and leaves the opposite one where it was", () => {
    const crop = defaultCrop(WIDE, "logo")
    expect(moveEdge(crop, "right", -100, WIDE)).toEqual({
      x: 0,
      y: 0,
      width: 300,
      height: 200,
    })
    expect(moveEdge(crop, "left", 50, WIDE)).toEqual({
      x: 50,
      y: 0,
      width: 350,
      height: 200,
    })
    expect(moveEdge(crop, "top", 20, WIDE)).toEqual({
      x: 0,
      y: 20,
      width: 400,
      height: 180,
    })
    expect(moveEdge(crop, "bottom", -20, WIDE)).toEqual({
      x: 0,
      y: 0,
      width: 400,
      height: 180,
    })
  })

  it("never grows past the image nor shrinks below MAX_ZOOM", () => {
    const crop = defaultCrop(WIDE, "logo")
    expect(moveEdge(crop, "right", 500, WIDE).width).toBe(400)
    expect(moveEdge(crop, "left", -500, WIDE).x).toBe(0)
    expect(moveEdge(crop, "right", -10_000, WIDE).width).toBe(
      WIDE.width / MAX_ZOOM
    )
    expect(moveEdge(crop, "top", 10_000, WIDE).height).toBe(
      WIDE.height / MAX_ZOOM
    )
  })
})

describe("margin", () => {
  it("pads every side by the fraction of the crop's longer edge", () => {
    const state: FrameState = { crop: defaultCrop(WIDE, "logo"), margin: 0.1 }
    expect(outputRect(state)).toEqual({
      x: -40,
      y: -40,
      width: 480,
      height: 280,
    })
  })

  it("is capped at MAX_MARGIN", () => {
    const state: FrameState = { crop: defaultCrop(WIDE, "logo"), margin: 9 }
    expect(outputRect(state).width).toBe(400 + 2 * 400 * MAX_MARGIN)
  })

  it("keeps a square crop square", () => {
    const state: FrameState = { crop: defaultCrop(TALL, "photo"), margin: 0.25 }
    const output = outputRect(state)
    expect(output.width).toBe(output.height)
  })
})

describe("output size and the 2048 cap", () => {
  it("never upscales a small image", () => {
    const image = { width: 64, height: 32 }
    expect(outputSize(defaultState(image, "logo"))).toEqual(image)
  })

  it("brings the longest edge down to 2048, keeping the ratio", () => {
    const image = { width: 6000, height: 3000 }
    expect(outputSize(defaultState(image, "logo"))).toEqual({
      width: MAX_OUTPUT_EDGE_PX,
      height: 1024,
    })
  })

  it("counts the margin in the longest edge", () => {
    const image = { width: 2048, height: 2048 }
    const size = outputSize({ crop: defaultCrop(image, "logo"), margin: 0.5 })
    expect(size).toEqual({ width: 2048, height: 2048 })
  })
})

describe("render plan", () => {
  it("draws the crop into the margin-padded canvas", () => {
    const state: FrameState = { crop: defaultCrop(WIDE, "logo"), margin: 0.1 }
    const plan = renderPlan(state, WIDE)
    expect(plan.canvas).toEqual({ width: 480, height: 280 })
    expect(plan.source).toEqual({ x: 0, y: 0, width: 400, height: 200 })
    expect(plan.dest).toEqual({ x: 40, y: 40, width: 400, height: 200 })
  })

  it("copies only the visible part when the square reaches past the image", () => {
    const plan = renderPlan(defaultState(WIDE, "favicon"), WIDE)
    expect(plan.canvas).toEqual({ width: 400, height: 400 })
    expect(plan.source).toEqual({ x: 0, y: 0, width: 400, height: 200 })
    // Transparent bars of 100 px above and below.
    expect(plan.dest).toEqual({ x: 0, y: 100, width: 400, height: 200 })
  })

  it("maps a zoomed crop onto the whole canvas", () => {
    const crop = setZoom(defaultCrop(WIDE, "logo"), 2, WIDE, "logo")
    const plan = renderPlan({ crop, margin: 0 }, WIDE)
    expect(plan.canvas).toEqual({ width: 200, height: 100 })
    expect(plan.source).toEqual(crop)
    expect(plan.dest).toEqual({ x: 0, y: 0, width: 200, height: 100 })
  })

  it("scales source to canvas when the cap applies", () => {
    const image = { width: 4096, height: 4096 }
    const plan = renderPlan(defaultState(image, "logo"), image)
    expect(plan.canvas).toEqual({ width: 2048, height: 2048 })
    expect(plan.dest).toEqual({ x: 0, y: 0, width: 2048, height: 2048 })
  })

  it("has nothing to draw when the crop misses the image", () => {
    const plan = renderPlan(
      { crop: { x: 500, y: 0, width: 10, height: 10 }, margin: 0 },
      WIDE
    )
    expect(plan.source).toBeNull()
    expect(plan.dest).toBeNull()
  })
})

describe("byte-limit retries", () => {
  it("shrinks by a quarter down to the floor, then stops", () => {
    expect(nextSmallerEdge(2048)).toBe(1536)
    expect(nextSmallerEdge(300)).toBe(MIN_OUTPUT_EDGE_PX)
    expect(nextSmallerEdge(MIN_OUTPUT_EDGE_PX)).toBeNull()
  })
})

describe("file name", () => {
  it("keeps the original's name and ends in .png", () => {
    expect(framedFileName("logo.final.jpeg")).toBe("logo.final.png")
    expect(framedFileName("noext")).toBe("noext.png")
    expect(framedFileName(".png")).toBe("image.png")
  })
})

describe("stage layout", () => {
  it("fits and centers the output inside the padding", () => {
    const layout = stageLayout(defaultState(WIDE, "logo"), WIDE, 100, 10)
    expect(layout.scale).toBe(0.2)
    expect(layout.output).toEqual({ x: 10, y: 30, width: 80, height: 40 })
    expect(layout.image).toEqual(layout.output)
  })

  it("shows the whole image around a zoomed crop", () => {
    const crop = setZoom(defaultCrop(WIDE, "logo"), 2, WIDE, "logo")
    const layout = stageLayout({ crop, margin: 0 }, WIDE, 100, 10)
    expect(layout.crop).toEqual(layout.output)
    expect(layout.image.width).toBeCloseTo(layout.output.width * 2)
    // The crop sits in the middle of the image.
    expect(layout.image.x + layout.image.width / 2).toBeCloseTo(50)
  })
})
