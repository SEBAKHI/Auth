import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * What happens between pressing Save and believing the page.
 *
 * The defect this file exists for was silent and total: a save that came back
 * 409 refetched the section, the refetch changed the row version, the row
 * version was part of the form's remount key, and the form was destroyed with
 * everything the operator had typed in it. A toast said someone else had
 * changed the section. The work was already gone. Nothing errored, nothing
 * logged, and no assertion anywhere looked at an input after a failed request.
 *
 * So test 1 below is the whole point of the file, and the rest guard the
 * behaviours that had to change around it: the form now decides for itself when
 * to adopt server values, Save and Cancel live in a sticky bar outside the
 * card, an invalid submit says so out loud instead of doing nothing, and a
 * change to a setting that can end the operator's own access asks first.
 *
 * Everything is measured through the DOM contract the components publish —
 * `#setting-<path>` for a row, `[data-slot="settings-unsaved-bar"]` for the
 * bar, `#settings-conflict` for the alert — never through class names.
 */

test.use({ viewport: { width: 1440, height: 900 } })

/** What the section this form last agreed with the server about was stamped with. */
const ROW_VERSION = "AAAAAAAAB9E="
/** What it is stamped with after somebody else saves. */
const OTHER_ROW_VERSION = "AAAAAAAAB9I="

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

function stringField(
  path: string,
  value = "",
  extra: Record<string, unknown> = {}
) {
  return {
    path,
    kind: "string",
    effectiveValue: value,
    baselineValue: value,
    defaultValue: value,
    ...FLAGS,
    ...extra,
  }
}

function enumField(
  path: string,
  value: string,
  allowedValues: string[],
  extra: Record<string, unknown> = {}
) {
  return {
    path,
    kind: "enum",
    effectiveValue: value,
    baselineValue: value,
    defaultValue: value,
    allowedValues,
    ...FLAGS,
    ...extra,
  }
}

function section(
  key: string,
  group: string,
  fields: Record<string, unknown>[],
  extra: Record<string, unknown> = {}
) {
  return {
    key,
    group,
    editable: true,
    version: 0,
    rowVersion: ROW_VERSION,
    fields,
    ...extra,
  }
}

/**
 * The `Session` section as the registry declares it, at whatever value the
 * server is currently holding.
 *
 * `MaxConcurrentSessions` and `TerminateOldestOnMax` are both in
 * `HIGH_IMPACT_PATHS.Session`; `TerminateSessionsOnPasswordChange` is in no
 * registry at all, which is what makes the pair of confirmation tests below a
 * contrast rather than two spellings of the same assertion.
 */
function sessionSection(
  options: {
    max?: number
    source?: string
    restartRequired?: boolean
    rowVersion?: string
  } = {}
) {
  return section(
    "Session",
    "security",
    [
      intField("MaxConcurrentSessions", options.max ?? 0, {
        // The file default never moves; only the effective value does, which is
        // what makes a customized value show its "Customized" badge.
        baselineValue: 0,
        defaultValue: 0,
        source: options.source ?? "file",
        restartRequired: options.restartRequired ?? false,
      }),
      boolField("TerminateOldestOnMax", true),
      boolField("TerminateSessionsOnPasswordChange", true),
    ],
    { rowVersion: options.rowVersion ?? ROW_VERSION }
  )
}

/**
 * Mutable server state, created per test rather than per file: the isolated
 * config runs `fullyParallel`, so module-level state would be shared by every
 * test that happened to land in the same worker process.
 */
interface Server {
  /** What the next GET answers with. Replace it to be "somebody else". */
  sections: Record<string, unknown>[]
  /** What the next GET answers with instead, when it is not 200. */
  getStatus: number
  /** What the next PUT answers with. */
  putStatus: number
  /** How many PUTs have actually gone out — the only honest "was it saved". */
  putCount: number
  /** Every PUT body, in order. */
  putBodies: unknown[]
  /** Keeps the next PUT in flight until `release` is called. */
  holdPut: boolean
  release: (() => void) | null
}

function server(sections: Record<string, unknown>[]): Server {
  return {
    sections,
    getStatus: 200,
    putStatus: 200,
    putCount: 0,
    putBodies: [],
    holdPut: false,
    release: null,
  }
}

