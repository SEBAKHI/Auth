import type * as React from "react"

import { AppleIcon, GoogleIcon } from "@authsystem/ui/brand-icons"

import type { SystemSettingsField } from "./sections"

/**
 * How a section divides its settings on screen.
 *
 * A settings section is a flat list of rows only when it answers ONE question.
 * Most of the long ones answer several: the password section holds what a
 * password must contain, what happens after repeated failures, and how the
 * hash is computed — three unrelated decisions in one run of nineteen rows,
 * told apart only by reading every label. A section declares its own division
 * here, and the reader gets it from the layout instead.
 *
 * Two devices, and the difference between them is not decoration:
 *
 * - A **group** is a heading over related rows. Nothing gates them; they
 *   simply belong together. Whitespace and a legend do the grouping, which is
 *   all it takes — and a container around every one of these would flatten the
 *   distinction that makes the other device mean something.
 * - A **category** is a bordered panel headed by its own switch, for a group
 *   that is a THING with an on/off gating the rest: the sign-in providers, the
 *   breached-password check. Turn it off and everything inside stops applying.
 *   This is the loud device, so it stays rare.
 *
 * Anything a section does not claim is general, rendered last. That default is
 * the safety property: a setting the backend adds later appears on its own
 * rather than being silently swallowed by whichever block sits nearest.
 */

/**
 * What a block takes from the section's fields. An exact config path, or a
 * prefix ending in ":" that claims everything under it — `"Certificate:"`
 * takes `Certificate:PfxPath` and every sibling, present and future.
 */
export type FieldClaim = string

interface BlockBase {
  /** Claims, in the order the rows should read. */
  claims: FieldClaim[]
}

/** A bordered panel whose header is the switch that gates it. */
export interface CategoryBlock extends BlockBase {
  kind: "category"
  /** The gating switch, promoted into the panel header. */
  switchPath: string
  /**
   * A brand mark for the header. Only where one genuinely exists: an official
   * logo identifies a panel faster than its name, and an invented glyph for
   * something like a hashing algorithm identifies nothing.
   */
  icon?: React.ComponentType<React.SVGProps<SVGSVGElement>>
}

/** A legend over related rows, at card level. */
export interface GroupBlock extends BlockBase {
  kind: "group"
  /** i18n key under `systemSettings.<section>.groups.*`; `+ "Description"` for its subtitle. */
  key: string
}

export type SectionBlock = CategoryBlock | GroupBlock

/**
 * Per section, in render order. A section absent from here renders the flat
 * list it always did.
 */
