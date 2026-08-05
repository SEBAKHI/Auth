import { ShieldCheck } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useNavigate } from "react-router-dom"

import { useAuth } from "@authsystem/auth/auth-context"
import { directionForLanguage } from "@authsystem/i18n"
import { Badge } from "@authsystem/ui/badge"
import { BrandingLogo } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import { LanguageToggle } from "@authsystem/ui/common/language-toggle"
import { PolicyDocument } from "@authsystem/ui/common/policy-document"
import { ThemeToggle } from "@authsystem/ui/common/theme-toggle"

import { usePrivacyPolicy } from "./use-privacy-policy"

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
 * text can never contradict the system it describes. The body is rendered by
 * the shared PolicyDocument, which the console preview also uses.
 */
export function PrivacyPolicyPage() {
  const { i18n, t } = useTranslation()
  const { status } = useAuth()
  const navigate = useNavigate()

  const { policy } = usePrivacyPolicy()
  const { content, disclosure } = policy
  const dir = directionForLanguage(i18n.language)

  // The draft banner and the optional-contact lines moved into PolicyDocument
  // so the console's editor preview renders them too. They were built here from
  // a compiled-in constant, which meant they described the bundle rather than
  // the document actually served from the database.

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

        <PolicyDocument
          content={content}
          disclosure={disclosure}
          dir={dir}
          deletionAction={
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
          }
        />

        {status !== "authenticated" ? (
          <div className="text-center text-sm text-muted-foreground">
            <Link to="/login" className="underline-offset-4 hover:underline">
              {t("auth.backToSignIn")}
            </Link>
          </div>
        ) : null}
      </div>
    </div>
  )
}
