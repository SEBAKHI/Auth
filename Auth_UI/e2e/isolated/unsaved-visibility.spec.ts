import { expect, test, type Locator, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * Which row did I touch, and what was in it before?
 *
 * A section can run to forty rows. An operator changes one of them, scrolls,
 * is interrupted, and comes back to a page that says "Unsaved changes: 1" and
 * nothing else — no way to find the row short of reading every one of them,
 * and no way at all to recall what the value used to be. The count was true
 * and useless.
 *
 * So there are two answers and this file measures both: the row marks itself
 * (a rail on its inline-start edge, driven by `data-dirty`, plus one extra
 * description line naming the previous value), and the bar's count opens a
 * list of every change with the value it came from and the value it is at now.
 *
 * Everything here is measured through the DOM contract those two publish —
 * `data-dirty="true"` on the row, `[data-slot="field-description"]` for the
 * line, `[data-slot="settings-unsaved-bar"]` for the bar — and never through
 * class names. The one exception is deliberate: the rail is asserted in PAINT
 * as well, because `data-dirty` is set in one file and the rule that draws it
 * lives in another, and an attribute nobody can see is not a mark.
 */

test.use({ viewport: { width: 1440, height: 900 } })

const ROW_VERSION = "AAAAAAAAB9E="

const FLAGS = {
  source: "file",
  restartRequired: false,
  isPendingRestart: false,
  readOnly: false,
  sensitive: false,
}

function intField(
  path: string,
  value: number,
  extra: Record<string, unknown> = {}
) {
  return {
    path,
    kind: "int",
    effectiveValue: value,
    baselineValue: value,
    defaultValue: value,
    min: 0,
    max: 100,
    ...FLAGS,
    ...extra,
  }
}

function boolField(
  path: string,
  value: boolean,
  extra: Record<string, unknown> = {}
) {
  return {
    path,
    kind: "bool",
    effectiveValue: value,
    baselineValue: value,
    defaultValue: value,
    ...FLAGS,
    ...extra,
  }
}

function section(
  key: string,
  group: string,
  fields: Record<string, unknown>[]
) {
  return {
    key,
    group,
    editable: true,
    version: 0,
    rowVersion: ROW_VERSION,
    fields,
  }
}

/**
 * Three settings, one of each shape that matters here: an integer whose form
 * value is text (so a previous value can be asserted as the string it is), and
 * two switches, which exist to be the rows that must stay UNMARKED.
 */
function sessionSection() {
  return section("Session", "security", [
    intField("MaxConcurrentSessions", 0),
    boolField("TerminateOldestOnMax", true),
    boolField("TerminateSessionsOnPasswordChange", true),
  ])
}

/**
 * A section whose second setting lives inside a category the first one
 * governs. Switching that category off makes the row's control inert while
 * the VALUE stays in the form — which is exactly the state a change can hide
 * in, and the reason the last test exists.
 */
function passwordSection() {
  return section("Password", "security", [
    boolField("BreachedPasswordCheck:Enabled", true),
    intField("BreachedPasswordCheck:RejectThreshold", 1, { min: 1, max: 100 }),
  ])
}

/**
 * Reads only. Nothing in this file saves: the subject is what the page says
 * about work that has NOT been sent yet, so a PUT arriving at all would mean
 * the test drove the wrong gesture — and the mock answers it with a 404.
 */
async function install(
  page: Page,
  sections: Record<string, unknown>[],
  preferredLanguage = "en"
) {
  await installAuthenticatedApi(
    page,
    ["system-settings:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() !== "/api/v1/admin/system-settings") {
        return false
      }
      await fulfillJson(route, {
        restartPending: false,
        dbOverridesUnavailable: false,
        sections,
      })
      return true
    },
    { preferredLanguage }
  )
}

/** A setting's row. Paths carry colons, so the id goes in an attribute selector. */
function row(page: Page, path: string) {
  return page.locator(`[id="setting-${path}"]`)
}

/** Every row currently marked as changed, whichever section rendered it. */
const dirtyRows = (page: Page) =>
  page.locator('[data-slot="field"][data-dirty="true"]')

