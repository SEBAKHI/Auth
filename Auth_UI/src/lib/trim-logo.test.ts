import { describe, expect, it } from "vitest"

import { computeTrimBox } from "./trim-logo"

/** Builds RGBA pixel data for a solid background with an optional content rect. */
function makeImage(
  width: number,
  height: number,
  bg: [number, number, number, number],
  content?: { x: number; y: number; w: number; h: number; color: [number, number, number, number] }
): Uint8ClampedArray {
  const data = new Uint8ClampedArray(width * height * 4)
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const inContent =
        content &&
        x >= content.x &&
        x < content.x + content.w &&
        y >= content.y &&
        y < content.y + content.h
      const [r, g, b, a] = inContent ? content.color : bg
      const i = (y * width + x) * 4
      data[i] = r
      data[i + 1] = g
      data[i + 2] = b
      data[i + 3] = a
    }
  }
  return data
}

describe("computeTrimBox", () => {
  it("finds the content box on a white background", () => {
    const data = makeImage(100, 100, [255, 255, 255, 255], {
      x: 30, y: 45, w: 40, h: 10, color: [0, 0, 0, 255],
    })

    const box = computeTrimBox(data, 100, 100)

    expect(box).not.toBeNull()
    // 3% padding of the 40px content edge, rounded = 1px
    expect(box).toEqual({ x: 29, y: 44, width: 42, height: 12 })
  })

  it("finds the content box on a transparent background", () => {
    const data = makeImage(100, 100, [0, 0, 0, 0], {
      x: 10, y: 10, w: 20, h: 20, color: [10, 10, 10, 255],
    })

    const box = computeTrimBox(data, 100, 100)

    expect(box).not.toBeNull()
    expect(box!.width).toBeLessThan(30)
    expect(box!.height).toBeLessThan(30)
  })

  it("returns null when the corners disagree (photo-like image)", () => {
    const data = makeImage(100, 100, [255, 255, 255, 255], {
      x: 0, y: 0, w: 50, h: 100, color: [0, 0, 0, 255], // left half dark -> corners differ
    })

    expect(computeTrimBox(data, 100, 100)).toBeNull()
  })

  it("returns null when there is nothing meaningful to trim", () => {
    const data = makeImage(100, 100, [255, 255, 255, 255], {
      x: 1, y: 1, w: 98, h: 98, color: [0, 0, 0, 255],
    })

    expect(computeTrimBox(data, 100, 100)).toBeNull()
  })

  it("returns null for a fully uniform image", () => {
    const data = makeImage(50, 50, [255, 255, 255, 255])

    expect(computeTrimBox(data, 50, 50)).toBeNull()
  })

  it("returns null for tiny images", () => {
    const data = makeImage(4, 4, [255, 255, 255, 255])

    expect(computeTrimBox(data, 4, 4)).toBeNull()
  })
})
