import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { SUPPORTED_LANGUAGES } from "@authsystem/i18n"
import { normalizeHiddenLanguages } from "@authsystem/i18n/hidden-languages"
import { DEFAULT_LANGUAGE } from "@authsystem/i18n/languages"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from "@authsystem/ui/field"
import { Spinner } from "@authsystem/ui/spinner"
import { Switch } from "@authsystem/ui/switch"

import { useReportUnsaved, type UnsavedState } from "../../lib/unsaved-state"
import { PLATFORM_SETTINGS_QUERY_KEY } from "./platform-settings-tabs"

/**
 * The Languages tab of the platform settings (item 11, owner decision D8): one switch per
 * language, on while the language is shown in the language menus of both apps. English stays
 * on, with the reason beside it. The switches are a draft, saved by one Save; the branding
 * then carries the list to every page, this one included.
 */
export function LanguagesTab({
  settings,
  onUnsavedChange,
}: {
  settings: Schemas["PlatformSettingsDto"]
  onUnsavedChange: (state: UnsavedState) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const saved = React.useMemo(() => normalizeHiddenLanguages(settings.hiddenLanguages ?? []), [settings.hiddenLanguages])
  // Null while untouched: the switches follow the stored list, a refetch included.
  const [edited, setEdited] = React.useState<string[] | null>(null)
  const draft = edited ?? saved
  const isDirty = edited !== null && edited.join(",") !== saved.join(",")

  const save = useMutation({
    mutationFn: async (hiddenLanguages: string[]) => {
      const { data, error } = await api.PUT("/api/v1/admin/platform-settings/hidden-languages", {
        body: { hiddenLanguages },
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      const stored = normalizeHiddenLanguages(data?.hiddenLanguages ?? [])
      // The list alone into the page's copy, as the logo's height is: the identity tab's saves
      // queue on that copy, and a whole answer landing out of turn could hand them older values.
      queryClient.setQueryData<Schemas["PlatformSettingsDto"]>(PLATFORM_SETTINGS_QUERY_KEY, (old) =>
        old ? { ...old, hiddenLanguages: stored } : old
      )
      setEdited(null)
      // The menus of this page follow the branding, like every other page's.
      void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
      toast.success(t("platformSettings.languages.saved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  useReportUnsaved(onUnsavedChange, isDirty, save.isPending)

  const setShown = (code: string, shown: boolean) =>
    setEdited(normalizeHiddenLanguages(shown ? draft.filter((c) => c !== code) : [...draft, code]))

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("platformSettings.languages.title")}</CardTitle>
        <CardDescription>{t("platformSettings.languages.subtitle")}</CardDescription>
      </CardHeader>
      <CardContent>
        <FieldSet>
          <FieldLegend variant="label">{t("platformSettings.languages.legend")}</FieldLegend>
          <FieldGroup>
            {SUPPORTED_LANGUAGES.map((lang) => {
              const id = `platform-language-${lang.code}`
              // English cannot be hidden (the server refuses it): its switch stays on, and its
              // reason is read with it.
              const locked = lang.code === DEFAULT_LANGUAGE
              const reasonId = `${id}-reason`
              return (
                <Field key={lang.code} orientation="horizontal" data-disabled={locked || undefined}>
                  <FieldContent>
                    <FieldLabel htmlFor={id}>{lang.label}</FieldLabel>
                    {locked ? (
                      <FieldDescription id={reasonId}>
                        {t("platformSettings.languages.englishAlwaysShown")}
                      </FieldDescription>
                    ) : null}
                  </FieldContent>
                  <Switch
                    id={id}
                    checked={!draft.includes(lang.code)}
                    disabled={locked || save.isPending}
                    aria-describedby={locked ? reasonId : undefined}
                    onCheckedChange={(checked) => setShown(lang.code, checked)}
                  />
                </Field>
              )
            })}
          </FieldGroup>
        </FieldSet>
      </CardContent>
      <CardFooter className="flex-wrap gap-2">
        <Button disabled={!isDirty || save.isPending} onClick={() => save.mutate(draft)}>
          {save.isPending ? <Spinner data-icon="inline-start" /> : null}
          {t("common.save")}
        </Button>
        <Button variant="outline" disabled={!isDirty || save.isPending} onClick={() => setEdited(null)}>
          {t("platformSettings.languages.revert")}
        </Button>
      </CardFooter>
    </Card>
  )
}
