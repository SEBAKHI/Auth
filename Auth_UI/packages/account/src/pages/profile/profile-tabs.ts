/**
 * The profile's tab names, as they appear in the URL.
 *
 * Its own module because two places need it and neither may import the other's
 * baggage: the page validates the incoming parameter against this list, and the
 * console's search index builds `/profile?tab=…` links from it. A tab renamed
 * on one side and not the other produces a link that silently opens the first
 * tab instead — a test asserts the two agree.
 *
 * The first entry is the default, and the default writes no parameter, so the
 * canonical address of the profile stays `/profile`.
 */
export const PROFILE_TABS = ["account", "sessions", "security"] as const

export type ProfileTab = (typeof PROFILE_TABS)[number]
