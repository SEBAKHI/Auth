import { ShieldCheck, TriangleAlert } from "lucide-react"
import type * as React from "react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"

import { useAuth } from "@astoom/auth/auth-context"
import { directionForLanguage } from "@astoom/i18n"
import { Alert, AlertDescription } from "@astoom/ui/alert"
import { Badge } from "@astoom/ui/badge"
import { BrandingLogo } from "@astoom/ui/branding"
import { Button } from "@astoom/ui/button"
import { LanguageToggle } from "@astoom/ui/common/language-toggle"
import { ThemeToggle } from "@astoom/ui/common/theme-toggle"
import { Separator } from "@astoom/ui/separator"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@astoom/ui/table"

import { CONTROLLER, hasUnfilledDetails } from "./content/details"
import {
  LAW_LINKS,
  interpolate,
  type PolicyDisclosure,
  type PolicySection,
} from "./content/types"
import { usePrivacyPolicy } from "./use-privacy-policy"

const LAW_PATTERN = new RegExp(
  `(${LAW_LINKS.map((law) => law.term.replace("/", "\\/")).join("|")})`,
  "g"
)

/**
 * Turns every reference to a named law (KVKK, GDPR/RGPD, CCPA/CPRA) into a
 * link to the official text — the acronyms are Latin in all 7 locales, so
 * one pattern serves every language.
 */
function withLawLinks(text: string): React.ReactNode {
  const parts = text.split(LAW_PATTERN)
  if (parts.length === 1) return text
  return parts.map((part, index) => {
    const law = LAW_LINKS.find((candidate) => candidate.term === part)
    return law ? (
      // eslint-disable-next-line react/no-array-index-key
      <a
        key={index}
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
      {section.paragraphs.map((paragraph) => (
        <p
          key={paragraph.slice(0, 40)}
          className="text-sm leading-relaxed text-muted-foreground"
        >
          {render(paragraph)}
        </p>
      ))}
      {section.bullets ? (
        <ul className="list-disc ps-5 text-sm leading-relaxed text-muted-foreground [&>li+li]:mt-2">
          {section.bullets.map((bullet) => (
            <li key={bullet.slice(0, 40)}>{render(bullet)}</li>
          ))}
        </ul>
      ) : null}
    </section>
  )
}

/**
 * Public, versioned privacy policy — the R6 compliance surface. It doubles as
 * the KVKK Article 10 disclosure and hosts the "Delete my account" entry
 * point required by app-store data-deletion policies: anonymous visitors go
 * to the public no-login wizard, signed-in users to the profile danger zone
 * (the wizard route is anonymous-only).
 *
 * Content is authored in the console and stored per (version, language) — the
 * bundled document is only a fallback for when the API is unreachable. The
 * numeric disclosures come from the running configuration, so the rendered
 * text can never contradict the system it describes.
 */
export function PrivacyPolicyPage() {
  const { i18n, t } = useTranslation()
  const { status } = useAuth()
  const navigate = useNavigate()

  const { policy } = usePrivacyPolicy()
  const { content, disclosure } = policy
  const dir = directionForLanguage(i18n.language)
  const render = (text: string) => withLawLinks(interpolate(text, disclosure))

  const optionalContact: Array<[string, string]> = [
    [content.contactDpoLabel, CONTROLLER.dpoContact],
    [content.contactVerbisLabel, CONTROLLER.verbisNo],
    [content.contactKepLabel, CONTROLLER.kepAddress],
  ]

  return (
    <div className="relative min-h-svh">
      <div className="absolute end-4 top-4 flex items-center gap-1">
        <LanguageToggle />
        <ThemeToggle />
      </div>

      <div className="mx-auto flex w-full max-w-3xl flex-col gap-8 px-4 py-12">
        <header className="flex flex-col items-center gap-2 text-center">
          <BrandingLogo
            className="mb-2 h-16 w-auto max-w-56 object-contain"
            fallback={
              <div className="mb-2 flex size-14 items-center justify-center rounded-2xl bg-primary text-primary-foreground">
                <ShieldCheck className="size-7" />
              </div>
            }
          />
          <h1 className="text-2xl font-semibold tracking-tight">
            {content.title}
          </h1>
          <div className="flex flex-wrap items-center justify-center gap-2">
            <Badge variant="secondary">
              {content.versionLabel} {policy.version}
            </Badge>
            <span className="text-sm text-muted-foreground">
              {content.effectiveDate}
            </span>
          </div>
        </header>

        {hasUnfilledDetails() ? (
          <Alert variant="destructive">
            <TriangleAlert />
            <AlertDescription>{content.unfilledWarning}</AlertDescription>
          </Alert>
        ) : null}

        <div className="flex flex-col gap-3">
          {content.intro.map((paragraph) => (
            <p
              key={paragraph.slice(0, 40)}
              className="text-sm leading-relaxed text-muted-foreground"
            >
              {render(paragraph)}
            </p>
          ))}
        </div>

        {content.sections.map((section) => (
          <Section
            key={section.heading}
            section={section}
            disclosure={disclosure}
          />
        ))}

        <section className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold tracking-tight">
            {content.retention.heading}
          </h2>
          <p className="text-sm leading-relaxed text-muted-foreground">
            {render(content.retention.intro)}
          </p>
          {/* Per-cell dir: RTL locales scramble mixed-direction table content
              when only the table element carries the direction. */}
          <Table dir={dir}>
            <TableHeader>
              <TableRow>
                {content.retention.columns.map((column) => (
                  <TableHead key={column} dir={dir}>
                    {column}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {content.retention.rows.map((row) => (
                <TableRow key={row.category}>
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
            {content.deletion.heading}
          </h2>
          {content.deletion.paragraphs.map((paragraph) => (
            <p
              key={paragraph.slice(0, 40)}
              className="text-sm leading-relaxed text-muted-foreground"
            >
              {render(paragraph)}
            </p>
          ))}
          <ul className="list-disc ps-5 text-sm leading-relaxed text-muted-foreground [&>li+li]:mt-2">
            {content.deletion.bullets.map((bullet) => (
              <li key={bullet.slice(0, 40)}>{render(bullet)}</li>
            ))}
          </ul>
          <div className="flex flex-col items-start gap-2 pt-2">
            <Button
              variant="destructive"
              onClick={() =>
                navigate(
                  status === "authenticated" ? "/profile" : "/delete-account"
                )
              }
            >
              {content.deletion.button}
            </Button>
            <p className="text-xs text-muted-foreground">
              {content.deletion.signedInHint}
            </p>
          </div>
        </section>

        <Separator />

        {content.rights.map((section) => (
          <Section
            key={section.heading}
            section={section}
            disclosure={disclosure}
          />
        ))}

        <Separator />

        {content.closing.map((section) => (
          <Section
            key={section.heading}
            section={section}
            disclosure={disclosure}
          />
        ))}

        {optionalContact.some(([, value]) => value) ? (
          <div className="flex flex-col gap-1 text-sm text-muted-foreground">
            {optionalContact
              .filter(([, value]) => value)
              .map(([label, value]) => (
                <p key={label}>
                  {label}: {value}
                </p>
              ))}
          </div>
        ) : null}

        {status !== "authenticated" ? (
          <div className="text-center text-sm text-muted-foreground">
            <Link
              to="/login"
              className="underline-offset-4 hover:underline"
            >
              {t("auth.backToSignIn")}
            </Link>
          </div>
        ) : null}
      </div>
    </div>
  )
}
