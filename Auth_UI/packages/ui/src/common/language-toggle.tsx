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
import { usePreferenceSync } from "@astoom/ui/common/use-preference-sync"
import { SUPPORTED_LANGUAGES, type LanguageCode } from "@astoom/i18n"
import { useLanguage } from "@astoom/i18n/direction"

export function LanguageToggle() {
  const { t } = useTranslation()
  const { language, setLanguage } = useLanguage()
  const syncPreference = usePreferenceSync()

  const changeLanguage = (code: LanguageCode) => {
    setLanguage(code)
    // Keep the profile's preferred language in sync so the next login — and
    // localized emails — follow the user's choice.
    syncPreference({ preferredLanguage: code })
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