const bar = (page: Page) => page.locator('[data-slot="settings-unsaved-bar"]')

/**
 * The control that opens the change list.
 *
 * Found by `aria-haspopup`, which Radix's Popover trigger sets on whatever
 * element it renders — including through `asChild`. That keeps this locator
 * indifferent to whether the trigger IS the count or merely wraps it, which is
 * a presentation decision, while still failing loudly if the list is hung off
 * something that is not a popover at all.
 */
const changesTrigger = (page: Page) =>
  bar(page).locator('[aria-haspopup="dialog"]')

const changesPanel = (page: Page) =>
  page.locator('[data-slot="popover-content"]')

/** One entry per changed setting, each of them activatable. */
const changeItems = (page: Page) =>
  changesPanel(page).locator('[data-slot="item-group"]').getByRole("button")

/**
 * The entry for one setting, addressed by its accessible NAME rather than by
 * the text inside it. A list whose entries are described to a screen reader as
 * "button" three times over is not a list of changes, and the difference
 * between that and this is invisible to a text-matching locator.
 */
const changeItem = (page: Page, label: RegExp) =>
  changesPanel(page).getByRole("button", { name: label })

/**
 * The extra description line a changed row grows, identified by the only thing
 * the contract promises about it: it is a field description and it carries the
 * word for "unsaved". The hint and the bounds are field descriptions too, so
 * the filter is what separates them.
 */
const unsavedLine = (page: Page, path: string) =>
  row(page, path)
    .locator('[data-slot="field-description"]')
    .filter({ hasText: "Unsaved" })

/**
 * Bidi isolates, removed before an assertion reads the text.
 *
 * Every interpolated value on this surface is wrapped in FSI/PDI so a value
 * cannot re-order the Arabic prose around it. They are real characters in the
 * text node — invisible, but present — so `toContainText("was 0")` would never
 * match "was ⁨0⁩". Stripping them here means the assertions state what a reader
 * sees while the isolates stay where they belong.
 */
/**
 * Written as escapes rather than as the characters themselves, deliberately: a
 * literal range here would be four invisible glyphs no reviewer could check
 * and any careless re-encoding could silently empty.
 */
const ISOLATES = /[\u2066-\u2069]/g

async function plainText(locator: Locator): Promise<string> {
  return (await locator.innerText())
    .replace(ISOLATES, "")
    .replace(/\s+/g, " ")
    .trim()
}

/**
 * The bar's accessible name, resolved the way a screen reader resolves it.
 *
 * The count used to be a paragraph and the region was named by pointing at it.
 * Turning that count into a popover trigger is exactly the kind of change that
 * silently drops a landmark's name — and an unnamed region on a sticky bar is
 * a landmark the reader who most needs it cannot identify.
 */
async function barAccessibleName(page: Page): Promise<string> {
  return page.evaluate(() => {
    const region = document.querySelector('[data-slot="settings-unsaved-bar"]')
    if (!region) return ""
    const label = region.getAttribute("aria-label")
    if (label && label.trim()) return label.trim()
    return (region.getAttribute("aria-labelledby") ?? "")
      .split(/\s+/)
      .filter(Boolean)
      .map((id) => document.getElementById(id)?.textContent ?? "")
      .join(" ")
      .replace(/\s+/g, " ")
      .trim()
  })
}

/** Whether the caret ended up inside a given row. */
function focusIsIn(page: Page, path: string) {
  return page.evaluate((id) => {
    const element = document.getElementById(id)
    return Boolean(element && element.contains(document.activeElement))
  }, `setting-${path}`)
}

async function open(page: Page, sectionKey: string, firstPath: string) {
  await page.goto(`/admin/system-settings/${sectionKey}`)
  // A row, not the card: the card paints before its rows are laid out, and
  // every locator below addresses a row.
  await row(page, firstPath).waitFor()
}

