import { expect, test, type Page } from "@playwright/test"

import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The two settings pages are forms, and a form is read at a measure.
 *
 * Uncapped, the page column spent every pixel the monitor gave it, and the
 * rows spent it on emptiness: a row puts its label at the start and pins its
 * control to the end, so a wider column does not widen anything a reader
 * looks at — it only pushes the two ends of a single row further apart. On a
 * 2560 monitor the end of a hint and the start of the switch it describes
 * were 1228px apart. A row whose two ends are a third of a metre apart is not
 * a row, and nothing in the suite could see it: no overflow, no clipping, no
 * crushed content, every control present and every value correct.
 *
 * So this file measures. `--content-measure` (72rem, `apps/console/src/index.css`)
 * caps the page column, and every number below follows from that one cap plus
 * class names that already existed.
 *
 * THE ARITHMETIC. Every constant is read out of the worktree and the source is
 * named, so nobody re-derives it from memory:
 *
 *   viewport                          2560   `test.use` below
 *   sidebar                            256   SIDEBAR_WIDTH = "16rem",
 *                                            packages/ui/src/sidebar.tsx:29
 *   main padding, each side             24   `p-4 md:p-6`,
 *                                            packages/ui/src/common/app-shell.tsx:198
 *   available to the page column      2256   2560 − 256 − 48
 *   page column                       1152   min(2256, 1152) — max-w-(--content-measure), 72rem
 *   section nav                        224   `lg:w-56`, system-settings-page.tsx:33
 *                                            (border-box, so its `lg:pe-2` is inside it)
 *   column gap                          24   `gap-6`, system-settings-page.tsx:129
 *   pane padding, both sides            16   `lg:p-2`, system-settings-page.tsx:143
 *   CARD                               888   1152 − 224 − 24 − 16
 *   CardContent padding, both sides     48   `px-(--card-spacing)`, `--card-spacing: --spacing(6)`,
 *                                            packages/ui/src/card.tsx:16,73
 *   ROW CONTENT BOX                    840   888 − 48. `ROW.card`'s `-mx-3
 *                                            w-[calc(100%+1.5rem)] px-3` cancels exactly:
 *                                            (840 + 24) − 24
 *   Field gap                           12   `gap-3`, packages/ui/src/field.tsx:54
 *   hint block cap                     672   `max-w-2xl` = 42rem, setting-field.tsx:118
 *   Switch                              44   `data-[size=default]:w-11`,
 *                                            packages/ui/src/switch.tsx:20
 *   SWITCH ROW FREE SPACE              112   840 − 672 − 12 − 44; `justify-between` puts
 *                                            all of it between the two children
 *   HINT-END → SWITCH-START            124   12 + 112
 *   int control                        160   CONTROL.int = `w-40`, setting-field.tsx:129
 *   INT ROW HINT BLOCK                 668   840 − 12 − 160; `flex-1` under its 672 cap,
 *                                            so it takes 668 and leaves 0
 *   HINT-END → INPUT-START              12   the bare `Field` gap
 *
 * For the record, the same arithmetic with no cap — the numbers the change was
 * made against:
 *   1920: column 1616 → card 1352 → row 1304 → 1304 − 672 − 12 − 44 = 576 free → gutter 588
 *   2560: column 2256 → card 1992 → row 1944 → 1944 − 672 − 12 − 44 = 1216 free → gutter 1228
 *   1440: available 1136 < 1152, so the cap is inert there — which is why it
 *         cannot disturb `scroll-pane-integrity.spec.ts`, which runs at 1440.
 *
 * TOLERANCE IS `Math.round` AND EXACT EQUALITY, NEVER A RANGE. One caveat, and
 * it is why the mock stays at four fields: 888 holds only while the scroll pane
 * grows no vertical scrollbar. Chromium uses classic scrollbars, so a richer
 * mock would silently shave ~15px off the card and off every gutter with it. If
 * 888 comes back as ~873, the mock grew — shrink it. Do not widen the
 * tolerance: a drift means the card width is being computed from something
 * other than the table above.
 */
test.use({ viewport: { width: 2560, height: 1000 } })

/**
 * The `Session` section, four fields, copied from `scroll-pane-integrity.spec.ts`.
 *
 * Four is the maximum, not a sample: see the scrollbar caveat above. One `int`
 * field and one `bool` field are all the geometry needs, and the other two
 * bools cost no vertical room worth the risk.
 */
const settingsSection = {
  key: "Session",
  group: "security",
  editable: true,
  version: 0,
  rowVersion: null,
  fields: [
    {
      path: "MaxConcurrentSessions",
      kind: "int",
      effectiveValue: 0,
      baselineValue: 0,
      defaultValue: 0,
      source: "file",
      restartRequired: false,
      isPendingRestart: false,
      readOnly: false,
      sensitive: false,
      min: 0,
      max: 100,
    },
    ...[
      "TerminateOldestOnMax",
      "TerminateSessionsOnPasswordChange",
      "TerminateSessionsOnPasswordReset",
    ].map((path) => ({
      path,
      kind: "bool",
      effectiveValue: true,
      baselineValue: true,
      defaultValue: true,
      source: "file",
      restartRequired: false,
      isPendingRestart: false,
      readOnly: false,
      sensitive: false,
    })),
  ],
}

async function openSystemSettings(page: Page, preferredLanguage = "en") {
  await installAuthenticatedApi(
    page,
    ["system-settings:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/admin/system-settings") {
        await fulfillJson(route, {
          restartPending: false,
          dbOverridesUnavailable: false,
          sections: [settingsSection],
        })
        return true
      }
      return false
    },
    { preferredLanguage }
  )

  await page.goto("/admin/system-settings")
  // The switch is the last thing to paint in a row, so waiting on it means
  // every box this file measures has been laid out.
  await page.getByRole("switch").first().waitFor()
}

/**
 * The page column, the card it holds, and where `main`'s content box begins
 * and ends.
 *
 * The column is found by walking up from the card to the direct child of
 * `<main>`: `ZonedOutlet` is a Fragment (`packages/ui/src/common/app-shell.tsx:66`),
 * so the routed page's own root element IS that child, and no selector has to
 * know its class list. `mainPad*` are the inside edges of `main`'s padding —
 * the two places the column would sit flush against if nothing centred it.
 *
 * THE SHELL HAS TWO `<main>` ELEMENTS, and a bare `querySelector("main")` finds
 * the wrong one. `SidebarInset` is itself a `<main>`
 * (`packages/ui/src/sidebar.tsx:310`, `data-slot="sidebar-inset"`), and the
 * padded scroller sits inside it (`app-shell.tsx:198`). Taking the
 * outer one stops the walk a level early — it returns the inner `<main>` as
 * "the column", measures 2304 (viewport − sidebar) and reads the padding off an
 * element that has none, which also makes the start-alignment assertion compare
 * a box with its own parent and pass no matter what. So the padded one is named
 * explicitly.
 */
async function measureColumn(page: Page) {
  return page.evaluate(() => {
    const card = document.querySelector('[data-slot="card"]')
    if (!(card instanceof HTMLElement)) {
      throw new Error('no [data-slot="card"] on the page')
    }
    const main = document.querySelector('main:not([data-slot="sidebar-inset"])')
    if (!(main instanceof HTMLElement)) throw new Error("no <main> in the shell")

    let column: HTMLElement = card
    while (column.parentElement && column.parentElement !== main) {
      column = column.parentElement
    }
    if (column.parentElement !== main) {
      throw new Error("the card is not inside <main>")
    }

    const columnBox = column.getBoundingClientRect()
    const mainBox = main.getBoundingClientRect()
    const mainStyle = getComputedStyle(main)
    const cardBox = card.getBoundingClientRect()
    return {
      columnWidth: columnBox.width,
      columnLeft: columnBox.left,
      columnRight: columnBox.right,
      mainPadLeft: mainBox.left + parseFloat(mainStyle.paddingLeft),
      mainPadRight: mainBox.right - parseFloat(mainStyle.paddingRight),
      cardWidth: cardBox.width,
    }
  })
}

/**
 * A row's text block and its control, as boxes.
 *
 * A switch is addressed as `[role="switch"]`, never `[data-slot="switch"]`. The
 * slot attribute does not survive the form: `FormControl` is a `Slot.Root`
 * carrying `data-slot="form-control"` (`packages/ui/src/form.tsx:108`), and
 * `Switch` spreads the props it is handed AFTER its own attribute
 * (`packages/ui/src/switch.tsx:17,24`), so the slot's value wins and every
 * switch inside a form row renders as `data-slot="form-control"`. The role is
 * the stable handle, and it is what `external-provider-categories.spec.ts:205`
 * already uses.
 */
async function measureRow(page: Page, rowId: string, controlSelector: string) {
  return page.evaluate(
    ({ rowId, controlSelector }) => {
      const row = document.getElementById(rowId)
      if (!row) throw new Error(`no row #${rowId}`)
      const content = row.querySelector('[data-slot="field-content"]')
      if (!(content instanceof HTMLElement)) {
        throw new Error(`#${rowId} has no [data-slot="field-content"]`)
      }
      const control = row.querySelector(controlSelector)
      if (!(control instanceof HTMLElement)) {
        throw new Error(`#${rowId} has no ${controlSelector}`)
      }
      const contentBox = content.getBoundingClientRect()
      const controlBox = control.getBoundingClientRect()
      return {
        contentWidth: contentBox.width,
        contentLeft: contentBox.left,
        contentRight: contentBox.right,
        controlLeft: controlBox.left,
        controlRight: controlBox.right,
      }
    },
    { rowId, controlSelector }
  )
}

