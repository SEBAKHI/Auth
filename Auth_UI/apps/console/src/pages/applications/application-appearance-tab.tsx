import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { useBranding, useThemePreview } from "@authsystem/ui/branding"
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
  FieldSeparator,
  FieldSet,
} from "@authsystem/ui/field"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Spinner } from "@authsystem/ui/spinner"
import { Switch } from "@authsystem/ui/switch"
import {
  themeConfigKey,
  toThemeConfig,
  toThemeRequest,
  type ThemeConfig,
} from "@authsystem/ui/theme/theme-config"

import { AppearanceEditor } from "../../components/appearance/appearance-editor"
import { isAppearanceSavable } from "../../components/appearance/appearance-rules"
import { LogoDisplaySizeCard } from "../../components/logo-display-size/logo-display-size-card"
import { useReportUnsaved, useUnsavedParts, type UnsavedState } from "../../lib/unsaved-state"

type ApplicationDto = Schemas["ApplicationDto"]
type Appearance = { enabled: boolean; theme: ThemeConfig }

/** The preview's stylesheet scope: the console itself never takes the application's colours. */
const PREVIEW_SCOPE = "application-appearance"

/** The tab's two parts, each with edits of its own; the tab holds the one leave-prompt. */
const APPEARANCE_PARTS = ["logoSize", "appearance"] as const

/**
 * An application's own appearance: a switch, and the same editor as the
 * platform's. While it is on, the accounts pages the application sends people
 * to show it in place of the platform's; off, they show the platform's, and
 * the saved appearance is kept.
 *
 * The editor starts from the application's saved appearance or, when there is
 * none, from the platform's: "inherits the platform's" is where the
 * administrator begins. Only the preview tiles are painted with it.
 *
 * Seeded once, at mount: the page mounts it per application and keeps it
 * mounted across tab switches, so neither a refetch nor a tab overwrites what
 * is being edited. It waits for the platform's appearance, which it may start
 * from, to be known.
 */
export default function ApplicationAppearanceTab(props: {
  application: ApplicationDto
  canUpdate: boolean
}) {
  const { isPending } = useBranding()
  const { reporters, combined } = useUnsavedParts(APPEARANCE_PARTS)
  const unsavedPrompt = useUnsavedChangesPrompt(combined)
  return (
    <div className="flex flex-col gap-6">
      <ApplicationLogoSizeCard {...props} onUnsavedChange={reporters.logoSize} />
      {isPending ? (
        <Skeleton className="h-96 w-full" />
      ) : (
        <ApplicationAppearanceCard {...props} onUnsavedChange={reporters.appearance} />
      )}
      {unsavedPrompt}
    </div>
  )
}

/**
 * The height the application's logo is shown at above the form on its sign-in pages (owner
 * decision D6): on this tab, outside the logo's upload dialog, saved on its own.
 */
function ApplicationLogoSizeCard({
  application,
  canUpdate,
  onUnsavedChange,
}: {
  application: ApplicationDto
  canUpdate: boolean
  onUnsavedChange: (state: UnsavedState) => void
}) {
  const queryClient = useQueryClient()
  const appId = application.id as string

  const save = React.useCallback(
    async (logoHeightPx: number | null) => {
      const { data, error } = await api.PUT("/api/v1/Applications/{id}/logo-height", {
        params: { path: { id: appId } },
        body: { logoHeightPx },
      })
      if (error) throw error
      // The page's read of the application, at once, as the appearance's save does.
      queryClient.setQueryData<ApplicationDto>(["applications", appId], (old) =>
        old ? { ...old, logoHeightPx: data?.logoHeightPx ?? null } : old
      )
      void queryClient.invalidateQueries({ queryKey: ["applications"] })
      return data?.logoHeightPx
    },
    [appId, queryClient]
  )

  return (
    <LogoDisplaySizeCard
      owner="application"
      storedPx={application.logoHeightPx}
      name={application.name ?? ""}
      lightSrc={application.logoUrl}
      darkSrc={application.logoUrlDark}
      canEdit={canUpdate}
      save={save}
      onUnsavedChange={onUnsavedChange}
    />
  )
}

