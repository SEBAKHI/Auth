import type * as React from "react"
import { TriangleAlert } from "lucide-react"

import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Separator } from "@authsystem/ui/separator"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@authsystem/ui/table"

/**
 * Shared privacy-policy document model and renderer.
 *
 * Lives in the UI package so the public accounts page and the console's
 * editor preview render through the SAME code — a preview that could drift
 * from the published page would be worse than no preview at all.
 */

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
  effectiveDate: string
  versionLabel: string
  intro: string[]
  sections: PolicySection[]
  retention: {
    heading: string
    intro: string
    columns: [string, string, string]
    rows: RetentionRow[]
  }
  deletion: {
    heading: string
    paragraphs: string[]
    bullets: string[]
    button: string
    signedInHint: string
  }
  rights: PolicySection[]
  closing: PolicySection[]
  contactDpoLabel: string
  contactVerbisLabel: string
  contactKepLabel: string
  unfilledWarning: string
}

/**
 * Configuration-driven numbers the policy quotes, served by the API from the
 * running settings. The document stores `{{token}}` placeholders, never
 * literals, so the text cannot contradict the system.
 */
export interface PolicyDisclosure {
  graceDays: number
  otpValidityMinutes: number
  loginAttemptRetentionDays: number
  outboxRetentionDays: number
  identifierReservationDays: number
  policyVersion: string
  /**
   * Data-controller identity, served from settings. These are legal facts, so
   * they are never optional at the type level: a missing value must render as a
   * visible placeholder that trips `hasUnfilledControllerDetails`, never as
   * `undefined` (which `interpolate` leaves as a raw `{{token}}` on a public
   * page) or `null` (which stringifies to the word "null").
   */
  legalName: string
  address: string
  privacyEmail: string
  emailProvider: string
  hostingProvider: string
  hostingCountry: string
  /** Optional by law; blank omits its line entirely rather than rendering empty. */
  dpoContact: string
  verbisNo: string
  kepAddress: string
}

/**
 * Tokens an editor may insert; kept beside the type they populate.
 *
 * The three optional controller fields are deliberately absent: they have no
 * host sentence, they are rendered as whole label/value lines that disappear
 * when blank, and a flat token cannot remove the label it sits next to.
 */
export const POLICY_TOKENS = [
  "{{graceDays}}",
  "{{otpValidityMinutes}}",
  "{{loginAttemptRetentionDays}}",
  "{{outboxRetentionDays}}",
  "{{identifierReservationDays}}",
  "{{legalName}}",
  "{{address}}",
  "{{privacyEmail}}",
  "{{emailProvider}}",
  "{{hostingProvider}}",
  "{{hostingCountry}}",
] as const

/**
 * True while the policy cannot name its own controller — blank or still a
 * bracketed placeholder in any legally required field.
 *
 * This deliberately inspects the DISCLOSURE being rendered rather than a
 * compiled-in constant. The previous check tested a build-time value in the
 * accounts bundle, so it stayed silent about the document actually served from
 * the database, which is the one users read.
 */
export function hasUnfilledControllerDetails(disclosure: PolicyDisclosure): boolean {
  return [
    disclosure.legalName,
    disclosure.address,
    disclosure.privacyEmail,
    disclosure.emailProvider,
    disclosure.hostingProvider,
    disclosure.hostingCountry,
  ].some((value) => !value || value.includes("["))
}

/** Official sources for every law the policy names. */
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

const LAW_PATTERN = new RegExp(
  `(${LAW_LINKS.map((law) => law.term.replace("/", "\\/")).join("|")})`,
  "g"
)

/** Substitutes `{{token}}` placeholders with the live disclosure values. */
export function interpolate(text: string, disclosure: PolicyDisclosure): string {
  return text.replace(/\{\{(\w+)\}\}/g, (match, token: string) => {
    const value = (disclosure as unknown as Record<string, unknown>)[token]
    return value === undefined ? match : String(value)
  })
}

/** Links every law reference to its official text. */
function withLawLinks(text: string): React.ReactNode {
  const parts = text.split(LAW_PATTERN)
  if (parts.length === 1) return text
  return parts.map((part, index) => {
    const law = LAW_LINKS.find((candidate) => candidate.term === part)
    return law ? (
      <a
        key={`${part}-${index}`}
        href={law.url}
        target="_blank"
        rel="noreferrer noopener"
        className="underline underline-offset-4 hover:text-foreground"
      >
        {part}
      </a>
    ) : (
      part
    )
  })
}

function Section({
  section,
  disclosure,
}: {
  section: PolicySection
  disclosure: PolicyDisclosure
}) {
  const render = (text: string) => withLawLinks(interpolate(text, disclosure))

  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold tracking-tight">
        {render(section.heading)}
      </h2>
      {section.paragraphs.map((paragraph, index) => (
        <p
          key={`${paragraph.slice(0, 24)}-${index}`}
          className="text-sm leading-relaxed text-muted-foreground"
        >
          {render(paragraph)}
        </p>
      ))}
      {section.bullets && section.bullets.length > 0 ? (
        <ul className="list-disc ps-5 text-sm leading-relaxed text-muted-foreground [&>li+li]:mt-2">
          {section.bullets.map((bullet, index) => (
            <li key={`${bullet.slice(0, 24)}-${index}`}>{render(bullet)}</li>
          ))}
        </ul>
      ) : null}
    </section>
  )
}