async function install(page: Page, state: Server) {
  await installAuthenticatedApi(
    page,
    ["system-settings:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/admin/system-settings") {
        await fulfillJson(
          route,
          state.getStatus === 200
            ? {
                restartPending: false,
                dbOverridesUnavailable: false,
                sections: state.sections,
              }
            : { title: "Server error", status: state.getStatus },
          state.getStatus
        )
        return true
      }
      // The section key rides in the path, so the method is what tells a save
      // from the list above it.
      if (
        path.startsWith("/api/v1/admin/system-settings/") &&
        route.request().method() === "PUT"
      ) {
        state.putCount += 1
        state.putBodies.push(route.request().postDataJSON())
        if (state.holdPut) {
          await new Promise<void>((resolve) => {
            state.release = resolve
          })
        }
        await fulfillJson(
          route,
          state.putStatus === 200
            ? {}
            : { title: "Conflict", status: state.putStatus },
          state.putStatus
        )
        return true
      }
      return false
    }
  )
}

/** A setting's row. Paths carry colons, so the id goes in an attribute selector. */
function row(page: Page, path: string) {
  return page.locator(`[id="setting-${path}"]`)
}

const bar = (page: Page) => page.locator('[data-slot="settings-unsaved-bar"]')
const barSave = (page: Page) => bar(page).locator('button[type="submit"]')
const conflictAlert = (page: Page) => page.locator("#settings-conflict")

/**
 * Answers the question a high-impact change asks on its way out.
 *
 * `MaxConcurrentSessions` is in `HIGH_IMPACT_PATHS.Session`, so every save
 * driven through it stops here first and no PUT leaves until it does. The
 * question itself is the subject of exactly one test below; everywhere else the
 * subject is what happens AFTER the save leaves, so it is answered and passed.
 */
async function confirmHighImpact(page: Page) {
  await page
    .getByRole("alertdialog")
    .getByRole("button", { name: "Confirm" })
    .click()
}

async function open(page: Page, sectionKey: string, firstPath: string) {
  await page.goto(`/admin/system-settings/${sectionKey}`)
  // A row, not the card: the card paints before its rows are laid out, and
  // every locator below addresses a row.
  await row(page, firstPath).waitFor()
}

test("a conflict keeps every value the operator typed", async ({ page }) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  const input = row(page, "MaxConcurrentSessions").locator("input")
  await input.fill("7")

  // Somebody else saves 5 to the same section while this form is open: the
  // next GET answers with their value under a new row version.
  state.putStatus = 409
  state.sections = [
    sessionSection({
      max: 5,
      source: "database",
      rowVersion: OTHER_ROW_VERSION,
    }),
  ]

  await barSave(page).click()
  await confirmHighImpact(page)

  // The report names what MOVED, not what this operator changed.
  await expect(conflictAlert(page)).toBeVisible()
  await expect(conflictAlert(page)).toContainText("Maximum concurrent sessions")

  // THE ASSERTION THIS FILE EXISTS FOR. The old form was keyed on the row
  // version, the 409 path refetches, and the refetch changed it — so this input
  // came back reading 5 and the operator's 7 was gone with no way to recover it.
  await expect(input).toHaveValue("7")

  // Still dirty, so still saveable: the way out is to decide, not to retype.
  await expect(bar(page)).toBeVisible()
})

test("a successful save leaves the form clean and the badges current", async ({
  page,
}) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  const input = row(page, "MaxConcurrentSessions").locator("input")
  await input.fill("7")

  // What the server holds once it accepts: the value, customized, restamped.
  state.sections = [
    sessionSection({
      max: 7,
      source: "database",
      rowVersion: OTHER_ROW_VERSION,
    }),
  ]

  await barSave(page).click()
  await confirmHighImpact(page)

  // The bar unmounts the moment the form goes clean, and a form that stayed
  // dirty after a successful save would warn about leaving a page with nothing
  // left to save.
  await expect(bar(page)).toBeHidden()
  await expect(input).toHaveValue("7")
  await expect(page.getByText("Settings saved.")).toBeVisible()

  // The badge is the proof that the refetch still reaches the rows now that the
  // remount key no longer carries the row version: nothing here is remounted,
  // so this value arrived through the adopt effect or not at all.
  await expect(
    row(page, "MaxConcurrentSessions").getByText("Customized")
  ).toBeVisible()
})

test("cancelling after a conflict shows the other person's values", async ({
  page,
}) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  const input = row(page, "MaxConcurrentSessions").locator("input")
  await input.fill("7")
  state.putStatus = 409
  state.sections = [
    sessionSection({
      max: 5,
      source: "database",
      rowVersion: OTHER_ROW_VERSION,
    }),
  ]
  await barSave(page).click()
  await confirmHighImpact(page)
  await expect(conflictAlert(page)).toBeVisible()

  await bar(page).getByRole("button", { name: "Cancel" }).click()

  // Discard mine, show theirs, one gesture: Cancel resets to the last agreed
  // values, which makes the form clean, and a clean form adopts what the server
  // actually holds.
  await expect(input).toHaveValue("5")
  await expect(bar(page)).toBeHidden()
  await expect(conflictAlert(page)).toBeHidden()
})

