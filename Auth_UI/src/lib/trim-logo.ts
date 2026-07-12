/**
 * Trims uniform padding (a solid or transparent margin) from a logo image so
 * wordmarks exported on large canvases render at their natural aspect ratio.
 * Runs client-side in the logo upload path only — the server re-encodes but
 * never crops, and profile photos must not be trimmed.
 */

/** Max per-channel difference for a pixel to count as background. */
const COLOR_TOLERANCE = 16
/** Alpha at or below this is treated as fully transparent. */
const ALPHA_TRANSPARENT = 16
/** Padding kept around the detected content, as a fraction of its larger edge. */
const PADDING_RATIO = 0.03

export interface TrimBox {
  x: number
  y: number
  width: number
  height: number
}

function channelsMatch(
  data: Uint8ClampedArray,
  offset: number,
  bg: readonly number[]
): boolean {
  return (
    Math.abs(data[offset] - bg[0]) <= COLOR_TOLERANCE &&
    Math.abs(data[offset + 1] - bg[1]) <= COLOR_TOLERANCE &&
    Math.abs(data[offset + 2] - bg[2]) <= COLOR_TOLERANCE &&
    Math.abs(data[offset + 3] - bg[3]) <= COLOR_TOLERANCE
  )
}

/**
 * Finds the bounding box of content pixels against the background sampled at
 * the four corners. Returns null when the corners disagree (photo-like image,
 * nothing safe to trim) or when trimming would not meaningfully shrink the
 * image. Pure so it can be unit-tested without a canvas.
 */
export function computeTrimBox(
  data: Uint8ClampedArray,
  width: number,
  height: number
): TrimBox | null {
  if (width < 8 || height < 8) return null

  const corner = (x: number, y: number) => {
    const i = (y * width + x) * 4
    return [data[i], data[i + 1], data[i + 2], data[i + 3]] as const
  }
  const corners = [
    corner(0, 0),
    corner(width - 1, 0),
    corner(0, height - 1),
    corner(width - 1, height - 1),
  ]

  const bg = corners[0]
  const transparentBg = bg[3] <= ALPHA_TRANSPARENT
  for (const c of corners.slice(1)) {
    if (transparentBg) {
      if (c[3] > ALPHA_TRANSPARENT) return null
    } else if (
      Math.abs(c[0] - bg[0]) > COLOR_TOLERANCE ||
      Math.abs(c[1] - bg[1]) > COLOR_TOLERANCE ||
      Math.abs(c[2] - bg[2]) > COLOR_TOLERANCE ||
      Math.abs(c[3] - bg[3]) > COLOR_TOLERANCE
    ) {
      return null
    }
  }

  const isBackground = (offset: number) =>
    transparentBg
      ? data[offset + 3] <= ALPHA_TRANSPARENT
      : channelsMatch(data, offset, bg)

  let minX = width
  let minY = height
  let maxX = -1
  let maxY = -1
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (!isBackground((y * width + x) * 4)) {
        if (x < minX) minX = x
        if (x > maxX) maxX = x
        if (y < minY) minY = y
        if (y > maxY) maxY = y
      }
    }
  }
  if (maxX < 0) return null // fully uniform image, nothing to anchor on

  const pad = Math.round(Math.max(maxX - minX + 1, maxY - minY + 1) * PADDING_RATIO)
  const x = Math.max(0, minX - pad)
  const y = Math.max(0, minY - pad)
  const w = Math.min(width - 1, maxX + pad) - x + 1
  const h = Math.min(height - 1, maxY + pad) - y + 1

  // Trimming less than a few percent of the area is not worth a re-encode.
  if (w * h >= width * height * 0.96) return null

  return { x, y, width: w, height: h }
}

/**
 * Returns a cropped copy of the file with its uniform margin removed, or the
 * original file when there is nothing (safe) to trim or decoding fails.
 * Output is PNG to preserve transparency; the server re-encodes to WebP.
 */
export async function trimLogoFile(file: File): Promise<File> {
  try {
    const bitmap = await createImageBitmap(file)
    const canvas = document.createElement("canvas")
    canvas.width = bitmap.width
    canvas.height = bitmap.height
    const ctx = canvas.getContext("2d", { willReadFrequently: true })
    if (!ctx) return file
    ctx.drawImage(bitmap, 0, 0)
    bitmap.close()

    const { data } = ctx.getImageData(0, 0, canvas.width, canvas.height)
    const box = computeTrimBox(data, canvas.width, canvas.height)
    if (!box) return file

    const cropped = document.createElement("canvas")
    cropped.width = box.width
    cropped.height = box.height
    const croppedCtx = cropped.getContext("2d")
    if (!croppedCtx) return file
    croppedCtx.drawImage(
      canvas,
      box.x, box.y, box.width, box.height,
      0, 0, box.width, box.height
    )

    const blob = await new Promise<Blob | null>((resolve) =>
      cropped.toBlob(resolve, "image/png")
    )
    if (!blob) return file

    const name = `${file.name.replace(/\.[^.]+$/, "")}.png`
    return new File([blob], name, { type: "image/png" })
  } catch {
    return file
  }
}
