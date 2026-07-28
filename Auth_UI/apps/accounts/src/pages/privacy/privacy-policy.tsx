import { ShieldCheck, TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"

import { useAuth } from "@astoom/auth/auth-context"
import { directionForLanguage, type LanguageCode } from "@astoom/i18n"
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

import { PRIVACY_CONTENT } from "./content"
import { CONTROLLER, hasUnfilledDetails } from "./content/details"
import { POLICY_VERSION, type PolicySection } from "./content/types"

function Section({ section }: { section: PolicySection }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold tracking-tight">
        {section.heading}
      </h2>
      {section.paragraphs.map((paragraph) => (
        <p
          key={paragraph.slice(0, 40)}
          className="text-sm leading-relaxed text-muted-foreground"
        >
          {paragraph}
        </p>
      ))}
      {section.bullets ? (
        <ul className="list-disc ps-5 text-sm leading-relaxed text-muted-foreground [&>li+li]:mt-2">
          {section.bullets.map((bullet) => (
            <li key={bullet.slice(0, 40)}>{bullet}</li>
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
 * Content is a typed document per language (see ./content); the controller
 * facts are interpolated from ./content/details, and the page shows a
 * draft warning until every required placeholder there is filled.
 */
export function PrivacyPolicyPage() {
  const { i18n, t } = useTranslation()
  const { status } = useAuth()
  const navigate = useNavigate()

  const content =
    PRIVACY_CONTENT[i18n.language as LanguageCode] ?? PRIVACY_CONTENT.en
  const dir = directionForLanguage(i18n.language)

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
              {content.versionLabel} {POLICY_VERSION}
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
              {paragraph}
            </p>
          ))}
        </div>

        {content.sections.map((section) => (
          <Section key={section.heading} section={section} />
        ))}

        <section className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold tracking-tight">
            {content.retention.heading}
          </h2>
          <p className="text-sm leading-relaxed text-muted-foreground">
            {content.retention.intro}
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
                    {row.category}
                  </TableCell>
                  <TableCell dir={dir} className="whitespace-normal align-top">
                    {row.retention}
                  </TableCell>
                  <TableCell
                    dir={dir}
                    className="whitespace-normal align-top text-muted-foreground"
                  >
                    {row.detail}
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
              {paragraph}
            </p>
          ))}
          <ul className="list-disc ps-5 text-sm leading-relaxed text-muted-foreground [&>li+li]:mt-2">
            {content.deletion.bullets.map((bullet) => (
              <li key={bullet.slice(0, 40)}>{bullet}</li>
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
          <Section key={section.heading} section={section} />
        ))}

        <Separator />

        {content.closing.map((section) => (
          <Section key={section.heading} section={section} />
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
