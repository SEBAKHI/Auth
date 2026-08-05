import { TriangleAlert } from "lucide-react"
import { useTranslation } from "react-i18next"

import { SUPPORTED_LANGUAGES } from "@authsystem/i18n"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"

/**
 * Names the languages a revision has not been translated into, shown inside the
 * publish and notify confirmations.
 *
 * Publishing is still allowed — an untranslated language is served the English
 * document carrying a notice, in the reader's own language, that says so. But
 * that is a decision about a legal disclosure, so the person making it is told
 * which readers it affects before they make it, rather than discovering it from
 * the published page.
 */
export function PolicyLanguageGapNotice({
  languages,
}: {
  /** Language codes this revision has documents for. */
  languages: string[] | undefined
}) {
  const { t, i18n } = useTranslation()

  const written = new Set(languages ?? [])
  const missing = SUPPORTED_LANGUAGES.filter((language) => !written.has(language.code))

  if (missing.length === 0) return null

  // Intl rather than a joined string: the separator and the final conjunction
  // differ per locale, and this list is read in all seven of them.
  const names = new Intl.ListFormat(i18n.language, {
    style: "long",
    type: "conjunction",
  }).format(missing.map((language) => language.label))

  return (
    <Alert>
      <TriangleAlert />
      <AlertTitle>{t("notifications.policyMissingLanguagesTitle")}</AlertTitle>
      <AlertDescription>
        {t("notifications.policyMissingLanguagesBody", { languages: names })}
      </AlertDescription>
    </Alert>
  )
}