test("one edit marks one row and leaves the rest alone", async ({ page }) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  // Nothing is marked before anything is touched. Asserted first, because a
  // mark that is always on says nothing when it comes on.
  await expect(dirtyRows(page)).toHaveCount(0)

  await row(page, "MaxConcurrentSessions").locator("input").fill("7")

  await expect(dirtyRows(page)).toHaveCount(1)
  await expect(row(page, "MaxConcurrentSessions")).toHaveAttribute(
    "data-dirty",
    "true"
  )
  // The value that matters is the string "true", not the presence of the
  // attribute: a bare `data-dirty` serialises to "" and matches no
  // `data-[dirty=true]` rule, which is a mark that exists in the DOM and
  // nowhere on screen. That defect is already shipped one row away, on
  // ReadOnlyFieldRow's `data-disabled`.
  for (const untouched of [
    "TerminateOldestOnMax",
    "TerminateSessionsOnPasswordChange",
  ]) {
    await expect(row(page, untouched)).not.toHaveAttribute("data-dirty", "true")
    await expect(unsavedLine(page, untouched)).toHaveCount(0)
  }
})

test("the mark is a rail the eye can actually find", async ({ page }) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  const target = row(page, "MaxConcurrentSessions")
  const clean = row(page, "TerminateOldestOnMax")

  // Before: the rows carry `border-b` and nothing on the inline start edge.
  expect(
    await clean.evaluate(
      (element) => getComputedStyle(element).borderInlineStartWidth
    )
  ).toBe("0px")

  await target.locator("input").fill("7")

  const painted = await target.evaluate((element) => {
    const style = getComputedStyle(element)
    return {
      width: style.borderInlineStartWidth,
      color: style.borderInlineStartColor,
      // The rule the row has always had, and the colour this repo sets as the
      // default for every border on the page.
      inherited: style.borderBottomColor,
    }
  })

  // The attribute is set in one file and the rule that draws it lives in
  // another. An assertion that read only the attribute would still pass on the
  // day the utility is renamed, dropped, or written in a form the installed
  // Tailwind does not compile — the day the mark stops existing for the reader.
  expect(parseFloat(painted.width)).toBeGreaterThanOrEqual(2)

  // And it is coloured on purpose. An uncoloured border inherits the page's
  // default border colour, which is the same near-invisible hairline the row
  // already draws underneath itself: a rail nobody can pick out of forty rows.
  expect(painted.color).not.toBe(painted.inherited)
})

test("the rail costs the row no width, in either state", async ({ page }) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  // The two ways to draw a border on a row both move it. Reserve the rail on
  // every row and all forty of them lose 2px of content for good — the numbers
  // `settings-measure` asserts and `index.css` derives in a comment. Add it
  // only when the row turns dirty and the label the operator is reading jumps
  // 2px sideways under their own first keystroke.
  //
  // So the rail is added and the inline-start padding pays for it. Both halves
  // are measured here, because either one alone is a bug that ships silently:
  // a clean row is untouched, and the changed row's content does not move.
  const target = row(page, "MaxConcurrentSessions")
  const clean = row(page, "TerminateOldestOnMax")
  const content = (locator: Locator) =>
    locator.locator('[data-slot="field-content"]')

  const cleanBefore = await content(clean).boundingBox()
  const before = await content(target).boundingBox()

  await target.locator("input").fill("7")
  await expect(target).toHaveAttribute("data-dirty", "true")

  const after = await content(target).boundingBox()
  const cleanAfter = await content(clean).boundingBox()

  expect(Math.round(after?.x ?? -1)).toBe(Math.round(before?.x ?? -2))
  expect(Math.round(after?.width ?? -1)).toBe(Math.round(before?.width ?? -2))
  // The neighbours are not collateral: a row that pays for its own rail must
  // not be paying out of the section's shared column either.
  expect(Math.round(cleanAfter?.x ?? -1)).toBe(Math.round(cleanBefore?.x ?? -2))
  expect(Math.round(cleanAfter?.width ?? -1)).toBe(
    Math.round(cleanBefore?.width ?? -2)
  )
})

