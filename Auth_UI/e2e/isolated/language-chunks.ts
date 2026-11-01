import type { Page } from "@playwright/test"

import { ar } from "../../packages/i18n/src/locales/ar"
import { fr } from "../../packages/i18n/src/locales/fr"

/**
 * Which languages' code a page downloaded (UI batch P5, item 11), for the console's and the
 * accounts' hidden-language specs alike. Scripts are told apart by what they hold, not by file
 * names a build may change: a language's strings by a sentence only its locale file has, its
 * dates by a month only its date-fns locale has. Arabic is the specs' floor: a visible language
 * that does arrive, so the detector's silence about French is evidence.
 */
export const LANGUAGE_MARKERS = {
  frStrings: fr.platformSettings.languages.saved,
  frDates: "janvier",
  arStrings: ar.platformSettings.languages.saved,
  arDates: "يناير",
} as const

export type LanguageMarker = keyof typeof LANGUAGE_MARKERS

/** Reads every script under /assets the page downloads for the markers. */
export function watchLanguageScripts(page: Page) {
  const reads: Promise<LanguageMarker[]>[] = []
  page.on("response", (response) => {
    const url = response.url()
    if (!/\.js(\?|$)/.test(url) || !url.includes("/assets/")) return
    reads.push(
      response
        .text()
        .then((body) =>
          (Object.keys(LANGUAGE_MARKERS) as LanguageMarker[]).filter((key) => body.includes(LANGUAGE_MARKERS[key]))
        )
        .catch(() => [])
    )
  })
  return {
    /** The markers seen so far, once every script read has finished. */
    async seen(): Promise<Set<LanguageMarker>> {
      return new Set((await Promise.all(reads)).flat())
    },
    /** Forgets what was seen, for a reload. */
    reset() {
      reads.length = 0
    },
  }
}
