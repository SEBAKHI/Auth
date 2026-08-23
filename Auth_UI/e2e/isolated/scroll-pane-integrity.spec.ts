import { test } from "@playwright/test"

import {
  expectCardOutlinesVisible,
  expectNoCrushedContent,
} from "./layout-overflow"
import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * The two screens that put a Card straight inside a scroll pane.
 *
 * Both defects this file guards were live in the product and invisible to every
 * other check we had:
 *
 *  1. The cards were squeezed to a fraction of their height and clipped their
 *     own controls, while the pane never grew a scrollbar. A Card carries
 *     `overflow-hidden`, which switches off the `min-height: auto` protection
 *     that normally stops a flex item shrinking past its content.
 *  2. The cards lost their outline. A Card is bounded by `ring-1` and lifted by
 *     `shadow-md`, both painted OUTSIDE its box, and a pane whose overflow is
 *     not `visible` clips them flush against its own edges.
 *
 * `expectNoShellOverflow` cannot see either: the first is a vertical loss, the
 * second is a one-pixel one, and both are silent.
 *
 * 1440 is not decoration. The policy editor's pane only becomes a scroll pane at
 * `xl` (1280px), so at phone and tablet widths neither defect exists - which is
 * exactly why the responsive matrix, which asserts horizontal overflow at six
 * widths, ran green through both of them.
 */
test.use({ viewport: { width: 1440, height: 900 } })

const POLICY_ID = "77777777-7777-7777-7777-777777777777"
const VERSION = "2026.08"

/** Enough content that the pane must scroll rather than absorb it. */
const section = (n: number) => ({
  heading: `Section ${n}`,
  paragraphs: [
    `Paragraph one of section ${n}. It is long enough to occupy a line or two so the card has real height to lose.`,
  ],
  bullets: [`First point in section ${n}`, `Second point in section ${n}`],
})

const policyDocument = {
  title: "Privacy Policy",
  effectiveDate: "Effective 28 July 2026",
  versionLabel: "Version",
  unfilledWarning: "",
  contactDpoLabel: "Data protection officer",
  contactVerbisLabel: "VERBIS registration number",
  contactKepLabel: "Registered e-mail (KEP)",
  intro: ["This policy explains what personal data the account service holds."],
  sections: [1, 2, 3, 4, 5].map(section),
  retention: {
    heading: "How long we keep data",
    intro: "These periods are enforced automatically.",
    columns: ["Data", "Kept for", "What happens then"],
    rows: [
      {
        category: "Account and profile data",
        retention: "Until you delete your account",
        detail: "Permanently destroyed by the staged deletion process.",
      },
      {
        category: "Sessions and tokens",
        retention: "Until expiry or sign-out",
        detail: "All revoked immediately when deletion is requested.",
      },
    ],
  },
  deletion: {
    heading: "Deleting your account",
    paragraphs: ["You can request permanent deletion at any time."],
    bullets: ["Your account is deactivated at once."],
    button: "Delete my account",
    signedInHint: "You can also do this from your profile.",
  },
  rights: [
    {
      heading: "Your rights",
      paragraphs: ["You may ask what we hold and require its correction."],
    },
  ],
  // `closing` is a list of sections, not of strings: the renderer walks each
  // entry's `paragraphs`, so a bare string here throws before anything paints.
  closing: [
    {
      heading: "Contact",
      paragraphs: ["Questions go to the contact address below."],
    },
  ],
}

const versions = [
  {
    id: POLICY_ID,
    version: VERSION,
    effectiveDateUtc: "2026-08-01T00:00:00Z",
    isPublished: true,
    changeNote: "test",
    createdAt: "2026-08-05T19:56:26Z",
    languages: ["en"],
    disclosureOutOfDate: false,
  },
]

test("the policy editor scrolls its cards instead of crushing them", async ({
  page,
}) => {
  await installAuthenticatedApi(
    page,
    ["privacy-policy:read", "privacy-policy:manage"],
    async (route, url) => {
      const path = url.pathname.toLowerCase()
      if (path === "/api/v1/privacy-policy/versions") {
        await fulfillJson(route, versions)
        return true
      }
      if (path === "/api/v1/privacy-policy/versions/content") {
        await fulfillJson(route, {
          version: VERSION,
          languageCode: url.searchParams.get("language") ?? "en",
          contentJson: JSON.stringify(policyDocument),
        })
        return true
      }
      if (path === "/api/v1/privacy-policy/published") {
        await fulfillJson(route, {
          version: VERSION,
          languageCode: "en",
          contentJson: JSON.stringify(policyDocument),
        })
        return true
      }
      return false
    }
  )

  await page.goto(`/notifications/policy/${POLICY_ID}`)
  // The first card's title is the anchor: it renders before the fields do, so
  // waiting on it alone would pass even while every field below it is clipped.
  await page.getByRole("textbox", { name: "Title" }).waitFor()

  await expectNoCrushedContent(page, "policy editor at 1440")
  await expectCardOutlinesVisible(page, "policy editor at 1440")
})

const settingsSection = {
  key: "Session",
  group: "security",
  editable: true,
  version: 0,
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
    ...["TerminateOldestOnMax", "TerminateSessionsOnPasswordChange", "TerminateSessionsOnPasswordReset"].map(
      (path) => ({
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
      })
    ),
  ],
}

test("the system settings card keeps its outline inside the scroll pane", async ({
  page,
}) => {
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
    }
  )

  await page.goto("/admin/system-settings")
  await page.getByRole("switch").first().waitFor()

  await expectNoCrushedContent(page, "system settings at 1440")
  await expectCardOutlinesVisible(page, "system settings at 1440")
})