test("a save that needs a restart says how many", async ({ page }) => {
  const state = server([sessionSection({ restartRequired: true })])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  await row(page, "MaxConcurrentSessions").locator("input").fill("3")
  state.sections = [
    sessionSection({
      max: 3,
      source: "database",
      restartRequired: true,
      rowVersion: OTHER_ROW_VERSION,
    }),
  ]

  await barSave(page).click()
  await confirmHighImpact(page)

  // "Saved" alone would be a half-truth: one of these values does nothing at
  // all until the API restarts, and the count says how many.
  await expect(
    page.getByText("Changes that need an API restart: 1")
  ).toBeVisible()
})

test("a refetch that fails does not take the form down with it", async ({
  page,
}) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  const input = row(page, "MaxConcurrentSessions").locator("input")
  await input.fill("7")

  // The conflict path refetches on purpose, so a GET that fails at exactly that
  // moment is reachable rather than theoretical — and the page answered any
  // errored query by replacing the whole form with one line of apology, taking
  // every unsaved value with it and asking nothing. That is the same total,
  // silent loss this file exists for, reached by the same gesture one network
  // failure later, and it made "your values are still here" false.
  state.putStatus = 409
  state.getStatus = 500

  await barSave(page).click()
  await confirmHighImpact(page)

  await expect(conflictAlert(page)).toBeVisible()
  await expect(input).toHaveValue("7")
  await expect(bar(page)).toBeVisible()
})

test("an out-of-range value is refused out loud", async ({ page }) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  // 100 is the registry maximum for this field.
  await row(page, "MaxConcurrentSessions").locator("input").fill("999")
  await barSave(page).click()

  // The highlight is asserted first because it is removed after two seconds,
  // and a slower assertion ahead of it would spend that budget.
  await expect(row(page, "MaxConcurrentSessions")).toHaveAttribute(
    "data-highlight",
    "true"
  )
  await expect(page.getByText("Nothing was saved.")).toBeVisible()
  await expect(
    row(page, "MaxConcurrentSessions").getByText("Must be at most 100.")
  ).toBeVisible()

  // An invalid submit used to do nothing whatsoever: no request, no message.
  // Half of "no request" is still right — this must not reach the server.
  expect(state.putCount).toBe(0)

  // And the caret is in the row that has to change, not wherever it was left.
  await expect
    .poll(() =>
      page.evaluate(() => {
        const element = document.getElementById("setting-MaxConcurrentSessions")
        return Boolean(element && element.contains(document.activeElement))
      })
    )
    .toBe(true)
})

test("changing a setting that can lock you out asks first", async ({ page }) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "TerminateOldestOnMax")

  // Off, the limit refuses the new sign-in instead of ending an old session —
  // which is how a person with no access to their old devices is locked out of
  // their own account. It is in HIGH_IMPACT_PATHS.Session for that reason.
  await row(page, "TerminateOldestOnMax").getByRole("switch").click()
  await barSave(page).click()

  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toBeVisible()
  await expect(dialog).toContainText(
    "Sign out the oldest session instead of refusing"
  )
  // Nothing is written while the question is on screen.
  expect(state.putCount).toBe(0)

  await dialog.getByRole("button", { name: "Confirm" }).click()
  await expect.poll(() => state.putCount).toBe(1)
  await expect(dialog).toBeHidden()
})

test("a setting that cannot lock you out saves without a dialog", async ({
  page,
}) => {
  const state = server([sessionSection()])
  await install(page, state)
  await open(page, "Session", "TerminateSessionsOnPasswordChange")

  // In no registry: a confirmation on every save is a click-through trainer,
  // and the value of the sixteen that do confirm is that the others do not.
  await row(page, "TerminateSessionsOnPasswordChange").getByRole("switch").click()
  await barSave(page).click()

  // The save going out at all is the proof that nothing intercepted it: a
  // dialog here would leave `putCount` at 0 and time this poll out.
  await expect.poll(() => state.putCount).toBe(1)
  await expect(page.getByRole("alertdialog")).toHaveCount(0)

  // And the payload is still the sparse override set: one field, because one
  // field differs from the file baseline.
  expect(state.putBodies[0]).toEqual(
    expect.objectContaining({
      overrides: { TerminateSessionsOnPasswordChange: false },
    })
  )
})

