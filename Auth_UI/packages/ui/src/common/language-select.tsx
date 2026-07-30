import { useTranslation } from "react-i18next"

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"

/** Preferred-language picker restricted to the languages the console ships. */
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
  const known = SUPPORTED_LANGUAGES.some((lang) => lang.code === value)

  return (
    <Select value={known && value ? value : undefined} onValueChange={onChange}>
      <SelectTrigger className={className}>
        <SelectValue placeholder={t("common.selectLanguage")} />
      </SelectTrigger>
      <SelectContent>
        <SelectGroup>
          {SUPPORTED_LANGUAGES.map((lang) => (
            <SelectItem key={lang.code} value={lang.code}>
              {lang.label}
            </SelectItem>
          ))}
        </SelectGroup>
      </SelectContent>
    </Select>
  )
}
