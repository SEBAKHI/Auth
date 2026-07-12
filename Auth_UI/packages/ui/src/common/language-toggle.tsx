import { useQueryClient } from "@tanstack/react-query"
import { Languages } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@astoom/ui/dropdown-menu"
import { api } from "@astoom/api/client"
import { useAuth } from "@astoom/auth/auth-context"
import { SUPPORTED_LANGUAGES, type LanguageCode } from "@astoom/i18n"
import { useLanguage } from "@astoom/i18n/direction"

export function LanguageToggle() {
  const { t } = useTranslation()
  const { language, setLanguage } = useLanguage()
  const { status } = useAuth()
  const queryClient = useQueryClient()

  const changeLanguage = (code: LanguageCode) => {
    setLanguage(code)
    // Keep the profile's preferred language in sync (fire-and-forget) so the
    // next login — and localized emails — follow the user's choice.
    if (status === "authenticated") {
      void api
        .PUT("/api/v1/Users/me", { body: { preferredLanguage: code } })
        .then(({ error }) => {
          if (!error) {
            void queryClient.invalidateQueries({ queryKey: ["me"] })
          }
        })
        .catch(() => {
          /* language stays local-only if the sync fails */
        })
    }
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("common.language")}>
          <Languages />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t("common.language")}</DropdownMenuLabel>
        {SUPPORTED_LANGUAGES.map((lang) => (
          <DropdownMenuItem
            key={lang.code}
            onClick={() => changeLanguage(lang.code)}
            disabled={language === lang.code}
          >
            {lang.label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
