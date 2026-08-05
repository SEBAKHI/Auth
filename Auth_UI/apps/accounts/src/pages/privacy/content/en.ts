import type { PrivacyPolicyContent } from "./types"

export const en: PrivacyPolicyContent = {
  title: "Privacy Policy",
  effectiveDate: "Effective 28 July 2026",
  versionLabel: "Version",
  unfilledWarning:
    "Draft — controller details are not filled in yet. This page must not be published until every placeholder is completed.",
  contactDpoLabel: "Data protection officer",
  contactVerbisLabel: "VERBİS registration number",
  contactKepLabel: "Registered e-mail (KEP)",
  intro: [
    `This policy explains what personal data the {{legalName}} account service collects, why we collect it, how long we keep it, and the rights you have over it — including how to delete your account and everything attached to it.`,
    "It also serves as the disclosure required by Article 10 of Türkiye's Personal Data Protection Law No. 6698 (KVKK) — our primary compliance framework — and is written to meet the EU/EEA General Data Protection Regulation (GDPR) and the California Consumer Privacy Act as amended (CCPA/CPRA). The controls described in this policy are available to every user, regardless of location.",
  ],
  sections: [
    {
      heading: "Data we collect",
      paragraphs: [
        "We collect only what the account service needs to work. We never ask for more than that, and optional fields are clearly optional.",
      ],
      bullets: [
        "Account and profile: email address, first and last name, optional display name, optional phone number (stored encrypted), optional profile picture, preferred language, time zone and theme.",
        "Credentials and security settings: your password (stored only as a one-way Argon2id hash — we cannot read it), optional two-factor authentication secret and recovery codes (stored encrypted), and password-change history (hashes only, to prevent reuse).",
        "Sign-in with Google or Apple: your provider identifier, email and name as shared by the provider. For Apple, a revocation token is stored encrypted solely so we can revoke Apple's sign-in permission when you delete your account.",
        "Security and usage records: sign-in attempts (time, IP address, browser identifier, outcome), active sessions and tokens, and an audit log of account-related actions.",
        "Communications: a record of the service emails we send you (verification codes, security notices, deletion confirmations). Message content that carries one-time codes or sign-in links is removed from this record as soon as the email is delivered — not even our administrators can read it.",
      ],
    },
    {
      heading: "Why we process it (legal bases)",
      paragraphs: [
        "Your data is collected electronically, through the forms and sign-in flows of the service itself. Each purpose rests on a legal basis under KVKK Article 5 and its GDPR Article 6 equivalent:",
      ],
      bullets: [
        "To provide your account — authentication, sessions, profile, organization membership (necessary for the performance of a contract: KVKK Art. 5(2)(c); GDPR Art. 6(1)(b)).",
        "To keep accounts secure — sign-in attempt records, rate limiting, session revocation, audit logging, fraud prevention (legitimate interests: KVKK Art. 5(2)(f); GDPR Art. 6(1)(f)).",
        "To meet legal obligations — retaining minimal evidence that a deletion request was honored (KVKK Art. 5(2)(ç); GDPR Art. 6(1)(c)).",
        "Optional data — the phone number and profile picture are processed only because you chose to provide them, and you can remove them at any time (explicit consent / açık rıza: KVKK Art. 5(1); GDPR Art. 6(1)(a)).",
      ],
    },
    {
      heading: "What we do not do",
      paragraphs: [
        "We do not sell your personal data, and we do not share it for cross-context behavioral advertising — in CCPA terms, no \"sale\" and no \"sharing\" has occurred in the preceding 12 months and none is planned. We do not run advertising or third-party analytics trackers in the account service, and we do not use your data for automated decisions that produce legal or similarly significant effects.",
      ],
    },
    {
      heading: "Cookies and local storage",
      paragraphs: [
        "The account service uses only what sign-in strictly requires: an essential session cookie that keeps you signed in across our own pages, and browser local storage that holds your session's refresh token and your language/theme preference. There are no analytics, advertising or cross-site tracking cookies.",
      ],
    },
    {
      heading: "Who we share data with",
      paragraphs: [
        "We share personal data only with processors that operate the service, and only to the extent needed:",
      ],
      bullets: [
        "Google (Sign in with Google) and Apple (Sign in with Apple) — only when you choose to sign in with them; the exchange is governed by their own privacy policies.",
        `Our email delivery provider, {{emailProvider}}, to send the service emails described above.`,
        `Our hosting provider, {{hostingProvider}}, which stores the service's data.`,
        "Public authorities, if and only if a valid legal demand compels us.",
      ],
    },
    {
      heading: "International transfers",
      paragraphs: [
        `The service is hosted in {{hostingCountry}}. Where personal data is transferred out of Türkiye, we do so under KVKK Article 9: an adequacy decision of the Personal Data Protection Board where one exists, and otherwise the appropriate safeguards that Article provides for (such as the Board's standard contract, notified to the Authority as required). For data leaving the EEA or the UK we additionally rely on adequacy decisions or the European Commission's Standard Contractual Clauses. The security measures below apply in every case.`,
      ],
    },
    {
      heading: "How we protect it",
      paragraphs: ["Security is layered and applies to every account:"],
      bullets: [
        "Passwords are hashed with Argon2id; verification codes are stored only as hashes; reset links are stored only as HMAC digests.",
        "Your phone number, two-factor secret and Apple revocation token are encrypted with AES-256-GCM under a key that is unique to your account.",
        "All traffic is encrypted in transit (TLS). Sign-in is protected by rate limiting and account lockout; sessions can be revoked at any time and are all revoked instantly when you request deletion.",
        "Account actions are recorded in an audit log so suspicious activity can be detected and investigated.",
      ],
    },
  ],
  retention: {
    heading: "How long we keep data",
    intro:
      "We keep personal data no longer than the purpose requires. These periods are enforced automatically by the system, not by manual review:",
    columns: ["Data", "Kept for", "What happens then"],
    rows: [
      {
        category: "Account and profile data",
        retention: "Until you delete your account (+ {{graceDays}}-day recovery window)",
        detail:
          "Permanently destroyed by the staged deletion process described below.",
      },
      {
        category: "Password hashes and two-factor secrets",
        retention: "Until account deletion",
        detail: "Stored only hashed or encrypted; destroyed with the account.",
      },
      {
        category: "Sessions and tokens",
        retention: "Until expiry or sign-out",
        detail: "All revoked immediately when deletion is requested.",
      },
      {
        category: "Sign-in attempt records (incl. IP address)",
        retention: "{{loginAttemptRetentionDays}} days",
        detail:
          "Purged automatically; de-identified immediately when the account is deleted.",
      },
      {
        category: "Security audit log",
        retention: "De-identified at account deletion",
        detail:
          "All personal fields are removed; the fact that a deletion was carried out (event type and timestamp only) is kept for at least 3 years as legal evidence.",
      },
      {
        category: "Record of sent service emails",
        retention: "{{outboxRetentionDays}} days",
        detail: "Purged automatically.",
      },
      {
        category: "Deletion verification codes",
        retention: "{{otpValidityMinutes}} minutes (code validity)",
        detail:
          "Stored only as hashes; expired entries are removed by the daily cleanup job.",
      },
      {
        category: "Deletion record (hashed identifier)",
        retention: "{{identifierReservationDays}} days",
        detail:
          "A keyed one-way digest of the deleted email, kept so nobody — including you — can re-register that address while the reservation lasts. An address cannot be read out of a digest, but we keep the key that can test one, so this is a pseudonymous record rather than an anonymous one. It is deleted when the window ends, and the address becomes available again.",
      },
      {
        category: "Backups",
        retention: "6 months at most",
        detail:
          "Backup rotation is enforced by the hosting platform's retention configuration. Independently of it, deleting your account destroys the encryption key for your encrypted fields, making that data permanently unreadable even inside existing backups.",
      },
    ],
  },
  deletion: {
    heading: "Deleting your account",
    paragraphs: [
      "You can request the permanent deletion of your account and personal data at any time, without contacting anyone. The request is acknowledged immediately and completed within {{graceDays}} days:",
    ],
    bullets: [
      "Your account is deactivated at once and signed out of every device; every session, token and sign-in permission is revoked immediately.",
      "For {{graceDays}} days you can change your mind — signing back in restores the account and cancels the deletion. You receive an email confirming each step.",
      "After the {{graceDays}}-day window, deletion is carried out automatically and is irreversible: profile data is erased, security records are de-identified, per-account encryption keys are destroyed (crypto-shredding, covering backups), and — if you used Sign in with Apple — Apple is told to revoke the sign-in permission.",
      "Your email address and username are never recycled: only one-way digests remain, so nobody else can ever register them.",
    ],
    button: "Delete my account",
    signedInHint:
      "Signed in? You can also do this from your profile's Account tab (Danger zone).",
  },
  rights: [
    {
      heading: "Your rights in Türkiye (KVKK)",
      paragraphs: [
        "Under Article 11 of Law No. 6698, as the data subject (ilgili kişi) you have the right to learn whether your data is processed, to request information about that processing and its purpose, to know the domestic and foreign third parties it is transferred to, to request correction of incomplete or inaccurate data, to request deletion or destruction under Article 7, to ask that corrections and deletions be notified to recipients, to object to a result produced exclusively by automated analysis, and to claim compensation for damage caused by unlawful processing.",
        "Applications are made to the data controller using the contact details below — in writing, via our registered e-mail (KEP) address where provided, or from an e-mail address you have previously notified to us — per the Communiqué on Application Procedures. We answer within 30 days at the latest, free of charge. If the application is rejected or unanswered, you may complain to the Personal Data Protection Board (Kişisel Verileri Koruma Kurulu).",
      ],
    },
    {
      heading: "Your rights in the EEA and the UK (GDPR)",
      paragraphs: [
        "You have the right to access your data, to have it corrected, to have it erased, to restrict or object to processing, to receive a portable copy, and to withdraw consent at any time where processing is based on consent. Most of these you can exercise directly in the app (profile editing, account deletion); for the rest, contact us using the details below. You also have the right to lodge a complaint with your national supervisory authority.",
      ],
    },
    {
      heading: "Your rights in California (CCPA/CPRA)",
      paragraphs: [
        "You have the right to know what personal information we collect and to access it, to correct it, to delete it, and not to be discriminated against for exercising these rights. Because we do not sell or share personal information and use sensitive personal information only to provide the service, there is nothing to opt out of. We honor verified requests within 45 days. You may use an authorized agent to submit a request on your behalf.",
      ],
    },
    {
      heading: "Everyone else",
      paragraphs: [
        "The same controls — access, correction, deletion, and the in-app deletion flow above — are available to every user, regardless of where you live.",
      ],
    },
  ],
  closing: [
    {
      heading: "Children",
      paragraphs: [
        "The service is not directed at children and may not be used by anyone under 16. We do not knowingly collect children's data; if you believe a child has created an account, contact us and we will delete it.",
      ],
    },
    {
      heading: "Changes to this policy",
      paragraphs: [
        "Each revision of this policy carries a version number (year.month, shown at the top). If we make material changes, we will notify you by email or an in-app notice before they take effect. Deletion requests are always honored under the terms in force when the request was made.",
      ],
    },
    {
      heading: "Contact and complaints",
      paragraphs: [
        `Data controller: {{legalName}}, {{address}}. Privacy contact: {{privacyEmail}}. We answer rights requests within 30 days (GDPR/KVKK) and 45 days (CCPA). If you are unsatisfied, you may complain to your supervisory authority: in the EEA/UK your national data protection authority, in Türkiye the Kişisel Verileri Koruma Kurumu, in California the California Privacy Protection Agency or Attorney General.`,
      ],
    },
  ],
}
