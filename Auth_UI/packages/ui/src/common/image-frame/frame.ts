/**
 * The geometry of the image frame editor, kept apart from any DOM so every
 * rule is unit-tested.
 *
 * Choosing the frame is ONE operation: the crop box picks the part of the
 * image to keep, and the margin pads it with transparent pixels on every side.
 * The output rectangle (crop + margin) is what gets uploaded, so a frame
 * larger than the image bakes the margin into the pixels and every surface
 * that shows the image (sidebar, sign-in pages, circles, email renditions)
 * shows it with no code of its own.
 *
 * All rectangles are in IMAGE pixels of the decoded, upright image. Image
 * space is physical: an image is never mirrored by the page's writing
 * direction, so "left" here is the image's left in every language.
 *
 * The editor never decides a crop by itself (owner rule 2026-07-13): the
 * default frame is what the surface already shows, and saving it untouched
 * sends the original file unchanged.
 */

export interface Size {
  width: number
  height: number
}

export interface Rect {
  x: number
  y: number
  width: number
  height: number
}

/**
 * - `logo`: free aspect, shown whole (`object-contain`) - application,
 *   organization and platform logos.
 * - `favicon`: 1:1, shown whole - a tab icon is square.
 * - `photo`: 1:1, shown filling a circle (`object-cover`) - people.
 */
export type FrameMode = "logo" | "favicon" | "photo"

export interface FrameState {
  /** The part of the image kept; for 1:1 modes always a square. */
  crop: Rect
  /** Transparent padding on every side, as a fraction of the crop's longer edge. */
  margin: number
}

export const MAX_ZOOM = 8
export const MAX_MARGIN = 0.5
/** Longest edge of the exported image: what `uploadImage` would send anyway. */
export const MAX_OUTPUT_EDGE_PX = 2048
/** The server's default `ImageStorage:MaxSizeBytes` (4 MiB). */
export const MAX_OUTPUT_BYTES = 4 * 1024 * 1024
/** The export never shrinks below this edge to fit the byte limit. */
export const MIN_OUTPUT_EDGE_PX = 256

/** Below this many image pixels, two rectangles are the same frame. */
const SAME_FRAME_PX = 0.5

export function isSquareMode(mode: FrameMode): boolean {
  return mode !== "logo"
}

/**
 * The frame a surface already shows: the whole image for a logo, the square
 * that contains it for a favicon (transparent bars on the short sides, as
 * `object-contain` shows it), and the centered square of the short side for a
 * photo (exactly what an `object-cover` circle shows today).
 */
export function defaultCrop(image: Size, mode: FrameMode): Rect {
  const { width, height } = image
  if (mode === "logo") return { x: 0, y: 0, width, height }
  const side =
    mode === "favicon" ? Math.max(width, height) : Math.min(width, height)
  return { x: (width - side) / 2, y: (height - side) / 2, width: side, height: side }
}

export function defaultState(image: Size, mode: FrameMode): FrameState {
  return { crop: defaultCrop(image, mode), margin: 0 }
}

/**
 * The box the crop must stay inside: the image itself for a logo (a logo's
 * margin comes from the margin slider), and the square containing the image
 * for 1:1 modes, so a wide image can still be framed whole in a square.
 */
export function cropBounds(image: Size, mode: FrameMode): Rect {
  if (!isSquareMode(mode)) {
    return { x: 0, y: 0, width: image.width, height: image.height }
  }
  const side = Math.max(image.width, image.height)
  return {
    x: (image.width - side) / 2,
    y: (image.height - side) / 2,
    width: side,
    height: side,
  }
}

/**
 * How many times the crop fits into its bounds: 1 = the largest crop, up to
 * {@link MAX_ZOOM}. For a logo whose crop changed aspect it is the axis that
 * runs out first.
 */