test("the row says it is unsaved to a reader who cannot see the rail", async ({
  page,
}) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  await row(page, "MaxConcurrentSessions").locator("input").fill("7")

  // The rail is paint and the line is a field description, which the form
  // primitive does NOT put in the control's `aria-describedby` — it reserves
  // that for the hint and the validation message. So the row, which is a
  // `role="group"`, takes the line into its own name: a group's name is
  // announced on entering it, which is the moment the fact is worth having.
  //
  // Resolved the way a reader resolves it, from ids to text, because that is
  // the path that silently breaks — an `aria-labelledby` pointing at an id that
  // no longer exists reads as an unnamed group and looks perfect in the DOM.
  const resolved = await row(page, "MaxConcurrentSessions").evaluate((element) =>
    (element.getAttribute("aria-labelledby") ?? "")
      .split(/\s+/)
      .filter(Boolean)
      .map((id) => document.getElementById(id)?.textContent ?? "")
      .join(" ")
  )
  // Stripped out here rather than in the page: `evaluate` runs in a scope that
  // cannot see this file's constants, and a second copy of the isolate range
  // written inline would be four more invisible characters to keep in step.
  const name = resolved.replace(ISOLATES, "").replace(/\s+/g, " ").trim()
  expect(name).toContain("Maximum concurrent sessions")
  expect(name).toContain("Unsaved")
  expect(name).toContain("was 0")

  // And it goes back to naming the setting alone once the change is undone.
  await row(page, "MaxConcurrentSessions").locator("input").fill("0")
  await expect(row(page, "MaxConcurrentSessions")).toHaveAttribute(
    "aria-labelledby",
    "setting-MaxConcurrentSessions-label"
  )
})

test("the changed row says what it used to hold", async ({ page }) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  await row(page, "MaxConcurrentSessions").locator("input").fill("7")

  const line = unsavedLine(page, "MaxConcurrentSessions")
  await expect(line).toHaveCount(1)
  // The previous value, which is the fact the operator came back for. The
  // control already shows the new one.
  await expect.poll(() => plainText(line)).toContain("was 0")

  // FIRST in the description stack, above the hint. The hint under this
  // particular setting is a hundred and fifty words about how many browsers an
  // ordinary person runs; a line buried under it is a line that was not found,
  // which is the whole complaint this feature answers.
  const descriptions = row(page, "MaxConcurrentSessions").locator(
    '[data-slot="field-description"]'
  )
  await expect(descriptions.first()).toHaveText(/Unsaved/)
})

test("typing the original value back takes the mark away with it", async ({
  page,
}) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  const input = row(page, "MaxConcurrentSessions").locator("input")
  await input.fill("7")
  await expect(dirtyRows(page)).toHaveCount(1)

  // Undo by hand — the fourth way a form goes clean, owned by no button.
  await input.fill("0")

  // A rail and a "was 0" line on a row that holds 0 would be a page lying
  // about its own state, and lying in the direction that costs most: the
  // operator hunts for a change that is not there.
  await expect(dirtyRows(page)).toHaveCount(0)
  await expect(row(page, "MaxConcurrentSessions")).not.toHaveAttribute(
    "data-dirty",
    "true"
  )
  await expect(unsavedLine(page, "MaxConcurrentSessions")).toHaveCount(0)
  // And the bar goes with it: there is nothing left to save.
  await expect(bar(page)).toBeHidden()
})

test("the bar lists every change, from what to what", async ({ page }) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  await row(page, "MaxConcurrentSessions").locator("input").fill("7")
  await row(page, "TerminateSessionsOnPasswordChange").getByRole("switch").click()

  // Named, still. Turning the count into a control is precisely the change
  // that drops a landmark's name without anything failing.
  expect(await barAccessibleName(page)).not.toBe("")

  await changesTrigger(page).click()
  await expect(changesPanel(page)).toBeVisible()

  // Exactly the two that were touched. Sourcing this list from the save
  // payload instead of the form's dirty fields would put a third entry here on
  // any section carrying a customized value nobody edited this session — a
  // list of "your unsaved changes" containing changes that are not yours and
  // not unsaved.
  await expect(changeItems(page)).toHaveCount(2)
  await expect(changeItem(page, /Maximum concurrent sessions/)).toHaveCount(1)
  await expect(
    changeItem(page, /Sign out everywhere on password change/)
  ).toHaveCount(1)

  const entry = await plainText(changeItem(page, /Maximum concurrent sessions/))
  expect(entry).toContain("was 0")
  expect(entry).toContain("now 7")
  // Previous before current, in reading order.
  expect(entry.indexOf("was 0")).toBeLessThan(entry.indexOf("now 7"))
  // And no arrow between them. An arrow is a direction, and a direction drawn
  // between two values inverts its meaning in an RTL paragraph: the same glyph
  // that reads "0 became 7" in English reads "7 became 0" in Arabic.
  expect(entry).not.toMatch(/[→←⟶⟵➜➔>]/)
})

