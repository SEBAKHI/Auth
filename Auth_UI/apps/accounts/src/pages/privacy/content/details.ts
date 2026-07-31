/**
 * ═══════════════════════════════════════════════════════════════════════
 *  OWNER-FILLABLE FACTS — the ONLY file to edit before publication.
 * ═══════════════════════════════════════════════════════════════════════
 *
 * Every value below is interpolated into ALL 7 language versions of the
 * privacy policy. Entity names, addresses and email addresses are not
 * translated — fill each value once, in its official form.
 *
 * While any value still contains "[", the page displays a prominent
 * "not ready for publication" warning banner (in every language), so a
 * half-completed policy cannot silently ship.
 */
export const CONTROLLER = {
  /** Registered legal name of the data controller (e.g. "Acme Corp LLC"). */
  legalName: "[LEGAL ENTITY NAME]",

  /** Registered address, exactly as it should appear in the policy. */
  address: "[REGISTERED ADDRESS]",

  /** A MONITORED inbox for privacy/rights requests (e.g. privacy@example.com). */
  privacyEmail: "[PRIVACY CONTACT EMAIL]",

  /**
   * Data protection officer name/contact. OPTIONAL: leave as "" if no DPO is
   * appointed (GDPR only requires one in specific cases) — the DPO line is
   * then omitted from the rendered policy in every language.
   */
  dpoContact: "",

  /**
   * VERBİS (Veri Sorumluları Sicili) registration number. OPTIONAL: fill if
   * the controller meets Türkiye's VERBİS registration thresholds; leave ""
   * to omit the line. KVKK compliance is the primary target of this policy —
   * verify the threshold assessment with counsel.
   */
  verbisNo: "",

  /**
   * KEP (kayıtlı elektronik posta) address. OPTIONAL: a Turkish registered
   * e-mail address is one of the application channels under the KVKK
   * application communiqué; leave "" to omit the line.
   */
  kepAddress: "",

  /** Email delivery provider, named in "Who we share data with". */
  emailProvider: "[EMAIL DELIVERY PROVIDER]",

  /** Hosting provider, named in "Who we share data with". */
  hostingProvider: "[HOSTING PROVIDER]",

  /** Country where the service is hosted (international-transfers section). */
  hostingCountry: "[HOSTING COUNTRY]",
} as const

const OPTIONAL_KEYS: ReadonlySet<string> = new Set([
  "dpoContact",
  "verbisNo",
  "kepAddress",
])

/** True while any required value above is still a bracketed placeholder. */
export function hasUnfilledDetails(): boolean {
  return Object.entries(CONTROLLER).some(
    ([key, value]) => !OPTIONAL_KEYS.has(key) && value.includes("[")
  )
}
