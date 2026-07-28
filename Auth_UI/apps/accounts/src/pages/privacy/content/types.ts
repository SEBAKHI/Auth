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

/** Mirrors `AccountDeletionSettings.PolicyVersion` — bump both together. */
export const POLICY_VERSION = "2026.07"

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