test("an entry in the list is the way back to the row", async ({ page }) => {
  await install(page, [sessionSection()])
  await open(page, "Session", "MaxConcurrentSessions")

  await row(page, "MaxConcurrentSessions").locator("input").fill("7")
  await changesTrigger(page).click()
  await changeItem(page, /Maximum concurrent sessions/).click()

  // The list is a finding aid, not a place to stay: leaving it open would put
  // a panel over the row it just pointed at.
  await expect(changesPanel(page)).toBeHidden()

  // Marked, and asserted before the focus poll because the ring is removed
  // after two seconds and a slower assertion ahead of it would spend that
  // budget. Its presence is also the proof that this reuses the arrival path
  // the `?field=` deep link already ships rather than a second scroll written
  // beside it.
  await expect(row(page, "MaxConcurrentSessions")).toHaveAttribute(
    "data-highlight",
    "true"
  )

  // Landing means being able to type. A popover that closes and hands focus
  // back to its own trigger has moved the eye and left the hands at the bottom
  // of the page.
  await expect.poll(() => focusIsIn(page, "MaxConcurrentSessions")).toBe(true)
})

test("a change inside a switched-off category is still on the list", async ({
  page,
}) => {
  await install(page, [passwordSection()])
  await open(page, "Password", "BreachedPasswordCheck:Enabled")

  // The order is the point: change the value, THEN switch its category off.
  // Gating is a DOM prop on the control and never a change to what is in the
  // form, so the edit survives — inert, invisible, and about to be saved.
  await row(page, "BreachedPasswordCheck:RejectThreshold")
    .locator("input")
    .fill("5")
  await row(page, "BreachedPasswordCheck:Enabled").getByRole("switch").click()
  await expect(
    row(page, "BreachedPasswordCheck:RejectThreshold").locator("input")
  ).toBeDisabled()

  // The row keeps its mark. A disabled control is still a changed setting, and
  // this is the one row in the section whose change cannot be read off the
  // control the operator is looking at.
  await expect(
    row(page, "BreachedPasswordCheck:RejectThreshold")
  ).toHaveAttribute("data-dirty", "true")

  await changesTrigger(page).click()
  const threshold = changeItem(page, /Breach count threshold/)
  await expect(threshold).toHaveCount(1)
  await expect.poll(() => plainText(threshold)).toContain("was 1")

  // Both changes, including the switch that hid the other one.
  await expect(changeItems(page)).toHaveCount(2)
  await expect(changeItem(page, /Breached-password check/)).toHaveCount(1)
})

test("in Arabic the rail is drawn on the right", async ({ page }) => {
  await install(page, [sessionSection()], "ar")
  await open(page, "Session", "MaxConcurrentSessions")
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")

  await row(page, "MaxConcurrentSessions").locator("input").fill("7")
  await expect(row(page, "MaxConcurrentSessions")).toHaveAttribute(
    "data-dirty",
    "true"
  )

  // Measured in PHYSICAL sides on purpose. `border-inline-start` resolves
  // correctly here by definition, so reading it back would only ever restate
  // the assertion; the physical sides are what catches a rail written as
  // `border-l`, which is right in English, invisible against the card edge in
  // Arabic, and identical to the correct version in every screenshot taken in
  // English.
  const sides = await row(page, "MaxConcurrentSessions").evaluate((element) => {
    const style = getComputedStyle(element)
    return { left: style.borderLeftWidth, right: style.borderRightWidth }
  })
  expect(parseFloat(sides.right)).toBeGreaterThanOrEqual(2)
  expect(sides.left).toBe("0px")
})
