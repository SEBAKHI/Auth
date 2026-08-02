import { Monitor, Moon, Sun } from "lucide-react"
import { useTranslation } from "react-i18next"

import { useTheme, type Theme } from "@authsystem/ui/theme-provider"
import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { usePreferenceSync } from "@authsystem/ui/common/use-preference-sync"

export function ThemeToggle() {
  const { t } = useTranslation()
  const { setTheme } = useTheme()
  const syncPreference = usePreferenceSync()

  const changeTheme = (theme: Theme) => {
    setTheme(theme)
    // Keep the profile's theme in sync so the other apps adopt it on their
    // next session.
    syncPreference({ theme })
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={t("common.theme")}>
          <Sun className="dark:hidden" />
          <Moon className="hidden dark:block" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel>{t("common.theme")}</DropdownMenuLabel>
        <DropdownMenuGroup>
          <DropdownMenuItem onClick={() => changeTheme("light")}>
            <Sun />
            {t("common.light")}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => changeTheme("dark")}>
            <Moon />
            {t("common.dark")}
          </DropdownMenuItem>
          <DropdownMenuItem onClick={() => changeTheme("system")}>
            <Monitor />
            {t("common.system")}
          </DropdownMenuItem>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
