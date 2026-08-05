/**
 * Contract for the bundled privacy-policy documents.
 *
 * The document model and renderer live in `@authsystem/ui/common/policy-document`
 * so the public page and the console's editor preview share one implementation
 * — re-exported here to keep the locale files' imports stable.
 *
 * These bundled documents are a FALLBACK only: the published policy is stored
 * per (version, language) in the database and edited from the console. They
 * also seed a fresh install (see `16_PrivacyPolicyContent.sql`, generated from
 * these files).
 */
export type {
  PolicyDisclosure,
  PolicySection,
  PrivacyPolicyContent,
  RetentionRow,
} from "@authsystem/ui/common/policy-document"

export {
  LAW_LINKS,
  POLICY_TOKENS,
  interpolate,
} from "@authsystem/ui/common/policy-document"

import type { PolicyDisclosure } from "@authsystem/ui/common/policy-document"

/**
 * Fallback version stamp, used only when the API is unreachable. The live
 * value comes from the published policy (`GET /privacy-policy/published`).
 */
export const POLICY_VERSION = "2026.07"

/** Values used when the API cannot be reached (mirrors appsettings defaults). */
export const FALLBACK_DISCLOSURE: PolicyDisclosure = {
  graceDays: 30,
  otpValidityMinutes: 15,
  loginAttemptRetentionDays: 365,
  outboxRetentionDays: 180,
  identifierReservationDays: 1095,
  policyVersion: POLICY_VERSION,
  // Controller identity now comes from the API. These bracketed values are the
  // OFFLINE fallback only: they keep the sentence self-describing when the API
  // is unreachable, and hasUnfilledControllerDetails() treats the "[" as
  // unfilled so the draft banner shows rather than a silent hole in a legal
  // document. The real values live in System Settings -> Data controller.
  legalName: "[LEGAL ENTITY NAME]",
  address: "[REGISTERED ADDRESS]",
  privacyEmail: "[PRIVACY CONTACT EMAIL]",
  emailProvider: "[EMAIL DELIVERY PROVIDER]",
  hostingProvider: "[HOSTING PROVIDER]",
  hostingCountry: "[HOSTING COUNTRY]",
  dpoContact: "",
  verbisNo: "",
  kepAddress: "",
}