test("a prose value takes its direction from itself and a machine value does not", async ({
  page,
}) => {
  const state = server([
    section("DataController", "operations", [
      stringField("LegalName", "Astoom"),
      stringField("Address", "1 Example Street"),
      stringField("PrivacyEmail", "privacy@example.test"),
    ]),
  ])
  await install(page, state)
  await open(page, "DataController", "LegalName")

  // A legal name is written in the operator's own script, so the input takes
  // its direction from what was typed. An email address is a machine
  // identifier and stays pinned, in every locale.
  await expect(row(page, "LegalName").locator("input")).toHaveAttribute(
    "dir",
    "auto"
  )
  await expect(row(page, "PrivacyEmail").locator("input")).toHaveAttribute(
    "dir",
    "ltr"
  )

  // What this really guards is the wiring. The registry is keyed by section, so
  // a single row rendered without its section key answers "ltr" forever — and
  // that is invisible to a unit test and to the eye alike.
})

test("the choice control is named by its own row label", async ({ page }) => {
  const state = server([
    section("Password", "security", [
      boolField("BreachedPasswordCheck:Enabled", true),
      enumField("BreachedPasswordCheck:Mode", "Enforce", ["Enforce", "Warn"]),
    ]),
  ])
  await install(page, state)
  await open(page, "Password", "BreachedPasswordCheck:Enabled")

  // The row renders a `<label>`, but the control under it is a `div` with
  // `role="radiogroup"` and `<label for>` reaches only labelable elements. So
  // the label arrives through `aria-labelledby` or it does not arrive: before
  // that, this was an unnamed group of two unexplained buttons.
  await expect(
    page.getByRole("radiogroup", {
      name: "When a breached password is found",
    })
  ).toBeVisible()
})

test("a section waiting for a restart says so in words", async ({ page }) => {
  const state = server([
    sessionSection(),
    section("Gateway", "security", [
      boolField("ValidationEnabled", true, { isPendingRestart: true }),
    ]),
  ])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  // The red dot in the nav was the whole message for a sighted reader and
  // nothing at all for anyone else.
  const pending = page.getByRole("link", { name: /Waiting for restart/ })
  await expect(pending).toHaveCount(1)
  await expect(pending).toContainText("Gateway protection")
})

test("arriving from a deep link puts the caret in the setting", async ({
  page,
}) => {
  const state = server([sessionSection()])
  await install(page, state)

  // What the settings search produces: one section, one setting.
  await page.goto("/admin/system-settings/Session?field=TerminateOldestOnMax")
  await row(page, "TerminateOldestOnMax").waitFor()

  // Scrolling alone was what a search result used to get: the row arrived, the
  // ring faded, and the next keystroke went wherever focus had been left.
  await expect
    .poll(() =>
      page.evaluate(() => {
        const element = document.getElementById("setting-TerminateOldestOnMax")
        return Boolean(element && element.contains(document.activeElement))
      })
    )
    .toBe(true)
})

test("leaving mid-save says the save is still running", async ({ page }) => {
  const state = server([
    sessionSection(),
    section("Gateway", "security", [boolField("ValidationEnabled", true)]),
  ])
  state.holdPut = true
  await install(page, state)
  await open(page, "Session", "TerminateSessionsOnPasswordChange")

  // Deliberately a setting in no registry. A high-impact one would put a modal
  // confirmation between the bar and the sidebar — and a modal that is still
  // open because the PUT it started is still in flight makes the sidebar
  // unreachable, which is the one thing this test needs to reach.
  await row(page, "TerminateSessionsOnPasswordChange").getByRole("switch").click()
  await barSave(page).click()
  await expect.poll(() => state.putCount).toBe(1)

  // The bar's own Cancel is out for the same reason the prompt's Discard is
  // below: resetting the form while the PUT is committing discards nothing —
  // the save's success puts every discarded value straight back a moment later.
  await expect(bar(page).getByRole("button", { name: "Cancel" })).toBeDisabled()

  // The bar keeps the sidebar in reach while the PUT is in flight, which is
  // what makes this reachable at all — it was dead code while the prompt never
  // received `isSaving`.
  await page.getByRole("link", { name: "Gateway protection" }).click()
  const dialog = page.getByRole("alertdialog")
  await expect(dialog).toContainText("Save in progress")
  // Discarding a change the server may be in the middle of accepting is not a
  // decision anyone can make correctly.
  await expect(dialog.getByRole("button", { name: "Discard" })).toBeDisabled()

  state.release?.()
  // Once the save lands there is nothing left to lose, so the blocked
  // navigation resumes on its own.
  await expect(dialog).toBeHidden()
})

