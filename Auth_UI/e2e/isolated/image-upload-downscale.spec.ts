import { expect, test } from "@playwright/test"

import { en } from "../../packages/i18n/src/locales/en"
import { firstMultipartPart, solidPng } from "./image-bytes"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The proof that a large image is shrunk in the browser BEFORE it leaves it.
 *
 * jsdom decodes nothing, so the unit tests can only check the decisions around
 * the browser primitives. This runs the real thing: a 3000x3000 PNG goes into
 * the avatar picker of the production console build, and the multipart body
 * that reaches the (mocked) API is opened and measured. The assertion that
 * carries the meaning is the decoded WebP geometry - 2048 on the longest edge,
 * ratio kept - read straight out of the bytes Chromium produced.
 */

const USER_ID = "0f8fad5b-d9cb-469f-a165-70867728950e"

const USER = {
  id: USER_ID,
  email: "ada@example.test",
  displayName: "Ada Lovelace",
  firstName: "Ada",
  lastName: "Lovelace",
  status: "Active",
  emailConfirmed: true,
  phoneConfirmed: false,
  twoFactorEnabled: false,
  preferredLanguage: "en",
  timeZone: "UTC",
  createdAt: "2026-08-01T09:00:00Z",
}

/** Width and height from a WebP container, whichever of its three bitstreams it uses. */
function webpDimensions(bytes: Buffer): { width: number; height: number } {
  expect(bytes.subarray(0, 4).toString("ascii")).toBe("RIFF")
  expect(bytes.subarray(8, 12).toString("ascii")).toBe("WEBP")
  const chunk = bytes.subarray(12, 16).toString("ascii")
  if (chunk === "VP8 ") {
    // Lossy: frame tag (3), start code 9d 01 2a (3), then 14-bit width and height.
    return {
      width: bytes.readUInt16LE(26) & 0x3fff,
      height: bytes.readUInt16LE(28) & 0x3fff,
    }
  }
  if (chunk === "VP8L") {
    // Lossless: signature byte 0x2f then 14 bits width-1, 14 bits height-1.
    const bits = bytes.readUInt32LE(21)
    return { width: (bits & 0x3fff) + 1, height: ((bits >>> 14) & 0x3fff) + 1 }
  }
  if (chunk === "VP8X") {
    // Extended: 24-bit canvas width-1 and height-1.
    return {
      width: bytes.readUIntLE(24, 3) + 1,
      height: bytes.readUIntLE(27, 3) + 1,
    }
  }
  throw new Error(`unexpected WebP chunk "${chunk}"`)
}

test("a photo far larger than the server keeps is shrunk in the browser before upload", async ({
  page,
}) => {
  const uploads: Buffer[] = []

  await installAuthenticatedApi(
    page,
    ["users:read", "users:update", "users:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === `/api/v1/users/${USER_ID}`) {
        await fulfillJson(route, USER)
        return true
      }
      if (path === "/api/v1/images" && route.request().method() === "POST") {
        uploads.push(route.request().postDataBuffer()!)
        await fulfillJson(route, {
          key: "images/isolated.webp",
          url: "http://localhost:4175/uploads/images/isolated.webp",
        })
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/profile-image`) {
        await route.fulfill({ status: 204 })
        return true
      }
      if (path === `/api/v1/users/${USER_ID}/organizations`) {
        await fulfillJson(route, [])
        return true
      }
      // Everything else gets the helper's distinctive 404: the detail page
      // tolerates a missing side panel, but not a body of the wrong shape.
      return false
    }
  )

  await page.goto(`/users/${USER_ID}`)
  await expect(
    page.getByRole("heading", { name: "Ada Lovelace" })
  ).toBeVisible()

  // 3000 px on each edge: 9 megapixels, 36 MB decoded, well past the 2048 px
  // ceiling - yet small enough on disk to pass the picker's own 10 MB gate,
  // so the shrink step is the only thing standing between it and the server.
  const source = solidPng(3000, 3000)
  expect(source.length).toBeLessThan(10 * 1024 * 1024)

  await page
    .locator('input[type="file"]')
    .setInputFiles({
      name: "IMG_0001.png",
      mimeType: "image/png",
      buffer: source,
    })

  // The frame editor opens first; left as it is, it hands on the original
  // file, so the shrink below is still the only change made to it.
  const frame = page.getByRole("dialog", { name: en.imageFrame.title })
  await frame.getByRole("button", { name: en.common.save, exact: true }).click()

  await expect.poll(() => uploads.length, { timeout: 15_000 }).toBe(1)

  const part = firstMultipartPart(uploads[0])
  expect(part.headers).toMatch(/content-type:\s*image\/webp/i)
  expect(part.headers).toMatch(/filename="IMG_0001\.webp"/)

  // The geometry read from the bytes Chromium actually produced.
  expect(webpDimensions(part.bytes)).toEqual({ width: 2048, height: 2048 })

  // And it can only have got smaller. Floors, not just ceilings: an empty
  // buffer must not pass as "smaller".
  expect(part.bytes.length).toBeGreaterThan(64)
  expect(part.bytes.length).toBeLessThan(source.length)
})
