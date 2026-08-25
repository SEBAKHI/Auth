import { expect, test, type Page, type Route } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * What the two lines of the delivery log's type cell are, and which of them
 * follows the reader's language.
 *
 * The cell used to lead with the raw code and put the message's SUBJECT under
 * it, where it read like the code's translation. It was not one: a subject is
 * rendered once, at enqueue time, in the RECIPIENT's language and frozen there —
 * so it stayed Arabic in an English console, and no amount of switching the
 * console's language was ever going to move it. Only a rendered page can settle
 * this: every check short of the DOM passes either way, because the payload is
 * identical in both languages.
 *
 * The subject is still worth reading, so it keeps a column of its own — where it
 * is understood as a record of what was sent rather than as a label.
 */

const ARABIC_SUBJECT = "تسجيل دخول جديد من جهاز جديد"

const PAGE_ONE = {
  messages: [
    {
      // The row from the report: sent in Arabic, read by an English console.
      id: "11111111-1111-1111-1111-111111111111",
      notificationTypeCode: "new-device-sign-in",
      channel: "Email",
      recipient: "info@example.test",
      languageCode: "ar",
      subject: ARABIC_SUBJECT,
      status: "Sent",
      attemptCount: 1,
      nextAttemptAt: "2026-08-01T09:00:00Z",
      sentAt: "2026-08-01T09:00:05Z",
      createdAt: "2026-08-01T09:00:00Z",
    },
    {
      // A type this build has never heard of: it must still render as itself.
      id: "22222222-2222-2222-2222-222222222222",
      notificationTypeCode: "quota-exceeded",
      channel: "Email",
      recipient: "ops@example.test",
      languageCode: "en",
      subject: "You have used your quota",
      status: "Sent",
      attemptCount: 1,
      nextAttemptAt: "2026-08-01T10:00:00Z",
      sentAt: "2026-08-01T10:00:02Z",
      createdAt: "2026-08-01T10:00:00Z",
    },
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 25,
  totalPages: 1,
}

function installOutboxApi(page: Page, preferredLanguage: string) {
  return installAuthenticatedApi(
    page,
    ["notification-templates:read"],
    async (route: Route, url: URL) => {
      if (url.pathname.toLowerCase() === "/api/v1/notification-outbox") {
        await fulfillJson(route, PAGE_ONE)
        return true
      }
      await fulfillJson(route, { items: [], totalCount: 0 })
      return true
    },
    { preferredLanguage }
  )
}

/** Opens the delivery log and waits for the rows to be on the page. */
async function openDeliveryLog(page: Page, preferredLanguage: string) {
  await installOutboxApi(page, preferredLanguage)
  await page.goto("/notifications/outbox")
  await expect(page.getByText("info@example.test")).toBeVisible()
}

/**
 * The cell of one row under one column HEADING, found by the heading's position
 * rather than by a fixed index — so the assertion keeps meaning if the columns
 * are ever reordered, and fails loudly if the heading disappears.
 */
async function cellUnder(page: Page, rowText: string, heading: string) {
  const headers = page.getByRole("columnheader")
  const count = await headers.count()
  let index = -1
  for (let i = 0; i < count; i++) {
    if ((await headers.nth(i).innerText()).trim() === heading) {
      index = i
      break
    }
  }
  expect(index, `no column headed "${heading}"`).toBeGreaterThan(-1)

  return page
    .getByRole("row")
    .filter({ hasText: rowText })
    .getByRole("cell")
    .nth(index)
}

test("the type cell leads with the name and carries the code beneath it", async ({
  page,
}) => {
  await openDeliveryLog(page, "en")

  const cell = await cellUnder(page, "new-device-sign-in", "Type")
  const lines = (await cell.innerText())
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)

  // Order is the whole point: the audit table reads name-then-code, and these
  // two screens are read side by side.
  expect(lines).toEqual(["Sign-in from a new device", "new-device-sign-in"])
})

test("the name follows the console's language, not the message's", async ({
  page,
}) => {
  await openDeliveryLog(page, "ar")

  const cell = await cellUnder(page, "new-device-sign-in", "النوع")
  await expect(cell).toContainText("تسجيل دخول من جهاز جديد")
  await expect(cell).toContainText("new-device-sign-in")
})

test("the subject is a column of its own, in the language it was sent in", async ({
  page,
}) => {
  await openDeliveryLog(page, "en")

  const subject = await cellUnder(page, "new-device-sign-in", "Subject")
  await expect(subject).toHaveText(ARABIC_SUBJECT)

  // The subject is a record of a send, so it keeps the direction of the language
  // it was sent in even while the console is read left to right.
  await expect(subject.locator("bdi")).toHaveAttribute("dir", "rtl")

  const type = await cellUnder(page, "new-device-sign-in", "Type")
  await expect(type).not.toContainText(ARABIC_SUBJECT)
})

test("a type this build does not know renders as its own code", async ({
  page,
}) => {
  await openDeliveryLog(page, "en")

  const cell = await cellUnder(page, "quota-exceeded", "Type")
  const lines = (await cell.innerText())
    .split("\n")
    .map((line) => line.trim())
    .filter(Boolean)

  // Not a blank, and not an invented name: the string the server stored.
  expect(lines).toEqual(["quota-exceeded", "quota-exceeded"])
})
