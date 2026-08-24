import { expect, test, type Page } from "@playwright/test"

import { fulfillJson, installAuthenticatedApi } from "./mock-authenticated-api"

/**
 * A redirect URI reads in full, at the side the page reads from.
 *
 * Two requirements pull against each other here, and satisfying one used to
 * break the other:
 *
 *  - The value is LTR whatever the page is. A URL's `:` `/` `?` `=` are bidi
 *    neutrals, so an Arabic paragraph direction reorders them and the address
 *    comes out scrambled.
 *  - The value is aligned to the page. `text-start` cannot do it: `start`
 *    resolves against the element that declares `dir="ltr"`, so it means
 *    "left" on an Arabic page and the list detached to the far side of its row.
 *
 * The alignment comes from the column instead (`items-start`, which resolves
 * against the CONTAINER's direction). That makes each value shrink to
 * fit-content — and CSS Text excludes the break opportunities `break-words`
 * introduces from min-content, so a long URI keeps its full unwrapped width,
 * hangs outside the Card, and the Card's `overflow-hidden` cuts it off. In RTL
 * the amputated side is the START of the URL: scheme and host, gone, with no
 * scrollbar to reveal them.
 *
 * `expectNoShellOverflow` cannot see any of this — the Card absorbs the spill
 * long before it reaches the shell inset, so the shell reads zero while the
 * address is unreadable. The assertions below measure the value against its own
 * cell, which is where the failure actually lives.
 */

const APP_ID = "22222222-2222-2222-2222-222222222222"

/**
 * Three shapes, chosen for how they break rather than for realism: one that
 * fits at every width, one realistic long callback, and one whose host has no
 * natural break opportunity at all — the only kind that can still overrun a
 * tablet column.
 */
const REDIRECT_URIS = [
  "https://client.example.com/cb",
  "https://app.customer.example.com/auth/oidc/signin-callback?state=1",
  "https://averyveryverylongsubdomainwithoutbreaks.example.com/callback",
]

const application = {
  id: APP_ID,
  name: "Aud Test",
  code: "AUDTEST",
  isActive: true,
  accessMode: "Restricted",
  allowSelfRegistration: false,
  requireTwoFactor: false,
  requireEmailVerification: false,
  sessionTimeoutMinutes: 60,
  redirectUris: REDIRECT_URIS,
  createdAt: "2026-07-19T16:32:00Z",
  modifiedAt: "2026-08-14T05:25:00Z",
  createdByName: "Super System Administrator",
  modifiedByName: "Super System Administrator",
}

async function installApplication(page: Page, preferredLanguage: string) {
  await installAuthenticatedApi(
    page,
    ["applications:read"],
    async (route, url) => {
      if (url.pathname.toLowerCase() === `/api/v1/applications/${APP_ID}`) {
        await fulfillJson(route, application)
        return true
      }
      // The detail page's tabs each load a list; one empty envelope serves them
      // all, because this is about the header grid above them.
      await fulfillJson(
        route,
        Object.assign([], {
          users: [],
          items: [],
          organizations: [],
          roles: [],
          permissions: [],
          totalCount: 0,
          totalPages: 1,
          pageNumber: 1,
          pageSize: 20,
        })
      )
      return true
    },
    { preferredLanguage }
  )
}

const LOCALES = [
  { code: "en", dir: "ltr" },
  { code: "ar", dir: "rtl" },
] as const

const WIDTHS = [320, 768, 1440] as const

for (const locale of LOCALES) {
  for (const width of WIDTHS) {
    test(`${locale.code}: redirect URIs read in full at ${width}px`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 800 })
      await installApplication(page, locale.code)
      await page.goto(`/applications/${APP_ID}`)
      await page.waitForSelector("dl")

      const { values, cardClips } = await page.evaluate(() => {
        const card = document.querySelector('[data-slot="card"]')
        if (!(card instanceof HTMLElement)) throw new Error("no card rendered")

        return {
          // What the Card is hiding, if anything. A page-level fact, so it is
          // asserted once rather than blamed on whichever value comes first.
          cardClips: card.scrollWidth - card.clientWidth,
          values: [...document.querySelectorAll('dd span[dir="ltr"]')].map(
            (node) => {
              const span = node as HTMLElement
              const cell = span.closest("dd") as HTMLElement
              const s = span.getBoundingClientRect()
              const c = cell.getBoundingClientRect()
              const rtl = getComputedStyle(cell).direction === "rtl"

              return {
                uri: (span.textContent ?? "").trim(),
                direction: getComputedStyle(span).direction,
                /** How far the value hangs outside its own cell, either side. */
                spill: Math.max(0, s.right - c.right, c.left - s.left),
                /** Distance from the cell's inline-start edge. */
                startGap: rtl ? c.right - s.right : s.left - c.left,
              }
            }
          ),
        }
      })

      expect(values.map((value) => value.uri)).toEqual(REDIRECT_URIS)

      const where = `at ${width}px ${locale.code}`
      expect(
        cardClips,
        `${where}: the card is hiding ${cardClips}px of content`
      ).toBe(0)

      for (const value of values) {
        const at = `"${value.uri}" ${where}`

        expect(value.direction, `${at} must render LTR`).toBe("ltr")
        expect(value.spill, `${at} hangs outside its cell`).toBeLessThanOrEqual(
          1
        )
        expect(
          value.startGap,
          `${at} is not aligned to the page's reading side`
        ).toBeLessThanOrEqual(1)
      }
    })
  }
}
