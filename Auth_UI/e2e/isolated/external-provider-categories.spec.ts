import { expect, test, type Page } from "@playwright/test"

import { expectNoShellOverflow } from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The external-provider sign-in section groups its settings by provider: each
 * provider is a panel headed by its own switch and its own mark, and the
 * settings that belong to every provider stay outside those panels.
 *
 * What this guards is a silent kind of loss. Nothing errors if a credential row
 * drifts out of its provider's panel, or if a shared setting is adopted by the
 * last panel on the page — the section still renders, every control still
 * saves, and the only thing that breaks is which provider the reader believes a
 * field belongs to. So the assertions are about CONTAINMENT, measured from the
 * DOM, not about the rows existing.
 */

test.use({ viewport: { width: 1440, height: 1200 } })

const field = (path: string, extra: Record<string, unknown> = {}) => ({
  path,
  kind: "string",
  effectiveValue: "",
  baselineValue: "",
  defaultValue: "",
  source: "file",
  restartRequired: false,
  isPendingRestart: false,
  readOnly: false,
  sensitive: false,
  ...extra,
})

const bool = (path: string, value: boolean) =>
  field(path, {
    kind: "bool",
    effectiveValue: value,
    baselineValue: value,
    defaultValue: value,
  })

/** The ExternalAuth section as the registry declares it, in registry order. */
const externalAuth = {
  key: "ExternalAuth",
  group: "access",
  editable: true,
  version: 0,
  rowVersion: null,
  fields: [
    bool("Google:Enabled", true),
    field("Google:ClientId", { effectiveValue: "abc.apps.googleusercontent.com" }),
    bool("Apple:Enabled", false),
    field("Apple:ServicesId"),
    field("Apple:TeamId"),
    field("Apple:KeyId"),
    field("Apple:PrivateKeyPem", { sensitive: true }),
    bool("AvatarImport:Enabled", true),
    field("AvatarImport:TimeoutMs", {
      kind: "int",
      effectiveValue: 3000,
      baselineValue: 3000,
      defaultValue: 3000,
      min: 500,
      max: 30000,
    }),
    field("AvatarImport:MaxBytes", {
      kind: "int",
      effectiveValue: 2097152,
      baselineValue: 2097152,
      defaultValue: 2097152,
      min: 65536,
      max: 4194304,
    }),
    bool("RequireNonce", false),
  ],
}

async function openSection(page: Page) {
  await installAuthenticatedApi(
    page,
    ["system-settings:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/admin/system-settings") {
        await fulfillJson(route, {
          restartPending: false,
          dbOverridesUnavailable: false,
          sections: [externalAuth],
        })
        return true
      }
      return false
    }
  )

  await page.goto("/admin/system-settings/ExternalAuth")
  await page.getByRole("switch", { name: "Google sign-in" }).waitFor()
}

/** The panel a setting sits in, or null when it sits at card level. */
function panelOf(page: Page, path: string) {
  return page.evaluate((id) => {
    const row = document.getElementById(id)
    if (!row) return "MISSING"
    const set = row.closest('[data-slot="field-set"]')
    if (!set) return null
    // A provider panel is the bordered one; the general block is a bare
    // fieldset carrying only a legend.
    if (!set.className.includes("rounded-xl")) return null
    const label = set.querySelector('[data-slot="field-label"]')
    return label?.textContent?.trim() ?? "UNLABELLED"
  }, `setting-${path}`)
}

test("each provider's credentials live in that provider's panel", async ({
  page,
}) => {
  await openSection(page)

  expect(await panelOf(page, "Google:Enabled")).toBe("Google sign-in")
  expect(await panelOf(page, "Google:ClientId")).toBe("Google sign-in")

  for (const path of [
    "Apple:Enabled",
    "Apple:ServicesId",
    "Apple:TeamId",
    "Apple:KeyId",
    "Apple:PrivateKeyPem",
  ]) {
    expect(await panelOf(page, path), path).toBe("Apple sign-in")
  }
})

test("settings that belong to every provider stay outside the panels", async ({
  page,
}) => {
  await openSection(page)

  for (const path of [
    "AvatarImport:Enabled",
    "AvatarImport:TimeoutMs",
    "AvatarImport:MaxBytes",
    "RequireNonce",
  ]) {
    expect(await panelOf(page, path), path).toBeNull()
  }

  // And they are announced as shared rather than left to be inferred.
  await expect(page.getByText("Applies to every provider")).toBeVisible()
})

