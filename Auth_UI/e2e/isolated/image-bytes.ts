import { deflateSync } from "node:zlib"

import { expect, type Page } from "@playwright/test"

/**
 * Image bytes for the upload specs: PNGs built pixel by pixel in Node, and the
 * readers that open what the browser actually sent. Built here rather than
 * stored as fixtures so a test states the picture it needs in one line.
 */

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])

const CRC_TABLE = (() => {
  const table = new Uint32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    table[n] = c >>> 0
  }
  return table
})()

function crc32(bytes: Buffer): number {
  let crc = 0xffffffff
  for (const byte of bytes) crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8)
  return (crc ^ 0xffffffff) >>> 0
}

function pngChunk(type: string, data: Buffer): Buffer {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length)
  const typeBytes = Buffer.from(type, "ascii")
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(Buffer.concat([typeBytes, data])))
  return Buffer.concat([length, typeBytes, data, crc])
}

export type Rgba = [number, number, number, number]

/** A valid RGBA PNG whose every pixel is `pixel(x, y)`. */
export function pngOf(
  width: number,
  height: number,
  pixel: (x: number, y: number) => Rgba
): Buffer {
  const header = Buffer.alloc(13)
  header.writeUInt32BE(width, 0)
  header.writeUInt32BE(height, 4)
  header[8] = 8 // bit depth
  header[9] = 6 // colour type: RGBA
  const rowLength = 1 + width * 4
  const raw = Buffer.alloc(rowLength * height)
  for (let y = 0; y < height; y++) {
    const offset = y * rowLength
    raw[offset] = 0 // filter: none
    for (let x = 0; x < width; x++) {
      const [r, g, b, a] = pixel(x, y)
      raw[offset + 1 + x * 4] = r
      raw[offset + 2 + x * 4] = g
      raw[offset + 3 + x * 4] = b
      raw[offset + 4 + x * 4] = a
    }
  }
  return Buffer.concat([
    PNG_SIGNATURE,
    pngChunk("IHDR", header),
    pngChunk("IDAT", deflateSync(raw, { level: 9 })),
    pngChunk("IEND", Buffer.alloc(0)),
  ])
}

/** One colour everywhere: small on disk, large once decoded. */
export function solidPng(width: number, height: number): Buffer {
  return pngOf(width, height, () => [0x33, 0x66, 0x99, 0xff])
}

/** Width and height straight from a PNG's IHDR chunk. */
export function pngDimensions(bytes: Buffer): { width: number; height: number } {
  expect(bytes.subarray(0, 8).equals(PNG_SIGNATURE), "the bytes must be a PNG").toBe(true)
  return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) }
}

/** The first part of a multipart/form-data body: its header block and its bytes. */
export function firstMultipartPart(body: Buffer): { headers: string; bytes: Buffer } {
  const headerEnd = body.indexOf("\r\n\r\n")
  expect(headerEnd, "the body must be multipart with a header block").toBeGreaterThan(0)
  const headers = body.subarray(0, headerEnd).toString("latin1")
  const boundary = headers.split("\r\n")[0]
  const start = headerEnd + 4
  const end = body.indexOf(`\r\n${boundary}`, start)
  expect(end, "the part must be terminated by the boundary").toBeGreaterThan(start)
  return { headers, bytes: body.subarray(start, end) }
}

/**
 * Decodes the bytes in the page and reads the listed pixels as RGBA. The
 * browser decodes; Node would need a whole PNG decoder to say the same.
 */
export async function readPixels(
  page: Page,
  bytes: Buffer,
  points: Array<[number, number]>
): Promise<Rgba[]> {
  return page.evaluate(
    async ({ base64, points }) => {
      const data = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0))
      const bitmap = await createImageBitmap(new Blob([data]))
      const canvas = new OffscreenCanvas(bitmap.width, bitmap.height)
      const context = canvas.getContext("2d")!
      context.drawImage(bitmap, 0, 0)
      return points.map(
        ([x, y]) => Array.from(context.getImageData(x, y, 1, 1).data) as Rgba
      )
    },
    { base64: bytes.toString("base64"), points }
  )
}
