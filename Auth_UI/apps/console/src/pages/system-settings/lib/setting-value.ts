import * as React from "react"
import { useTranslation } from "react-i18next"

/**
 * A setting's value as a person reads it, in the one place that spells it.
 *
 * Three surfaces draw the same values: the configuration-file note and the
 * unsaved line on the row itself, and the sticky bar's list of unsaved changes.
 * A screen that spells one value three ways is worse than one that spells it
 * oddly, so there is one renderer.
 *
 * A plain module, and that is the whole reason this file exists rather than a
 * helper exported from either component: `react-refresh/only-export-components`
 * fails the lint on a non-component export from a component module, so neither
 * `setting-field` nor `section-form` could own it and both carried a copy of it
 * instead. Nothing here renders anything, so the rule does not apply. Keep it
 * that way — a component added to this file re-opens the split.
 */

/**
 * Wraps an interpolated VALUE in a first-strong isolate so bidi never re-orders
 * it against the sentence it is dropped into.
 *
 * The element's `dir` cannot do this job: once i18next interpolates, the value
 * and the Arabic prose around it are ONE text node, and direction is a property
 * of an element. `field-constraints` carries the same scar and the same fix.
 *
 * FSI (U+2068), not the LRI that `field-constraints` uses. Its values are
 * always numbers, so forcing them left-to-right is right there. A settings
 * value is a number, a URL, a file path OR human prose that may itself be
 * Arabic — a legal name, an address. LRI would render that prose backwards.
 * FSI takes its direction from the value's own first strong character, which is
 * correct for all four.
 *
 * Built from code points rather than pasted in as literals: both characters
 * render as nothing at all, so a literal is one no reviewer can check and
 * nobody can tell has been dropped by a careless re-encoding.
 */
const FSI = String.fromCodePoint(0x2068)
const PDI = String.fromCodePoint(0x2069)
export const isolateFirstStrong = (text: string) => `${FSI}${text}${PDI}`

/**
 * The bidi controls a value may carry of its own, taken out before it goes into
 * a sentence.
 *
 * i18next is configured with `escapeValue: false`, which is what lets the
 * isolate above survive interpolation — and equally lets a value carrying its
 * own PDI close that isolate early, leaving the rest of it free to re-order the
 * Arabic prose around it. The embeddings and overrides (U+202A–U+202E), the
 * directional marks and the Arabic letter mark do the same. Settings values are
 * admin-entered, so this is hardening rather than an exposure — but the fix is
 * one pass over the string.
 *
 * The class is built from code points for the reason the isolate is: a
 * character class of invisible literals is one no reviewer can read, and one
 * nobody can tell has lost a member.
 */
const BIDI_CONTROLS = new RegExp(
  `[${[
    0x061c, 0x200e, 0x200f, 0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066,
    0x2067, 0x2068, 0x2069,
  ]
    .map((point) => String.fromCodePoint(point))
    .join("")}]`,
  "g"
)
export const stripBidiControls = (text: string) => text.replace(BIDI_CONTROLS, "")

/** The words a value needs when the value is not its own name. */
export interface SettingValueLabels {
  notSet: string
  enabled: string
  disabled: string
}

/**
 * A setting's value as a person reads it.
 *
 * Two shapes need translating rather than stringifying. A boolean is a switch,
 * and `true` is not a word an operator set — the shipped-default line directly
 * above a row already says "Enabled"/"Disabled" for the same field, so this
 * says it too. An array arrives here either as the registry's real array or as
 * the form's editing shape, which is one entry per LINE; a multi-line string
 * dropped into a one-line description would have its newlines collapsed to
 * spaces and read as run-on words, so lines are joined the way arrays are.
 *
 * An empty array is an absent value, not a value of "nothing" — the code before
 * this rendered it as the empty string, which left a note stating a fact and
 * then showing nothing. A line that says "was" and then shows nothing reads as
 * a rendering fault, not as an empty setting.
 *
 * A LIST is isolated entry by entry rather than as one run. Wrapping the joined
 * string would leave the commas between entries as neutral characters resolved
 * by the direction of the whole run, so an Arabic organisation name beside a
 * Latin URL could render in an order neither of them is in. One isolate per
 * entry is the bug FSI was chosen to prevent, applied at the level the values
 * actually differ.
 */
export function renderSettingValue(
  value: unknown,
  labels: SettingValueLabels
): string {
  if (typeof value === "boolean") return value ? labels.enabled : labels.disabled
  const joinEntries = (entries: string[]) => {
    const kept = entries
      .map((entry) => stripBidiControls(entry).trim())
      .filter((entry) => entry.length > 0)
    return kept.length > 0 ? kept.map(isolateFirstStrong).join(", ") : labels.notSet
  }
  if (Array.isArray(value)) return joinEntries(value.map((entry) => String(entry)))
  if (value === null || value === undefined || value === "") return labels.notSet
  const text = stripBidiControls(String(value))
  if (!text.includes("\n")) return text.length > 0 ? text : labels.notSet
  return joinEntries(text.split(/\r?\n/))
}

/**
 * The labels `renderSettingValue` needs, resolved once per caller.
 *
 * A hook in a plain module is legal — the react-refresh rule is about
 * COMPONENT exports — and it belongs beside the renderer it feeds rather than
 * in whichever component module happened to need it first, which is how the
 * same three translation keys came to be resolved in two places.
 *
 * Memoised on `t` so the object is referentially stable between renders: one
 * caller reads these labels inside a `useCallback`, and a fresh object every
 * render would rebuild that callback every render for no gain. `t` changes
 * identity when the language does, which is exactly when these three strings
 * change.
 */
export function useValueLabels(): SettingValueLabels {
  const { t } = useTranslation()
  return React.useMemo(
    () => ({
      notSet: t("systemSettings.notSet"),
      enabled: t("common.enabled"),
      disabled: t("common.disabled"),
    }),
    [t]
  )
}
