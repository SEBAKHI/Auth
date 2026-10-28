import {
  MAX_OUTPUT_BYTES,
  MAX_OUTPUT_EDGE_PX,
  framedFileName,
  isUntouched,
  nextSmallerEdge,
  renderPlan,
  type FrameMode,
  type FrameState,
  type RenderPlan,
  type Size,
} from "./frame"

/**
 * The browser half of the frame editor: decoding the chosen file and drawing
 * the framed result. Every decision (what to copy, where, how large) is made
 * by the pure functions in `frame.ts`; this module only executes them.
 */

/**
 * Decodes the file upright: a phone photo stored sideways with an EXIF
 * "rotate" flag comes back the way it is meant to be seen, so the frame is
 * chosen on the picture the user sees. Null when the browser cannot decode it
 * (or has no `createImageBitmap`); the editor then offers the original as is.
 */
export async function decodeImage(file: File): Promise<ImageBitmap | null> {
  if (typeof createImageBitmap !== "function") return null
  try {
    return await createImageBitmap(file, { imageOrientation: "from-image" })
  } catch {
    return null
  }
}

function newCanvas(size: Size): HTMLCanvasElement {
  const canvas = document.createElement("canvas")
  canvas.width = size.width
  canvas.height = size.height
  return canvas
}

/**
 * A data URL of the decoded image for the stage, at most `maxEdge` long.
 * A data URL, not a blob URL: both applications' CSP allows `img-src data:`
 * and not `blob:`. Drawn from the decoded bitmap, so it is upright in every
 * browser whatever it does with EXIF inside SVG.
 */
export function previewUrl(bitmap: ImageBitmap, maxEdge = 1024): string | null {
  const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height))
  const canvas = newCanvas({
    width: Math.max(1, Math.round(bitmap.width * scale)),
    height: Math.max(1, Math.round(bitmap.height * scale)),
  })
  const context = canvas.getContext("2d")
  if (!context) return null
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  // WebP keeps the alpha channel and is a fraction of a PNG's size; a browser
  // that cannot encode it returns PNG, which is fine for a preview.
  return canvas.toDataURL("image/webp", 0.9)
}

export type Encoder = (
  bitmap: ImageBitmap,
  plan: RenderPlan,
  type: "image/png" | "image/webp"
) => Promise<Blob | null>

/** Draws the plan on a transparent canvas and encodes it. */
export const encodeCanvas: Encoder = (bitmap, plan, type) => {
  const canvas = newCanvas(plan.canvas)
  const context = canvas.getContext("2d")
  if (!context) return Promise.resolve(null)
  context.imageSmoothingQuality = "high"
  if (plan.source && plan.dest) {
    context.drawImage(
      bitmap,
      plan.source.x,
      plan.source.y,
      plan.source.width,
      plan.source.height,
      plan.dest.x,
      plan.dest.y,
      plan.dest.width,
      plan.dest.height
    )
  }
  return new Promise((resolve) => canvas.toBlob(resolve, type, 0.9))
}

/**
 * The file to upload for a frame.
 *
 * Untouched, it is the ORIGINAL file object - no canvas, no re-encode, not a
 * byte changed. Otherwise a PNG (transparent margin needs alpha) with the
 * longest edge at most 2048 px. A PNG over the server's 4 MiB limit is tried
 * as WebP (alpha kept), then both again a quarter smaller, down to a floor;
 * past the floor the smallest result goes up and the server decides.
 */
export async function exportFrame(
  input: {
    file: File
    bitmap: ImageBitmap
    image: Size
    mode: FrameMode
    state: FrameState
  },
  encode: Encoder = encodeCanvas
): Promise<File> {
  const { file, bitmap, image, mode, state } = input
  if (isUntouched(state, image, mode)) return file

  let edge: number | null = MAX_OUTPUT_EDGE_PX
  let smallest: Blob | null = null
  while (edge !== null) {
    const plan = renderPlan(state, image, edge)
    for (const type of ["image/png", "image/webp"] as const) {
      const blob = await encode(bitmap, plan, type)
      // A browser that cannot encode WebP hands back PNG again: no gain.
      if (!blob || (type === "image/webp" && blob.type !== type)) continue
      if (blob.size <= MAX_OUTPUT_BYTES) return toFile(blob, file)
      if (!smallest || blob.size < smallest.size) smallest = blob
    }
    edge = nextSmallerEdge(edge)
  }
  if (!smallest) throw new Error("The framed image could not be encoded.")
  return toFile(smallest, file)
}

function toFile(blob: Blob, original: File): File {
  const name =
    blob.type === "image/webp"
      ? framedFileName(original.name).replace(/\.png$/, ".webp")
      : framedFileName(original.name)
  return new File([blob], name, {
    type: blob.type,
    lastModified: original.lastModified,
  })
}