export const SECTION_BLOCKS: Record<string, SectionBlock[]> = {
  ExternalAuth: [
    {
      kind: "category",
      switchPath: "Google:Enabled",
      claims: ["Google:"],
      icon: GoogleIcon,
    },
    {
      kind: "category",
      switchPath: "Apple:Enabled",
      claims: ["Apple:"],
      icon: AppleIcon,
    },
    // AvatarImport and RequireNonce belong to no provider and fall through.
  ],

  // Nineteen rows answering four questions. Ordered by who asks them: the
  // rules a person meets when choosing a password, then what happens when
  // they get it wrong, then the check that can reject an otherwise valid one,
  // then the storage nobody sees.
  Password: [
    {
      kind: "group",
      key: "composition",
      claims: [
        "MinimumLength",
        "RequireUppercase",
        "RequireLowercase",
        "RequireDigit",
        "RequireSpecialCharacter",
        "HistoryCount",
      ],
    },
    {
      kind: "group",
      key: "lockout",
      claims: ["MaxFailedAttempts", "LockoutDurationMinutes"],
    },
    {
      kind: "category",
      switchPath: "BreachedPasswordCheck:Enabled",
      claims: ["BreachedPasswordCheck:"],
    },
    {
      kind: "group",
      key: "hashing",
      claims: [
        "Argon2MemorySize",
        "Argon2Iterations",
        "Argon2Parallelism",
        "SaltSize",
        "HashSize",
        "Pepper:Enabled",
      ],
    },
  ],

  // Two token types with two different lifetimes, read as one undifferentiated
  // run — and four consecutive rows whose entire content is the words "Manage
  // secrets", with nothing saying what the four have in common.
  Jwt: [
    { kind: "group", key: "identity", claims: ["Issuer", "Audience"] },
    {
      kind: "group",
      key: "lifetimes",
      claims: [
        "AccessTokenLifetimeMinutes",
        "RefreshTokenLifetimeDays",
        "RotateRefreshTokens",
        "ClockSkewSeconds",
      ],
    },
    {
      kind: "group",
      key: "keyMaterial",
      claims: [
        "KeyId",
        "PrivateKeyPath",
        "PrivateKeyPem",
        "PrivateKeyEncrypted",
        "RefreshTokenEncryptedKey",
      ],
    },
  ],

  // "Send test email" proves the first group and nothing else, and the last
  // group is about the codes rather than the server it sits under — which is
  // where an operator debugging delivery goes looking for a setting that was
  // never the problem.
  Email: [
    {
      kind: "group",
      key: "server",
      claims: [
        "Enabled",
        "SmtpHost",
        "SmtpPort",
        "UseSsl",
        "Username",
        "Password",
      ],
    },
    {
      kind: "group",
      key: "sender",
      claims: ["SenderEmail", "SenderName", "FrontendBaseUrl"],
    },
    {
      kind: "group",
      key: "codes",
      claims: [
        "OtpExpirationMinutes",
        "ResetTokenExpirationMinutes",
        "RateLimitWindowSeconds",
        "MaxOtpRequestsPerWindow",
      ],
    },
  ],

  // Seven fields of the identical shape ("… (days)") following four
  // operational ones. The seven answer a different question from the four,
  // and only their shared shape says so.
  ExpiredDataCleanup: [
    {
      kind: "group",
      key: "sweeper",
      claims: [
        "Enabled",
        "WorkerPollMinutes",
        "BatchSize",
        "MaxRowsPerTablePerRun",
      ],
    },
    {
      kind: "group",
      key: "retention",
      claims: [
        "AuthorizationCodeDays",
        "TwoFactorChallengeDays",
        "PasswordResetTokenDays",
        "EmailVerificationTokenDays",
        "IdpSessionDays",
        "RefreshTokenDays",
        "PendingRegistrationDays",
      ],
    },
  ],

  // Four read-only rows describing where the files sit, then five deciding
  // what may be uploaded. Two questions, one run.
  ImageStorage: [
    {
      kind: "group",
      key: "location",
      claims: ["Provider", "PhysicalPath", "PublicBaseUrl", "RequestPath"],
    },
    {
      kind: "group",
      key: "limits",
      claims: [
        "MaxSizeBytes",
        "MaxMegapixels",
        "MaxEdgePx",
        "WebpQuality",
        "AllowedContentTypes",
      ],
    },
  ],

  // Two settings the person deleting their account actually experiences, and
  // four nobody outside this screen will ever see.
  AccountDeletion: [
    { kind: "group", key: "request", claims: ["GraceDays", "OtpExpirationMinutes"] },
    {
      kind: "group",
      key: "execution",
      claims: [
        "WorkerPollMinutes",
        "WorkerBatchSize",
        "MaxExecutionAttempts",
        "IdentifierHmacKeyPlain",
      ],
    },
  ],

  // Nine consecutive text fields with no seam anywhere. Three of them are
  // optional and jurisdiction-specific, which the run gives no way to tell.
  DataController: [
    {
      kind: "group",
      key: "identity",
      claims: ["LegalName", "Address", "PrivacyEmail", "DpoContact"],
    },
    {
      kind: "group",
      key: "processors",
      claims: ["EmailProvider", "HostingProvider", "HostingCountry"],
    },
    { kind: "group", key: "registrations", claims: ["VerbisNo", "KepAddress"] },
  ],

  // A delivery worker, and inside it an unrelated feature with its own switch.
  Notifications: [
    {
      kind: "group",
      key: "delivery",
      claims: [
        "UseOutbox",
        "PollIntervalSeconds",
        "BatchSize",
        "MaxAttempts",
        "StaleClaimMinutes",
      ],
    },
    {
      kind: "category",
      switchPath: "NewDeviceAlertEnabled",
      claims: ["NewDeviceAlertEnabled", "NewDeviceAlertMinIntervalMinutes"],
    },
  ],

  // All but one of these settings are limit/window pairs, and their labels
  // already pair them - each says which policy it belongs to. What the flat
  // list hides is the odd one out: an upload concurrency cap that is not a
  // rate at all, and whose hint has to open by saying so. The two headings
  // name the two KINDS of limit, which is the distinction no label can carry
  // alone.
  RateLimiting: [
    {
      kind: "group",
      key: "windows",
      claims: [
        "LoginPermitLimit",
        "LoginWindowSeconds",
        "RegisterPermitLimit",
        "RegisterWindowSeconds",
        "SignInPagePermitLimit",
        "SignInPageWindowSeconds",
        "PasswordResetPermitLimit",
        "PasswordResetWindowSeconds",
        "ApiKeyValidatePermitLimit",
        "ApiKeyValidateWindowSeconds",
        "RegistrationFollowupPermitLimit",
        "RegistrationFollowupWindowSeconds",
      ],
    },
    { kind: "group", key: "concurrency", claims: ["ImageUploadConcurrencyLimit"] },
  ],

  // Here the split carries a rule rather than a theme. The global ceiling
  // applies ON TOP of every per-route policy, so a global value tighter than
  // one of them caps that policy silently - and nothing in a flat list says
  // the first three rows outrank every row below them.
  GatewayRateLimiting: [
    {
      kind: "group",
      key: "global",
      claims: ["GlobalPermitLimit", "GlobalWindowSeconds", "GlobalQueueLimit"],
    },
    {
      kind: "group",
      key: "perRoute",
      claims: [
        "AuthPermitLimit",
        "AuthWindowSeconds",
        "RegisterPermitLimit",
        "RegisterWindowSeconds",
        "ApiPermitLimit",
        "ApiWindowSeconds",
        "AdminPermitLimit",
        "AdminWindowSeconds",
        "RegistrationFollowupPermitLimit",
        "RegistrationFollowupWindowSeconds",
      ],
    },
  ],
}