test("every provider panel is marked with that provider's own logo", async ({
  page,
}) => {
  await openSection(page)

  const marks = await page.evaluate(() =>
    [...document.querySelectorAll('[data-slot="field-set"]')]
      .filter((set) => set.className.includes("rounded-xl"))
      .map((set) => {
        const header = set.querySelector('[data-slot="field"]')
        const svg = header?.querySelector(":scope > svg")
        return {
          name: header
            ?.querySelector('[data-slot="field-label"]')
            ?.textContent?.trim(),
          // Google's mark is fixed four-colour; Apple's inherits the text
          // colour so it flips with the theme. Neither may be announced twice.
          fills: svg
            ? [...svg.querySelectorAll("path")].map(
                (p) => p.getAttribute("fill") ?? "currentColor"
              )
            : null,
          hidden: svg?.getAttribute("aria-hidden"),
        }
      })
  )

  expect(marks).toEqual([
    {
      name: "Google sign-in",
      fills: ["#4285F4", "#34A853", "#FBBC05", "#EA4335"],
      hidden: "true",
    },
    { name: "Apple sign-in", fills: ["currentColor"], hidden: "true" },
  ])
})

test("the mark stays against the name it identifies", async ({ page }) => {
  await openSection(page)

  // The header is the one row with three children, and the text block between
  // them is width-capped for readability. Under a plain `justify-between` the
  // leftover width splits into TWO gaps and parks the name in the middle of
  // the row, hundreds of pixels from its own logo — while every assertion
  // about content, order and overflow still passes.
  const gaps = await page.evaluate(() =>
    ["setting-Google:Enabled", "setting-Apple:Enabled"].map((id) => {
      const row = document.getElementById(id)!
      const mark = row.querySelector(":scope > svg")!.getBoundingClientRect()
      const label = row
        .querySelector('[data-slot="field-label"]')!
        .getBoundingClientRect()
      const control = row
        .querySelector('[role="switch"]')!
        .getBoundingClientRect()
      const box = row.getBoundingClientRect()
      return {
        id,
        markToLabel: Math.round(label.left - mark.right),
        // The switch stays pinned to the end of the row all the same.
        controlToRowEnd: Math.round(box.right - control.right),
      }
    })
  )

  for (const gap of gaps) {
    expect(gap.markToLabel, `${gap.id}: mark adrift from its name`).toBeLessThan(
      24
    )
    expect(gap.controlToRowEnd, `${gap.id}: switch left the row end`).toBeLessThan(
      24
    )
  }
})

test("a provider switched on with an empty credential says so", async ({
  page,
}) => {
  await openSection(page)

  // Google arrives on and complete: nothing to report.
  await expect(page.getByRole("alert")).toHaveCount(0)

  await page.getByRole("textbox", { name: "Google client ID" }).fill("")
  const warning = page.getByRole("alert")
  await expect(warning).toContainText("Missing credentials")

  // The warning belongs to Google's panel, not to the section.
  expect(
    await warning.evaluate((el) => {
      const set = el.closest('[data-slot="field-set"]')
      return set
        ?.querySelector('[data-slot="field-label"]')
        ?.textContent?.trim()
    })
  ).toBe("Google sign-in")

  // Apple is off with three empty credentials, and that is not a fault.
  await expect(warning).toHaveCount(1)

  await page.getByRole("textbox", { name: "Google client ID" }).fill("x.example")
  await expect(page.getByRole("alert")).toHaveCount(0)
})

test("the section survives a backend that stops declaring a provider switch", async ({
  page,
}) => {
  // A console one release behind its backend must fall back to the flat list
  // it rendered before panels existed, never to a panel with no way to turn
  // its provider on.
  await installAuthenticatedApi(
    page,
    ["system-settings:manage"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === "/api/v1/admin/system-settings") {
        await fulfillJson(route, {
          restartPending: false,
          dbOverridesUnavailable: false,
          sections: [
            {
              ...externalAuth,
              fields: externalAuth.fields.filter(
                (f) => f.path !== "Apple:Enabled"
              ),
            },
          ],
        })
        return true
      }
      return false
    }
  )

  await page.goto("/admin/system-settings/ExternalAuth")
  await page.getByRole("textbox", { name: "Apple Services ID" }).waitFor()

  expect(await panelOf(page, "Apple:ServicesId")).toBeNull()
  expect(await panelOf(page, "Google:ClientId")).toBe("Google sign-in")
  await expectNoShellOverflow(page, "social sign-in without an Apple switch")
})