function ApplicationAppearanceCard({
  application,
  canUpdate,
  onUnsavedChange,
}: {
  application: ApplicationDto
  canUpdate: boolean
  onUnsavedChange: (state: UnsavedState) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { savedTheme: platformTheme } = useThemePreview()
  const appId = application.id as string

  const [saved, setSaved] = React.useState(() => ({
    enabled: application.themeEnabled ?? false,
    theme: application.theme ? toThemeConfig(application.theme) : null,
  }))
  // What the editor shows before any edit: the saved one, or the platform's.
  const [start, setStart] = React.useState(() => saved.theme ?? platformTheme)
  const [draft, setDraft] = React.useState<Appearance>(() => ({ enabled: saved.enabled, theme: start }))

  const themeEdited = themeConfigKey(draft.theme) !== themeConfigKey(start)
  const isDirty = draft.enabled !== saved.enabled || themeEdited

  const save = useMutation({
    mutationFn: async ({ next, sendTheme }: { next: Appearance; sendTheme: boolean }) => {
      const { data, error } = await api.PUT("/api/v1/Applications/{id}/appearance", {
        params: { path: { id: appId } },
        // Off with nothing edited sends no theme: the saved one stays, and an
        // untouched copy of the platform's is never stored as the application's.
        body: { enabled: next.enabled, theme: sendTheme ? toThemeRequest(next.theme) : null },
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      const stored = {
        enabled: data?.enabled ?? false,
        theme: data?.theme ? toThemeConfig(data.theme) : null,
      }
      // The page's read of the application, at once: every later save on the
      // page builds on what is stored, not on what it rendered before.
      queryClient.setQueryData<ApplicationDto>(["applications", appId], (old) =>
        old ? { ...old, themeEnabled: stored.enabled, theme: data?.theme ?? null } : old
      )
      void queryClient.invalidateQueries({ queryKey: ["applications"] })
      setSaved(stored)
      if (stored.theme) setStart(stored.theme)
      setDraft((current) => ({ enabled: stored.enabled, theme: stored.theme ?? current.theme }))
      toast.success(t("applications.appearance.saved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  useReportUnsaved(onUnsavedChange, isDirty, save.isPending)
  const isPlatforms = themeConfigKey(draft.theme) === themeConfigKey(platformTheme)
  const switchId = `application-appearance-enabled-${appId}`

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("applications.appearance.title")}</CardTitle>
        <CardDescription>{t("applications.appearance.subtitle")}</CardDescription>
      </CardHeader>
      <CardContent>
        <FieldGroup>
          <Field orientation="horizontal">
            <FieldContent>
              <FieldLabel htmlFor={switchId}>{t("applications.appearance.enable")}</FieldLabel>
              <FieldDescription>
                {draft.enabled
                  ? t("applications.appearance.enabledHint")
                  : t("applications.appearance.disabledHint")}
              </FieldDescription>
            </FieldContent>
            <Switch
              id={switchId}
              checked={draft.enabled}
              disabled={!canUpdate || save.isPending}
              onCheckedChange={(enabled) => setDraft((current) => ({ ...current, enabled }))}
            />
          </Field>
          <FieldSeparator />
          {/* Off, the editor shows what would apply and takes no edit. */}
          <FieldSet disabled={!draft.enabled || !canUpdate}>
            <AppearanceEditor
              value={draft.theme}
              onChange={(update) => setDraft((current) => ({ ...current, theme: update(current.theme) }))}
              previewName={application.name ?? ""}
              previewScope={PREVIEW_SCOPE}
            />
          </FieldSet>
        </FieldGroup>
      </CardContent>
      {canUpdate ? (
        <CardFooter className="flex-wrap gap-2">
          <Button
            disabled={!isDirty || !isAppearanceSavable(draft.theme) || save.isPending}
            onClick={() => save.mutate({ next: draft, sendTheme: draft.enabled || themeEdited })}
          >
            {save.isPending ? <Spinner data-icon="inline-start" /> : null}
            {t("common.save")}
          </Button>
          <Button
            variant="outline"
            disabled={!isDirty || save.isPending}
            onClick={() => setDraft({ enabled: saved.enabled, theme: start })}
          >
            {t("platformSettings.appearance.revert")}
          </Button>
          <Button
            variant="ghost"
            disabled={!draft.enabled || isPlatforms || save.isPending}
            onClick={() => setDraft((current) => ({ ...current, theme: platformTheme }))}
          >
            {t("applications.appearance.copyPlatform")}
          </Button>
        </CardFooter>
      ) : null}
    </Card>
  )
}