/**
 * Renders the policy body: intro, sections, the retention table, the deletion
 * explainer (with a caller-supplied action slot), rights and closing.
 * Page chrome (logo, version badge, contact lines) stays with the caller.
 */
export function PolicyDocument({
  content,
  disclosure,
  dir,
  deletionAction,
  controllerStatus = "known",
}: {
  content: PrivacyPolicyContent
  disclosure: PolicyDisclosure
  /** Text direction of the rendered language. */
  dir: "ltr" | "rtl"
  /** Rendered under the deletion bullets (the "Delete my account" button). */
  deletionAction?: React.ReactNode
  /**
   * Whether `disclosure` reflects a settled answer.
   *
   * Defaults to "known" so no caller changes behaviour by accident. Pass
   * "pending" while the disclosure is still loading: the unfilled banner is a
   * statement about the operator's configuration, and asserting it before the
   * answer arrives makes it flash on every load of a page whose configuration
   * is in fact complete.
   */
  controllerStatus?: "known" | "pending"
}) {
  const render = (text: string) => withLawLinks(interpolate(text, disclosure))

  // Conditionally-required by law, so the system cannot decide for the operator
  // whether they apply. Rendered as whole lines that disappear when blank —
  // "DPO: " with nothing after it is worse than no line at all.
  const optionalContact: Array<[string, string]> = [
    [content.contactDpoLabel, disclosure.dpoContact],
    [content.contactVerbisLabel, disclosure.verbisNo],
    [content.contactKepLabel, disclosure.kepAddress],
  ]
  const shownContact = optionalContact.filter(([, value]) => value)

  return (
    <>
      {/* `content.unfilledWarning` guards an empty alert: a brand-new language
          document has no warning text yet, which used to render a red box with
          nothing in it. */}
      {controllerStatus === "known" &&
      hasUnfilledControllerDetails(disclosure) &&
      content.unfilledWarning ? (
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertDescription>{content.unfilledWarning}</AlertDescription>
        </Alert>
      ) : null}

      <div className="flex flex-col gap-3">
        {content.intro.map((paragraph, index) => (
          <p
            key={`${paragraph.slice(0, 24)}-${index}`}
            className="text-sm leading-relaxed text-muted-foreground"
          >
            {render(paragraph)}
          </p>
        ))}
      </div>

      {content.sections.map((section, index) => (
        <Section
          key={`${section.heading}-${index}`}
          section={section}
          disclosure={disclosure}
        />
      ))}

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold tracking-tight">
          {render(content.retention.heading)}
        </h2>
        <p className="text-sm leading-relaxed text-muted-foreground">
          {render(content.retention.intro)}
        </p>
        {/* Per-cell dir: RTL locales scramble mixed-direction table content
            when only the table element carries the direction. */}
        <Table dir={dir}>
          <TableHeader>
            <TableRow>
              {content.retention.columns.map((column, index) => (
                <TableHead key={`${column}-${index}`} dir={dir}>
                  {render(column)}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {content.retention.rows.map((row, index) => (
              <TableRow key={`${row.category}-${index}`}>
                <TableCell
                  dir={dir}
                  className="whitespace-normal align-top font-medium"
                >
                  {render(row.category)}
                </TableCell>
                <TableCell dir={dir} className="whitespace-normal align-top">
                  {render(row.retention)}
                </TableCell>
                <TableCell
                  dir={dir}
                  className="whitespace-normal align-top text-muted-foreground"
                >
                  {render(row.detail)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </section>

      <section className="flex flex-col gap-3">
        <h2 className="text-lg font-semibold tracking-tight">
          {render(content.deletion.heading)}
        </h2>
        {content.deletion.paragraphs.map((paragraph, index) => (
          <p
            key={`${paragraph.slice(0, 24)}-${index}`}
            className="text-sm leading-relaxed text-muted-foreground"
          >
            {render(paragraph)}
          </p>
        ))}
        <ul className="list-disc ps-5 text-sm leading-relaxed text-muted-foreground [&>li+li]:mt-2">
          {content.deletion.bullets.map((bullet, index) => (
            <li key={`${bullet.slice(0, 24)}-${index}`}>{render(bullet)}</li>
          ))}
        </ul>
        {deletionAction}
      </section>

      {content.rights.map((section, index) => (
        <Section
          key={`${section.heading}-${index}`}
          section={section}
          disclosure={disclosure}
        />
      ))}

      {content.closing.map((section, index) => (
        <Section
          key={`${section.heading}-${index}`}
          section={section}
          disclosure={disclosure}
        />
      ))}

      {shownContact.length > 0 ? (
        <>
          <Separator />
          <div className="flex flex-col gap-1 text-sm text-muted-foreground">
            {shownContact.map(([label, value]) => (
              <p key={label}>
                {label}: {value}
              </p>
            ))}
          </div>
        </>
      ) : null}
    </>
  )
}