test("the sticky bar is not clipped by the pane it sticks to", async ({
  page,
}) => {
  const filler = Array.from({ length: 20 }, (_, index) =>
    boolField(`Filler${index}`, true)
  )
  const state = server([
    section("Session", "security", [
      intField("MaxConcurrentSessions", 0),
      boolField("TerminateOldestOnMax", true),
      ...filler,
    ]),
  ])
  await install(page, state)
  await open(page, "Session", "MaxConcurrentSessions")

  await row(page, "TerminateOldestOnMax").getByRole("switch").click()
  await expect(bar(page)).toBeVisible()

  const measured = await page.evaluate(async () => {
    const element = document.querySelector(
      '[data-slot="settings-unsaved-bar"]'
    )
    if (!(element instanceof HTMLElement)) throw new Error("no sticky bar")

    // The pane, found by what makes it one rather than by its classes.
    let pane: HTMLElement | null = element.parentElement
    while (pane) {
      const overflow = getComputedStyle(pane).overflowY
      if (overflow === "auto" || overflow === "scroll") break
      pane = pane.parentElement
    }
    if (!pane) throw new Error("the bar has no scroll container")

    const range = pane.scrollHeight - pane.clientHeight
    // Mid-scroll, so the bar is pinned rather than resting at the end of the
    // content — which is the only state the defect appears in.
    pane.scrollTop = Math.round(range / 2)
    await new Promise((resolve) =>
      requestAnimationFrame(() => requestAnimationFrame(resolve))
    )

    const barBox = element.getBoundingClientRect()
    const paneBox = pane.getBoundingClientRect()
    return { range, scrolled: pane.scrollTop, gap: paneBox.bottom - barBox.bottom }
  })

  // Guard the guard: a mock too short to scroll would make everything below
  // pass without measuring anything.
  expect(measured.range).toBeGreaterThan(100)
  expect(measured.scrolled).toBeGreaterThan(0)

  // A sticky element's view rectangle is the SCROLLPORT, which for
  // `overflow: auto` is the padding box — and the padding box is where the pane
  // clips. At `bottom-0` the bar's bottom edge lands exactly on that clip edge
  // and its entirely-downward `shadow-md` is destroyed while pinned, then
  // reappears at the end of the scroll where the bar also jumps 8px as it
  // un-sticks. `bottom-2` is the pane's own `lg:p-2`, so the pinned position IS
  // the resting position. Nothing else in the suite can see this: the card
  // outline check measures top, start and end, and never a bottom.
  expect(Math.round(measured.gap)).toBeGreaterThanOrEqual(8)
})

test("an invalid value inside a switched-off category says how to reach it", async ({
  page,
}) => {
  const state = server([
    section("Password", "security", [
      boolField("BreachedPasswordCheck:Enabled", true),
      intField("BreachedPasswordCheck:RejectThreshold", 1, { min: 1, max: 100 }),
    ]),
  ])
  await install(page, state)
  await open(page, "Password", "BreachedPasswordCheck:Enabled")

  // The dead end, in the three gestures that reach it: type something invalid
  // into a whole-number field inside a category, switch the category off, save.
  // Gating is a DOM prop on the control, never a change to what is sent, so the
  // value is still in the form and still invalid — and the row that has to
  // change no longer holds anything that can take the caret.
  await row(page, "BreachedPasswordCheck:RejectThreshold")
    .locator("input")
    .fill("abc")
  await row(page, "BreachedPasswordCheck:Enabled").getByRole("switch").click()
  await expect(
    row(page, "BreachedPasswordCheck:RejectThreshold").locator("input")
  ).toBeDisabled()

  await barSave(page).click()

  // Marked, because the ring is the only thing that can say WHICH row when
  // nothing in it can be focused — so it is asserted in PAINT and not only in
  // the attribute. The attribute is set by `focusRow` and the ring is drawn by
  // `data-[highlight]:ring-2` on the row, two files apart: an assertion that
  // reads only the attribute would still pass on the day the rule is renamed
  // or dropped, which is the day the mark stops existing for the reader.
  await expect
    .poll(() =>
      row(page, "BreachedPasswordCheck:RejectThreshold").evaluate(
        (element) => getComputedStyle(element).boxShadow
      )
    )
    .not.toBe("none")
  await expect(
    row(page, "BreachedPasswordCheck:RejectThreshold")
  ).toHaveAttribute("data-highlight", "true")

  // And the remedy, naming the category by the title on its own panel header.
  // Without it the operator is told a value is wrong, shown a row they cannot
  // type into, and left with no way out — a new dead end shipped in the same
  // change that closed an old one.
  await expect(
    page.getByText("Breached-password check is switched off")
  ).toBeVisible()

  expect(state.putCount).toBe(0)
})
