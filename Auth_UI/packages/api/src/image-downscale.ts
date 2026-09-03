/**
 * Downscale an image in the browser before it is uploaded.
 *
 * The server keeps at most `ImageStorage:MaxEdgePx` (1024 px) on the longest
 * edge and re-encodes everything to WebP, yet it refuses anything over
 * `ImageStorage:MaxSizeBytes` (4 MiB) or `ImageStorage:MaxMegapixels`
 * (24 MP) — so a phone photo was rejected for being larger than what the
 * server was about to throw away anyway, with an unlocalised error about
 * bytes and megapixels. Every upload passes through `uploadImage`, so shrinking
 * here fixes all three upload surfaces at once, and the server's limits stay
 * where they are as the backstop for anything that bypasses the applications.
 *
 * Rules, in order:
 * - Only JPEG, PNG and WebP are touched. GIF passes through (the server
 *   flattens it to one frame already; re-encoding here would lose nothing new
 *   but would also gain nothing). Unknown types pass through — the server
 *   decides.
 * - The image is decoded honouring its EXIF orientation, so a phone photo that
 *   is "sideways in the bytes, upright by a flag" stays upright.
 * - Nothing is ever cropped: the longest edge is brought down to
 *   `MAX_UPLOAD_EDGE_PX` and the other edge follows the aspect ratio.
 * - Output is WebP (keeps transparency). If the browser cannot encode WebP it
 *   falls back to JPEG for JPEG sources and PNG for the rest, so a logo keeps
 *   its alpha channel and a photo does not balloon into a PNG.
 * - If the result is not smaller than the original, or anything fails, the
 *   original file is sent unchanged. This module can only ever reduce bytes.
 */

/** Longest edge sent to the server: twice what it keeps, as a quality margin. */
export const MAX_UPLOAD_EDGE_PX = 2048

const DOWNSCALED_TYPES = new Set(["image/jpeg", "image/png", "image/webp"])
const OUTPUT_QUALITY = 0.9

export async function prepareImageForUpload(
  file: File,
  maxEdgePx: number = MAX_UPLOAD_EDGE_PX
): Promise<File> {
  if (!DOWNSCALED_TYPES.has(file.type)) return file
  if (typeof createImageBitmap !== "function") return file

  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: "from-image" })
  } catch {
    return file
  }

  try {
    const { width, height } = bitmap
    const longest = Math.max(width, height)
    if (width === 0 || height === 0 || longest <= maxEdgePx) return file

    const scale = maxEdgePx / longest
    const targetWidth = Math.max(1, Math.round(width * scale))
    const targetHeight = Math.max(1, Math.round(height * scale))

    const blob = await encode(bitmap, targetWidth, targetHeight, file.type)
    if (!blob || blob.size >= file.size) return file

    return new File([blob], renameForType(file.name, blob.type), {
      type: blob.type,
      lastModified: file.lastModified,
    })
  } catch {
    return file
  } finally {
    bitmap.close()
  }
}

async function encode(
  source: ImageBitmap,
  width: number,
  height: number,
  sourceType: string
): Promise<Blob | null> {
  const webp = await draw(source, width, height, "image/webp")
  if (webp?.type === "image/webp") return webp

  // The browser could not encode WebP (it hands back PNG instead). Keep alpha
  // for sources that may carry it; keep bytes small for photos.
  const fallback = sourceType === "image/jpeg" ? "image/jpeg" : "image/png"
  return draw(source, width, height, fallback)
}

async function draw(
  source: ImageBitmap,
  width: number,
  height: number,
  type: string
): Promise<Blob | null> {
  if (typeof OffscreenCanvas === "function") {
    const canvas = new OffscreenCanvas(width, height)
    const context = canvas.getContext("2d")
    if (!context) return null
    context.drawImage(source, 0, 0, width, height)
    return canvas.convertToBlob({ type, quality: OUTPUT_QUALITY })
  }

  if (typeof document === "undefined") return null
  const canvas = document.createElement("canvas")
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext("2d")
  if (!context) return null
  context.drawImage(source, 0, 0, width, height)
  return new Promise((resolve) => canvas.toBlob(resolve, type, OUTPUT_QUALITY))
}

function renameForType(name: string, type: string): string {
  const extension =
    type === "image/webp"
      ? ".webp"
      : type === "image/jpeg"
        ? ".jpg"
        : type === "image/png"
          ? ".png"
          : ""
  if (!extension) return name
  return name.replace(/\.[^.]+$/, "") + extension
}