test("the settings column stops at the measure and stays at the inline start", async ({
  page,
}) => {
  await openSystemSettings(page)

  const measured = await measureColumn(page)

  // page column = min(available 2256, cap 1152) = 1152
  //   available = viewport 2560 − sidebar 256 − main padding 48
  //   cap       = --content-measure, 72rem = 1152
  expect(Math.round(measured.columnWidth)).toBe(1152)

  // card = column 1152 − nav 224 (lg:w-56) − column gap 24 (gap-6)
  //        − pane padding 16 (lg:p-2, both sides) = 888
  expect(Math.round(measured.cardWidth)).toBe(888)

  // The cap is a max-width on a stretched flex item, so it clamps and then
  // aligns at the cross-start. Flush against `main`'s inline-start padding edge
  // is what proves no `mx-auto` crept in: centring would open a void between
  // the section nav and the card and break the adjacency that makes the nav
  // read as this card's index.
  expect(Math.round(measured.columnLeft)).toBe(Math.round(measured.mainPadLeft))

  await expectNoShellOverflow(page, "system settings at 2560")
})

test("a switch sits 124px from the end of its hint, not 588", async ({
  page,
}) => {
  await openSystemSettings(page)

  const row = await measureRow(
    page,
    "setting-TerminateOldestOnMax",
    '[role="switch"]'
  )

  // hint block = max-w-2xl = 42rem = 672, and the row has 840 − 44 − 12 = 784
  // to give it, so the cap is what binds.
  expect(Math.round(row.contentWidth)).toBe(672)

  // Field gap 12 (gap-3) + free space 112, where
  //   free = row content 840 − hint 672 − gap 12 − Switch 44 (w-11) = 112
  // and `justify-between` puts all of it between the two children.
  //
  // The TOTAL distance is asserted, not `distance − 12 === 112`: a test that
  // subtracts a constant it also assumes is asserting that constant twice.
  expect(Math.round(row.controlLeft - row.contentRight)).toBe(124)
})

