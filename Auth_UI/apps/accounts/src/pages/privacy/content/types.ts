/**
 * Typed contract for the privacy-policy document. Every locale exports one
 * `PrivacyPolicyContent` — the compiler enforces parity the same way the
 * main i18n resources do. Content lives here (not in packages/i18n) because
 * it is a versioned legal document, not UI chrome: it changes on policy
 * revisions, carries its own version stamp, and would bloat the app-wide
 * resource files.
 *
 * Grounded in the deletion plan (Plans/ACCOUNT_DELETION_PLAN.md §5, §12
 * step 10): retention constants, PolicyVersion and the staged-destruction
 * description below MUST stay in sync with AccountDeletionSettings.
 */

/**
 * Fallback version stamp, used only when the API is unreachable. The live
 * value comes from the published policy (`GET /privacy-policy/published`).
 */
export const POLICY_VERSION = "2026.07"

/**
 * Configuration-driven numbers the policy quotes. Served per request from
 * `AccountDeletionSettings`, so changing appsettings changes the published
 * text — the document stores `{{token}}` placeholders, never literals.
 * Statutory windows (KVKK/GDPR 30 days, CCPA 45) are NOT tokens: they come
 * from law, not configuration.
 */
export interface PolicyDisclosure {
  graceDays: number
  otpValidityMinutes: number
  loginAttemptRetentionDays: number
  outboxRetentionDays: number
  policyVersion: string
}

/** Values used when the API cannot be reached (mirrors appsettings defaults). */
export const FALLBACK_DISCLOSURE: PolicyDisclosure = {
  graceDays: 30,
  otpValidityMinutes: 15,
  loginAttemptRetentionDays: 365,
  outboxRetentionDays: 180,
  policyVersion: POLICY_VERSION,
}

/** Substitutes `{{token}}` placeholders with the live disclosure values. */
export function interpolate(text: string, disclosure: PolicyDisclosure): string {
  return text.replace(/\{\{(\w+)\}\}/g, (match, token: string) => {
    const value = (disclosure as unknown as Record<string, unknown>)[token]
    return value === undefined ? match : String(value)
  })
}

/**
 * Official sources for every law the policy references. The page renderer
 * links each occurrence of a term to the official text — the acronyms are
 * Latin in every locale, so one registry serves all 7 languages. Longest
 * terms first so "CCPA/CPRA" wins over "CCPA".
 */
export const LAW_LINKS: ReadonlyArray<{ term: string; url: string }> = [
  {
    term: "CCPA/CPRA",
    url: "https://leginfo.legislature.ca.gov/faces/codes_displayText.xhtml?division=3.&part=4.&lawCode=CIV&title=1.81.5",
  },
  { term: "RGPD", url: "https://eur-lex.europa.eu/eli/reg/2016/679/oj" },
  { term: "GDPR", url: "https://eur-lex.europa.eu/eli/reg/2016/679/oj" },
  {
    term: "KVKK",
    url: "https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=6698&MevzuatTur=1&MevzuatTertip=5",
  },
  {
    term: "CPRA",
    url: "https://leginfo.legislature.ca.gov/faces/codes_displayText.xhtml?division=3.&part=4.&lawCode=CIV&title=1.81.5",
  },
  {
    term: "CCPA",
    url: "https://leginfo.legislature.ca.gov/faces/codes_displayText.xhtml?division=3.&part=4.&lawCode=CIV&title=1.81.5",
  },
]

export interface PolicySection {
  heading: string
  paragraphs: string[]
  bullets?: string[]
}

export interface RetentionRow {
  category: string
  retention: string
  detail: string
}

export interface PrivacyPolicyContent {
  title: string
  /** Localized rendering of the effective date (2026-07-28). */
  effectiveDate: string
  /** Label prefix for the version stamp, e.g. "Version". */
  versionLabel: string
  intro: string[]
  /** Ordered main body (data collected → security), before retention. */
  sections: PolicySection[]
  retention: {
    heading: string
    intro: string
    columns: [string, string, string]
    rows: RetentionRow[]
  }
  /** The deletion explainer that hosts the "Delete my account" button. */
  deletion: {
    heading: string
    paragraphs: string[]
    bullets: string[]
    /** Destructive button label, e.g. "Delete my account". */
    button: string
    /** Hint shown to signed-in users pointing at the profile danger zone. */
    signedInHint: string
  }
  /** Jurisdiction-specific rights: EEA/UK, California, Türkiye, everyone. */
  rights: PolicySection[]
  /** Children, changes-to-this-policy, contact & complaints. */
  closing: PolicySection[]
  /** Label for the optional DPO line (rendered only when a DPO is set). */
  contactDpoLabel: string
  /** Label for the optional VERBİS registration-number line (KVKK). */
  contactVerbisLabel: string
  /** Label for the optional KEP registered-email line (KVKK applications). */
  contactKepLabel: string
  /** Warning banner shown while CONTROLLER still has unfilled placeholders. */
  unfilledWarning: string
}