export function zoomOf(crop: Rect, image: Size, mode: FrameMode): number {
  const bounds = cropBounds(image, mode)
  return Math.min(bounds.width / crop.width, bounds.height / crop.height)
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

/**
 * Brings any crop back inside the rules: square in 1:1 modes, no larger than
 * its bounds, no smaller than {@link MAX_ZOOM} allows, inside its bounds, and
 * with its center over the image so it can never hold nothing but margin.
 */
export function clampCrop(crop: Rect, image: Size, mode: FrameMode): Rect {
  const bounds = cropBounds(image, mode)
  const minWidth = bounds.width / MAX_ZOOM
  const minHeight = bounds.height / MAX_ZOOM
  let width = clamp(crop.width, minWidth, bounds.width)
  let height = clamp(crop.height, minHeight, bounds.height)
  if (isSquareMode(mode)) {
    width = height = clamp(Math.max(width, height), minWidth, bounds.width)
  }

  const centerX = clamp(
    crop.x + crop.width / 2,
    Math.max(bounds.x + width / 2, 0),
    Math.min(bounds.x + bounds.width - width / 2, image.width)
  )
  const centerY = clamp(
    crop.y + crop.height / 2,
    Math.max(bounds.y + height / 2, 0),
    Math.min(bounds.y + bounds.height - height / 2, image.height)
  )
  return { x: centerX - width / 2, y: centerY - height / 2, width, height }
}

/** Scales the crop about its center so that {@link zoomOf} becomes `zoom`. */
export function setZoom(
  crop: Rect,
  zoom: number,
  image: Size,
  mode: FrameMode
): Rect {
  const target = clamp(zoom, 1, MAX_ZOOM)
  const factor = zoomOf(crop, image, mode) / target
  const width = crop.width * factor
  const height = crop.height * factor
  return clampCrop(
    {
      x: crop.x + (crop.width - width) / 2,
      y: crop.y + (crop.height - height) / 2,
      width,
      height,
    },
    image,
    mode
  )
}

/** Moves the crop by image pixels; the clamp stops it at the edges. */
export function panCrop(
  crop: Rect,
  dx: number,
  dy: number,
  image: Size,
  mode: FrameMode
): Rect {
  return clampCrop(
    { ...crop, x: crop.x + dx, y: crop.y + dy },
    image,
    mode
  )
}

export type Edge = "top" | "right" | "bottom" | "left"

/**
 * Moves one edge of a logo's crop by `delta` image pixels (positive = towards
 * the image's right or bottom), the opposite edge staying put. This is what
 * makes a logo's aspect free: trimming one side only. 1:1 modes never call it.
 */
export function moveEdge(
  crop: Rect,
  edge: Edge,
  delta: number,
  image: Size
): Rect {
  const bounds = cropBounds(image, "logo")
  const minWidth = bounds.width / MAX_ZOOM
  const minHeight = bounds.height / MAX_ZOOM
  const right = crop.x + crop.width
  const bottom = crop.y + crop.height
  switch (edge) {
    case "left": {
      const x = clamp(crop.x + delta, 0, right - minWidth)
      return { ...crop, x, width: right - x }
    }
    case "right": {
      const end = clamp(right + delta, crop.x + minWidth, image.width)
      return { ...crop, width: end - crop.x }
    }
    case "top": {
      const y = clamp(crop.y + delta, 0, bottom - minHeight)
      return { ...crop, y, height: bottom - y }
    }
    case "bottom": {
      const end = clamp(bottom + delta, crop.y + minHeight, image.height)
      return { ...crop, height: end - crop.y }
    }
  }
}

export function clampMargin(margin: number): number {
  return clamp(margin, 0, MAX_MARGIN)
}

/** The uploaded rectangle: the crop padded by the margin on every side. */
export function outputRect(state: FrameState): Rect {
  const { crop } = state
  const pad = clampMargin(state.margin) * Math.max(crop.width, crop.height)
  return {
    x: crop.x - pad,
    y: crop.y - pad,
    width: crop.width + 2 * pad,
    height: crop.height + 2 * pad,
  }
}

/**
 * True when the frame is still the default one: saving it must send the
 * original file - no canvas, no re-encode, not a byte changed.
 */
export function isUntouched(
  state: FrameState,
  image: Size,
  mode: FrameMode
): boolean {
  if (state.margin > 0) return false
  const initial = defaultCrop(image, mode)
  return (
    Math.abs(state.crop.x - initial.x) < SAME_FRAME_PX &&
    Math.abs(state.crop.y - initial.y) < SAME_FRAME_PX &&
    Math.abs(state.crop.width - initial.width) < SAME_FRAME_PX &&
    Math.abs(state.crop.height - initial.height) < SAME_FRAME_PX
  )
}

/** Output size in whole pixels: never upscaled, longest edge at most `maxEdge`. */
export function outputSize(
  state: FrameState,
  maxEdge: number = MAX_OUTPUT_EDGE_PX
): Size {
  const output = outputRect(state)
  const scale = Math.min(1, maxEdge / Math.max(output.width, output.height))
  return {
    width: Math.max(1, Math.round(output.width * scale)),
    height: Math.max(1, Math.round(output.height * scale)),
  }
}

export interface RenderPlan {
  canvas: Size
  /** The part of the image to copy, in image pixels; null when none is visible. */
  source: Rect | null
  /** Where that part lands on the canvas, in canvas pixels. */
  dest: Rect | null
}

/**
 * Everything the canvas step needs: the canvas size, and the visible part of
 * the image mapped onto it. Whatever the source does not cover stays
 * transparent - that is the margin.
 */
export function renderPlan(
  state: FrameState,
  image: Size,
  maxEdge: number = MAX_OUTPUT_EDGE_PX
): RenderPlan {
  const output = outputRect(state)
  const canvas = outputSize(state, maxEdge)
  const scaleX = canvas.width / output.width
  const scaleY = canvas.height / output.height

  const left = Math.max(state.crop.x, 0)
  const top = Math.max(state.crop.y, 0)
  const right = Math.min(state.crop.x + state.crop.width, image.width)
  const bottom = Math.min(state.crop.y + state.crop.height, image.height)
  if (right <= left || bottom <= top) {
    return { canvas, source: null, dest: null }
  }

  return {
    canvas,
    source: { x: left, y: top, width: right - left, height: bottom - top },
    dest: {
      x: (left - output.x) * scaleX,
      y: (top - output.y) * scaleY,
      width: (right - left) * scaleX,
      height: (bottom - top) * scaleY,
    },
  }
}

/**
 * The next, smaller longest edge to try when an export is over
 * {@link MAX_OUTPUT_BYTES}, or null when shrinking further would make the
 * image worse than the server's own 1024 px could use.
 */
export function nextSmallerEdge(edge: number): number | null {
  if (edge <= MIN_OUTPUT_EDGE_PX) return null
  return Math.max(MIN_OUTPUT_EDGE_PX, Math.floor(edge * 0.75))
}

/** The exported file's name: the original's, ending in `.png`. */
export function framedFileName(name: string): string {
  const base = name.replace(/\.[^./\\]+$/, "")
  return `${base || "image"}.png`
}

export interface StageLayout {
  /** Stage units per image pixel. */
  scale: number
  /** The output rectangle on the stage. */
  output: Rect
  /** The crop on the stage. */
  crop: Rect
  /** The whole image on the stage. */
  image: Rect
}

/**
 * Lays the frame out on a square stage of `stage` units: the output rectangle
 * fitted and centered inside the padding, the image and the crop placed
 * relative to it. The preview is therefore exactly what will be uploaded, and
 * the rest of the image shows dimmed around it for context.
 */
export function stageLayout(
  state: FrameState,
  image: Size,
  stage: number,
  padding: number
): StageLayout {
  const output = outputRect(state)
  const room = stage - 2 * padding
  const scale = Math.min(room / output.width, room / output.height)
  const originX = (stage - output.width * scale) / 2 - output.x * scale
  const originY = (stage - output.height * scale) / 2 - output.y * scale
  const place = (rect: Rect): Rect => ({
    x: originX + rect.x * scale,
    y: originY + rect.y * scale,
    width: rect.width * scale,
    height: rect.height * scale,
  })
  return {
    scale,
    output: place(output),
    crop: place(state.crop),
    image: place({ x: 0, y: 0, width: image.width, height: image.height }),
  }
}