test("a whole-number control leaves the hint the rest of the row", async ({
  page,
}) => {
  await openSystemSettings(page)

  const row = await measureRow(page, "setting-MaxConcurrentSessions", "input")

  // row content 840 − Field gap 12 − int control 160 (CONTROL.int = w-40) = 668,
  // which is under the 672 cap, so `flex-1` takes all of it and leaves nothing.
  expect(Math.round(row.contentWidth)).toBe(668)

  // Nothing between them but the bare `Field` gap: gap-3 = 12.
  expect(Math.round(row.controlLeft - row.contentRight)).toBe(12)
})

test("the column starts at the inline start in Arabic too", async ({
  page,
}) => {
  await openSystemSettings(page, "ar")
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl")

  const measured = await measureColumn(page)

  // Same cap, mirrored: in RTL the cross-start is the RIGHT edge, so the column
  // must sit flush against `main`'s inline-start padding edge on that side. A
  // physical `mx-auto` or a stray `ml-` would show up here and nowhere else.
  expect(Math.round(measured.columnRight)).toBe(
    Math.round(measured.mainPadRight)
  )
  // card = 1152 − 224 − 24 − 16 = 888, unchanged by direction.
  expect(Math.round(measured.cardWidth)).toBe(888)

  const row = await measureRow(
    page,
    "setting-TerminateOldestOnMax",
    '[role="switch"]'
  )
  // The same 12 + 112 = 124, measured the other way round: the hint starts
  // where the switch ends.
  expect(Math.round(row.contentLeft - row.controlRight)).toBe(124)

  await expectNoShellOverflow(page, "system settings at 2560 in Arabic")
})

test("the platform settings page stops at the same measure", async ({
  page,
}) => {
  await installAuthenticatedApi(
    page,
    ["platform-settings:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/admin/platform-settings") {
        // PlatformSettingsDto verbatim (schema.d.ts:13992). `unwrap` returns
        // `data` directly, so there is no envelope to wrap this in.
        await fulfillJson(route, {
          platformName: "AuthSystem",
          logoUrl: null,
          logoUrlDark: null,
          faviconUrl: null,
          modifiedAt: null,
          modifiedBy: null,
          modifiedByName: null,
        })
        return true
      }
      return false
    }
  )

  await page.goto("/admin/platform-settings")
  await page.getByRole("textbox", { name: "Platform name" }).waitFor()

  const measured = await measureColumn(page)

  // Identical arithmetic: available 2256, cap 1152, and the column clamped to
  // the cap. This page has no second column, so there is no card width to
  // derive — the point is only that the one free-text input, which is `w-full`
  // from the primitive, no longer stretches across a 2560px monitor.
  expect(Math.round(measured.columnWidth)).toBe(1152)
  expect(Math.round(measured.columnLeft)).toBe(Math.round(measured.mainPadLeft))

  await expectNoShellOverflow(page, "platform settings at 2560")
})
