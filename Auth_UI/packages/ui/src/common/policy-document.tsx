import type * as React from "react"

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"

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
  policyVersion: string
}

/** Tokens an editor may insert; kept beside the type they populate. */
export const POLICY_TOKENS = [
  "{{graceDays}}",
  "{{otpValidityMinutes}}",
  "{{loginAttemptRetentionDays}}",
  "{{outboxRetentionDays}}",
] as const

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
}: {
  content: PrivacyPolicyContent
  disclosure: PolicyDisclosure
  /** Text direction of the rendered language. */
  dir: "ltr" | "rtl"
  /** Rendered under the deletion bullets (the "Delete my account" button). */
  deletionAction?: React.ReactNode
}) {
  const render = (text: string) => withLawLinks(interpolate(text, disclosure))

  return (
    <>
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
    </>
  )
}
