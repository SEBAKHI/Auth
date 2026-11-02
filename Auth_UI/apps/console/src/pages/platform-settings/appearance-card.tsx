import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { BRANDING_QUERY_KEY, useThemePreview } from "@authsystem/ui/branding"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { Spinner } from "@authsystem/ui/spinner"
import {
  DEFAULT_THEME_CONFIG,
  themeConfigKey,
  toThemeConfig,
  toThemeRequest,
  type ThemeConfig,
} from "@authsystem/ui/theme/theme-config"

import { AppearanceEditor } from "../../components/appearance/appearance-editor"
import { isAppearanceSavable } from "../../components/appearance/appearance-rules"
import { useReportUnsaved, type UnsavedState } from "../../lib/unsaved-state"

export function AppearanceCard({
  settings,
  onSaved,
  onUnsavedChange,
}: {
  settings: Schemas["PlatformSettingsDto"]
  onSaved: (saved: Schemas["PlatformSettingsDto"] | undefined) => void
  /** Tells the page whether there is an unsaved draft, for its leave-prompt. */
  onUnsavedChange?: (state: UnsavedState) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { setThemePreview } = useThemePreview()

  // Seeded once, at mount: a refetch must not overwrite what is being edited.
  const [saved, setSaved] = React.useState(() => toThemeConfig(settings.theme))
  const [draft, setDraft] = React.useState(saved)
  const isDirty = themeConfigKey(draft) !== themeConfigKey(saved)

  // The whole console previews the draft for this administrator only.
  React.useEffect(() => {
    setThemePreview(isDirty ? draft : null)
  }, [draft, isDirty, setThemePreview])
  React.useEffect(() => () => setThemePreview(null), [setThemePreview])

  const save = useMutation({
    mutationFn: async (config: ThemeConfig) => {
      const { data, error } = await api.PUT("/api/v1/admin/platform-settings/theme", {
        body: toThemeRequest(config),
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      onSaved(data)
      const next = toThemeConfig(data?.theme)
      // The branding read carries the appearance every visitor gets. Writing
      // the saved one into it before the preview is dropped keeps the old
      // colours from flashing back while the refetch runs.
      queryClient.setQueryData<Schemas["PlatformBrandingDto"]>(
        BRANDING_QUERY_KEY,
        (old) => (old ? { ...old, theme: data?.theme ?? old.theme } : old)
      )
      void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
      setSaved(next)
      setDraft(next)
      toast.success(t("platformSettings.updated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  // The page holds the one leave-prompt for all its tabs (a router honours a single blocker).
  useReportUnsaved(onUnsavedChange, isDirty, save.isPending)

  const isDefault = themeConfigKey(draft) === themeConfigKey(DEFAULT_THEME_CONFIG)

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("platformSettings.appearance.title")}</CardTitle>
        <CardDescription>{t("platformSettings.appearance.subtitle")}</CardDescription>
      </CardHeader>
      <CardContent>
        <AppearanceEditor
          value={draft}
          onChange={setDraft}
          previewName={settings.platformName ?? ""}
          showHeaderIconSize
        />
      </CardContent>
      <CardFooter className="flex-wrap gap-2">
        <Button
          disabled={!isDirty || !isAppearanceSavable(draft) || save.isPending}
          onClick={() => save.mutate(draft)}
        >
          {save.isPending ? <Spinner data-icon="inline-start" /> : null}
          {t("common.save")}
        </Button>
        <Button
          variant="outline"
          disabled={!isDirty || save.isPending}
          onClick={() => setDraft(saved)}
        >
          {t("platformSettings.appearance.revert")}
        </Button>
        <Button
          variant="ghost"
          disabled={isDefault || save.isPending}
          onClick={() => setDraft(DEFAULT_THEME_CONFIG)}
        >
          {t("platformSettings.appearance.resetToDefault")}
        </Button>
      </CardFooter>
    </Card>
  )
}
