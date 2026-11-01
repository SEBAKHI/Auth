/**
 * The platform settings page's tabs, in order; the first is the default and writes no
 * `tab` parameter. The page renders a panel for each (its content table is keyed by these,
 * so a value added here does not compile until its panel exists), and the command palette's
 * rows are held to this list by `index-keys.test.ts`.
 *
 * Kept out of the page module so the palette can check its links without loading the page.
 */
export const PLATFORM_SETTINGS_TABS = ["identity", "appearance", "templates"] as const

export type PlatformSettingsTabValue = (typeof PLATFORM_SETTINGS_TABS)[number]
