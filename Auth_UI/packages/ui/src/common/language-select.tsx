import { useTranslation } from "react-i18next"

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { useVisibleLanguages } from "@authsystem/i18n/hidden-languages"

/**
 * Preferred-language picker restricted to the languages the console ships and an
 * administrator has not hidden (platform settings › Languages). A saved language that is
 * hidden now shows the placeholder; it stays saved until another one is picked.
 */
export function LanguageSelect({
  value,
  onChange,
  className,
}: {
  value: string | null | undefined
  onChange: (value: string) => void
  className?: string
}) {
  const { t } = useTranslation()
  const languages = useVisibleLanguages()
  const known = languages.some((lang) => lang.code === value)

  return (
    <Select value={known && value ? value : undefined} onValueChange={onChange}>
      <SelectTrigger className={className}>
        <SelectValue placeholder={t("common.selectLanguage")} />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          {languages.map((lang) => (
            <SelectItem key={lang.code} value={lang.code}>
              {lang.label}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  )
}