export interface ResolvedBlock {
  block: SectionBlock
  /** Present only for a category: the switch its header wears. */
  switchField?: SystemSettingsField
  /** The block's own rows, in claim order. */
  fields: SystemSettingsField[]
}

export interface SectionLayout {
  blocks: ResolvedBlock[]
  /** Claimed by nothing, in backend order. Rendered after every block. */
  general: SystemSettingsField[]
}

function claimed(claim: FieldClaim, path: string): boolean {
  return claim.endsWith(":") ? path.startsWith(claim) : path === claim
}

/**
 * Splits a section's fields into its declared blocks plus the unclaimed
 * remainder.
 *
 * Two degradations, both deliberate, because the console and the backend ship
 * separately and either can be a release ahead:
 *
 * - A category whose switch the payload does not carry is DROPPED, and its
 *   fields fall through to the general list. A panel with no way to turn its
 *   subject on would be worse than the flat list this section rendered before.
 * - A block left holding nothing is dropped rather than drawn as an empty
 *   heading.
 *
 * A field is claimed at most once: the first block that names it wins, and the
 * registry test asserts no two blocks in a section reach for the same path.
 */
export function resolveSectionLayout(
  sectionKey: string,
  fields: SystemSettingsField[]
): SectionLayout {
  const declared = SECTION_BLOCKS[sectionKey] ?? []
  const taken = new Set<SystemSettingsField>()

  const blocks = declared.flatMap<ResolvedBlock>((block) => {
    // Claim order, not backend order: a block decides how its own rows read.
    const own = block.claims.flatMap((claim) =>
      fields.filter((f) => !taken.has(f) && claimed(claim, f.path ?? ""))
    )

    if (block.kind === "category") {
      const switchField = own.find((f) => f.path === block.switchPath)
      if (!switchField) return []
      own.forEach((f) => taken.add(f))
      return [
        {
          block,
          switchField,
          fields: own.filter((f) => f !== switchField),
        },
      ]
    }

    if (own.length === 0) return []
    own.forEach((f) => taken.add(f))
    return [{ block, fields: own }]
  })

  return { blocks, general: fields.filter((f) => !taken.has(f)) }
}

/**
 * A category's settings that must carry a value for it to work: its text
 * fields. A blank one while the switch is on is the state that produces no
 * error anywhere and no working feature either.
 *
 * Booleans are excluded because empty is a legitimate value for them, and so
 * are secret-owned and read-only fields: their value does not live in this
 * form, so this form cannot tell whether it is set and must not claim to.
 */
export function requiredCredentials(
  fields: SystemSettingsField[]
): SystemSettingsField[] {
  return fields.filter((f) => !f.sensitive && !f.readOnly && f.kind !== "bool")
}
