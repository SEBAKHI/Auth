import { Languages } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button, type ButtonSize } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { usePreferenceSync } from "@authsystem/ui/common/use-preference-sync"
import type { LanguageCode } from "@authsystem/i18n"
import { useLanguage } from "@authsystem/i18n/direction"
import { useVisibleLanguages } from "@authsystem/i18n/hidden-languages"

/**
 * The language menu: the languages an administrator has not hidden (platform settings › Languages).
 * With every other language hidden there is nothing to choose, so there is no menu either: a
 * button whose one item is the language already in use would offer nothing.
 */
export function LanguageToggle({
  size = "icon",
}: {
  /** "icon-header" in the app header, which follows the header icon size. */
  size?: ButtonSize
} = {}) {
  const { t } = useTranslation()
  const { language, setLanguage } = useLanguage()
  const languages = useVisibleLanguages()
  const syncPreference = usePreferenceSync()

  const changeLanguage = (code: LanguageCode) => {
    setLanguage(code)
    // Keep the profile's preferred language in sync so the next login — and
    // localized emails — follow the user's choice.
    syncPreference({ preferredLanguage: code })
  }

  if (languages.length < 2) return null

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size={size} aria-label={t("common.language")}>
          <Languages />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t("common.language")}</DropdownMenuLabel>
        <DropdownMenuGroup>
          {languages.map((lang) => (
            <DropdownMenuItem
              key={lang.code}
              onClick={() => changeLanguage(lang.code)}
              disabled={language === lang.code}
            >
              {lang.label}
            </DropdownMenuItem>
          ))}
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
