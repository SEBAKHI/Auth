import { useTranslation } from "react-i18next"

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { SUPPORTED_LANGUAGES } from "@/lib/i18n"

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
        {SUPPORTED_LANGUAGES.map((lang) => (
          <SelectItem key={lang.code} value={lang.code}>
            {lang.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  )
}
