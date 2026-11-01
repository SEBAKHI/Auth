import { useMutation } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { getErrorMessage } from "@authsystem/api/errors"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { Field, FieldDescription, FieldGroup, FieldTitle } from "@authsystem/ui/field"
import {
  LOGO_HEIGHT,
  LOGO_MAX_ASPECT,
  LogoMark,
  LogoName,
  PlatformMark,
  resolveLogoHeight,
} from "@authsystem/ui/logo-mark"
import { Slider } from "@authsystem/ui/slider"
import { Spinner } from "@authsystem/ui/spinner"
import { pickLogo } from "@authsystem/ui/theme/pick-logo"
import { cn } from "@authsystem/ui/utils"

import { useReportUnsaved, type UnsavedState } from "../../lib/unsaved-state"

/** A stored height as the contract carries it: an integer, or null for "none chosen". */
type StoredHeight = number | string | null | undefined

/** null: none chosen, so the default applies. */
function toChosen(stored: StoredHeight): number | null {
  const px = resolveLogoHeight(stored, Number.NaN)
  return Number.isNaN(px) ? null : px
}

const OWNERS = {
  platform: { defaultPx: LOGO_HEIGHT.platformDefault, descriptionKey: "logoDisplaySize.platformDescription" },
  application: { defaultPx: LOGO_HEIGHT.applicationDefault, descriptionKey: "logoDisplaySize.applicationDescription" },
} as const

/**
 * The height a logo is shown at on the sign-in pages (owner decisions D5/D6): the area the logo
 * takes on the page, not the image, so it is set here, beside the logo and outside the upload
 * dialog, and saved on its own without choosing a file. The preview draws the logo with the
 * sign-in pages' own primitives, at real size, once in each mode.
 *
 * Seeded once, at mount: the page keys it per owner, so a refetch never overwrites a height being
 * dragged. A drag is a draft until Save; the page's one leave-prompt hears about it.
 */
export function LogoDisplaySizeCard({
  owner,
  storedPx,
  name,
  lightSrc,
  darkSrc,
  canEdit,
  save,
  onUnsavedChange,
}: {
  owner: keyof typeof OWNERS
  storedPx: StoredHeight
  name: string
  lightSrc?: string | null
  darkSrc?: string | null
  canEdit: boolean
  /** Saves the height (null: back to the default) and answers with the one stored. */
  save: (px: number | null) => Promise<StoredHeight>
  onUnsavedChange?: (state: UnsavedState) => void
}) {
  const { t } = useTranslation()
  const { defaultPx, descriptionKey } = OWNERS[owner]
  const [saved, setSaved] = React.useState(() => toChosen(storedPx))
  const [draft, setDraft] = React.useState(saved)
  const px = draft ?? defaultPx
  const isDirty = draft !== saved
  const labelId = React.useId()

  const mutation = useMutation({
    mutationFn: (next: number | null) => save(next),
    onSuccess: (stored) => {
      const chosen = toChosen(stored)
      setSaved(chosen)
      setDraft(chosen)
      toast.success(t("logoDisplaySize.saved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  useReportUnsaved(onUnsavedChange, isDirty, mutation.isPending)

  const hasLogo = Boolean(lightSrc || darkSrc)
  const modes = [
    { mode: "light", label: t("platformSettings.logoLight") },
    { mode: "dark", label: t("platformSettings.logoDark") },
  ] as const

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("logoDisplaySize.title")}</CardTitle>
        <CardDescription>{t(descriptionKey)}</CardDescription>
      </CardHeader>
      <CardContent>
        <FieldGroup>
          <Field data-disabled={!canEdit || undefined}>
            <div className="flex items-center justify-between gap-2">
              <FieldTitle id={labelId}>{t("logoDisplaySize.height")}</FieldTitle>
              <span className="flex items-center gap-2 text-sm text-muted-foreground tabular-nums">
                {draft === null ? <Badge variant="secondary">{t("logoDisplaySize.isDefault")}</Badge> : null}
                {t("logoDisplaySize.heightValue", { px })}
              </span>
            </div>
            <Slider
              aria-labelledby={labelId}
              min={LOGO_HEIGHT.min}
              max={LOGO_HEIGHT.max}
              step={1}
              value={[px]}
              disabled={!canEdit || mutation.isPending}
              onValueChange={([value]) => setDraft(value)}
            />
          </Field>
          <Field>
            {/* Each mode is painted in its own colours: a light wordmark meant for dark pages
                is invisible on a light tile, and the other way round. The drawing area is the
                tallest height, so the tiles never jump while the height is dragged. */}
            <div className="grid gap-3 sm:grid-cols-2">
              {modes.map(({ mode, label }) => {
                const src = pickLogo(lightSrc ?? null, darkSrc ?? null, mode)
                return (
                  <div
                    key={mode}
                    data-mode={mode}
                    data-slot="logo-display-size-preview"
                    className={cn(
                      mode,
                      "flex min-w-0 flex-col items-center gap-3 rounded-2xl bg-background p-4 text-foreground ring-1 ring-foreground/10"
                    )}
                  >
                    <div className="flex h-30 w-full min-w-0 items-center justify-center">
                      {owner === "platform" ? (
                        <PlatformMark src={src} name={name} heightPx={px} />
                      ) : src ? (
                        <LogoMark src={src} alt={name} heightPx={px} maxAspect={LOGO_MAX_ASPECT.application} />
                      ) : (
                        <LogoName name={name} />
                      )}
                    </div>
                    <p className="text-sm text-muted-foreground">{label}</p>
                  </div>
                )
              })}
            </div>
            <FieldDescription>
              {t("logoDisplaySize.previewHint")}
              {owner === "application" && !hasLogo ? ` ${t("logoDisplaySize.noLogo")}` : null}
            </FieldDescription>
          </Field>
        </FieldGroup>
      </CardContent>
      {canEdit ? (
        <CardFooter className="flex-wrap gap-2">
          <Button disabled={!isDirty || mutation.isPending} onClick={() => mutation.mutate(draft)}>
            {mutation.isPending ? <Spinner data-icon="inline-start" /> : null}
            {t("common.save")}
          </Button>
          <Button
            variant="outline"
            disabled={!isDirty || mutation.isPending}
            onClick={() => setDraft(saved)}
          >
            {t("platformSettings.appearance.revert")}
          </Button>
          <Button
            variant="ghost"
            disabled={draft === null || mutation.isPending}
            onClick={() => setDraft(null)}
          >
            {t("logoDisplaySize.reset", { px: defaultPx })}
          </Button>
        </CardFooter>
      ) : null}
    </Card>
  )
}
