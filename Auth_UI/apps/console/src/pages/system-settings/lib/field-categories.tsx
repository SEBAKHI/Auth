import type * as React from "react"

import { AppleIcon, GoogleIcon } from "@authsystem/ui/brand-icons"

import type { SystemSettingsField } from "./sections"

/**
 * A named subset of one section's settings, drawn as a panel of its own.
 *
 * Some sections hold several settings per THING rather than several settings
 * about one thing: this one carries two sign-in providers, each with its own
 * switch and its own credentials, plus a handful of settings that belong to
 * every provider at once. Read as a flat list the reader has to reconstruct
 * that from the labels — every Apple field says "Apple" because nothing else
 * does. A category says it once, in the layout, and the labels are then free
 * to stay complete for the surfaces that show them alone (search results, the
 * command palette).
 *
 * Membership comes from the config path, which already encodes the grouping:
 * `Google:ClientId` belongs to the `Google` category. Anything that matches no
 * category is general — deliberately the DEFAULT, so a setting added to the
 * backend that belongs to no provider appears with the other shared ones
 * rather than being silently adopted by the last panel.
 */
export interface SectionFieldCategory {
  /** Config-path prefix. `Google` collects `Google:Enabled`, `Google:ClientId`. */
  prefix: string
  /**
   * The setting that turns the whole category on, promoted into the panel
   * header — where the reader looks first, and where it governs everything
   * under it instead of sitting in the run of rows as a peer of the
   * credentials it gates.
   */
  switchPath: string
  /** The provider's own mark. Identifies the panel faster than its name does. */
  icon: React.ComponentType<React.SVGProps<SVGSVGElement>>
}

export const SECTION_FIELD_CATEGORIES: Record<string, SectionFieldCategory[]> = {
  ExternalAuth: [
    { prefix: "Google", switchPath: "Google:Enabled", icon: GoogleIcon },
    { prefix: "Apple", switchPath: "Apple:Enabled", icon: AppleIcon },
  ],
}

export interface CategorizedSection {
  categories: {
    category: SectionFieldCategory
    /** The header row — the category's own switch. */
    switchField: SystemSettingsField
    /** Everything else in the category, in the order the backend declared it. */
    fields: SystemSettingsField[]
  }[]
  /** Settings belonging to no category, in the order the backend declared them. */
  general: SystemSettingsField[]
}

function belongsTo(category: SectionFieldCategory, path: string): boolean {
  return path.startsWith(`${category.prefix}:`)
}

/**
 * Splits a section's fields into its declared categories plus the general
 * remainder.
 *
 * A category whose switch the payload does not carry is dropped and its fields
 * fall back to the general list: a console one release behind its backend
 * renders a flat section, which is what it did before categories existed, and
 * never a panel with no way to turn it on.
 */
export function categorizeFields(
  sectionKey: string,
  fields: SystemSettingsField[]
): CategorizedSection {
  const declared = SECTION_FIELD_CATEGORIES[sectionKey] ?? []

  const categories = declared.flatMap((category) => {
    const own = fields.filter((f) => belongsTo(category, f.path ?? ""))
    const switchField = own.find((f) => f.path === category.switchPath)
    if (!switchField) return []
    return [
      {
        category,
        switchField,
        fields: own.filter((f) => f !== switchField),
      },
    ]
  })

  const claimed = new Set(
    categories.flatMap(({ switchField, fields: rest }) => [switchField, ...rest])
  )

  return { categories, general: fields.filter((f) => !claimed.has(f)) }
}

/**
 * The category's own settings that must carry a value for it to work: its
 * text credentials. A blank one while the switch is on is the state that
 * produces no error anywhere and no sign-in button either.
 *
 * Booleans are excluded because empty is a legitimate value for them, and so
 * are secret-owned and read-only fields: their value does not live in this
 * form, so this form cannot tell whether it is set and must not claim to.
 */
export function requiredCredentials(
  fields: SystemSettingsField[]
): SystemSettingsField[] {
  return fields.filter(
    (f) => !f.sensitive && !f.readOnly && f.kind !== "bool"
  )
}
