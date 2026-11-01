import { useMutation, useQueryClient } from "@tanstack/react-query"
import { TrashIcon, UploadIcon } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"
import { Badge } from "@authsystem/ui/badge"
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
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { Field, FieldDescription, FieldError, FieldGroup, FieldTitle } from "@authsystem/ui/field"
import { Item, ItemContent } from "@authsystem/ui/item"
import { Spinner } from "@authsystem/ui/spinner"
import {
  DEFAULT_FONT_STACK,
  FONT_MAX_SIZE_BYTES,
  FONT_ROLES,
  type FontRole,
} from "@authsystem/ui/theme/platform-fonts"
import { cn } from "@authsystem/ui/utils"

import { useReportUnsaved, useUnsavedParts, type UnsavedState } from "../../lib/unsaved-state"

/**
 * The specimen every preview shows, in both scripts the platform is used in, whatever the
 * console's language: a Latin-only font must show at a glance that Arabic stays in IBM Plex.
 * Sample text, not copy, so it is not translated.
 */
const SPECIMEN = {
  latin: "The quick brown fox jumps over the lazy dog 0123456789",
  arabic: "نصٌّ حكيمٌ له سرٌّ قاطعٌ وذو شأنٍ عظيمٍ مكتوبٌ على ثوبٍ أخضرَ ومغلفٌ بجلدٍ أزرق ٠١٢٣٤٥٦٧٨٩",
} as const

/** The first bytes of every WOFF2 file. */
const WOFF2_SIGNATURE = "wOF2"

type FontDto = Schemas["PlatformFontDto"]

/** A file picked and read in this browser, shown in the preview until it is saved or discarded. */
interface StagedFont {
  file: File
  family: string
  face: FontFace
}

let previewSequence = 0

/**
 * Reads a picked file as the server will: WOFF2, at most 2 MiB, and a font this browser can
 * load. Registered under a preview family of its own, so the preview shows the file before
 * anything is sent. Throws the sentence to show.
 */
async function stage(file: File, role: FontRole, t: (key: string) => string): Promise<StagedFont> {
  if (file.size > FONT_MAX_SIZE_BYTES) throw new Error(t("platformSettings.fonts.tooLarge"))
  const bytes = await file.arrayBuffer()
  if (new TextDecoder().decode(bytes.slice(0, 4)) !== WOFF2_SIGNATURE) {
    throw new Error(t("platformSettings.fonts.notWoff2"))
  }
  const family = `AuthSystem Font Preview ${role} ${++previewSequence}`
  const face = new FontFace(family, bytes, { display: "block" })
  try {
    await face.load()
  } catch {
    throw new Error(t("platformSettings.fonts.unreadable"))
  }
  document.fonts.add(face)
  return { file, family, face }
}

function unstage(staged: StagedFont | null) {
  if (staged) document.fonts.delete(staged.face)
}

/**
 * One role's font: what is in use, a preview in Latin and Arabic, and upload / replace /
 * remove. A picked file is a draft (the page's leave-prompt hears about it) until Save, which
 * applies it at once to both apps and every sign-in page design.
 */
function FontRoleCard({
  role,
  font,
  bodyFont,
  onSaved,
  onUnsavedChange,
}: {
  role: FontRole
  font: FontDto | null | undefined
  /** The body font, which a heading without its own follows. */
  bodyFont: FontDto | null | undefined
  onSaved: (saved: Schemas["PlatformSettingsDto"] | undefined) => void
  onUnsavedChange: (state: UnsavedState) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const inputRef = React.useRef<HTMLInputElement>(null)
  const [staged, setStaged] = React.useState<StagedFont | null>(null)
  const [error, setError] = React.useState<string | null>(null)
  const [reading, setReading] = React.useState(false)
  const [confirmRemove, setConfirmRemove] = React.useState(false)
  const errorId = React.useId()

  // The preview face goes when the card does.
  const stagedRef = React.useRef(staged)
  React.useLayoutEffect(() => {
    stagedRef.current = staged
  })
  React.useEffect(() => () => unstage(stagedRef.current), [])

  const applied = React.useCallback(
    (saved: Schemas["PlatformSettingsDto"] | undefined) => {
      onSaved(saved)
      // The console itself shows the platform's fonts: the branding carries them to every page.
      void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
    },
    [onSaved, queryClient]
  )

  const save = useMutation({
    mutationFn: async (file: File) => {
      const body = new FormData()
      body.append("file", file, file.name)
      const { data, error } = await api.PUT("/api/v1/admin/platform-settings/fonts/{role}", {
        params: { path: { role } },
        body: {},
        bodySerializer: () => body,
      })
      if (error) throw error
      return data
    },
    onSuccess: (saved) => {
      applied(saved)
      unstage(staged)
      setStaged(null)
      toast.success(t("platformSettings.fonts.saved"))
    },
    onError: (failure) => toast.error(getErrorMessage(failure)),
  })

  const remove = useMutation({
    mutationFn: async () => {
      const { data, error } = await api.DELETE("/api/v1/admin/platform-settings/fonts/{role}", {
        params: { path: { role } },
      })
      if (error) throw error
      return data
    },
    onSuccess: (saved) => {
      applied(saved)
      setConfirmRemove(false)
      toast.success(t("platformSettings.fonts.removed"))
    },
    onError: (failure) => toast.error(getErrorMessage(failure)),
  })

  useReportUnsaved(onUnsavedChange, staged !== null, save.isPending)

  const pick = async (file: File) => {
    setError(null)
    setReading(true)
    try {
      const next = await stage(file, role, t)
      unstage(staged)
      setStaged(next)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("platformSettings.fonts.unreadable"))
    } finally {
      setReading(false)
    }
  }

  const busy = reading || save.isPending || remove.isPending
  const name = font ? (font.familyName ?? t("platformSettings.fonts.unnamed")) : null
  const fallback =
    role === "heading" && bodyFont
      ? t("platformSettings.fonts.sameAsBody")
      : t("platformSettings.fonts.shipped")

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t(`platformSettings.fonts.${role}.title`)}</CardTitle>
        <CardDescription>{t(`platformSettings.fonts.${role}.description`)}</CardDescription>
      </CardHeader>
      <CardContent>
        <FieldGroup>
          <Field>
            <FieldTitle>{t("platformSettings.fonts.current")}</FieldTitle>
            <div className="flex min-w-0 flex-wrap items-center gap-2">
              <span className="min-w-0 truncate">{staged ? staged.file.name : (name ?? fallback)}</span>
              {staged ? (
                <Badge variant="secondary">{t("platformSettings.fonts.notSaved")}</Badge>
              ) : font ? null : (
                <Badge variant="secondary">{t("platformSettings.fonts.isDefault")}</Badge>
              )}
            </div>
          </Field>
          <Field>
            <FieldTitle>{t("platformSettings.fonts.preview")}</FieldTitle>
            {/* The saved state through the preset's own utilities (what the pages show,
                fallback included); a picked file through its preview family, before the
                shipped stack, as the pages will use it once saved. */}
            <Item variant="outline" data-slot="font-preview" data-role={role}>
              <ItemContent
                className={cn(!staged && (role === "heading" ? "font-heading" : "font-sans"))}
                style={staged ? { fontFamily: `"${staged.family}", ${DEFAULT_FONT_STACK}` } : undefined}
              >
                <p lang="en" className={cn(role === "heading" ? "text-xl" : "text-base")}>
                  {SPECIMEN.latin}
                </p>
                <p lang="ar" className={cn(role === "heading" ? "text-xl" : "text-base")}>
                  {SPECIMEN.arabic}
                </p>
              </ItemContent>
            </Item>
          </Field>
          <Field data-invalid={error ? true : undefined}>
            <input
              ref={inputRef}
              type="file"
              accept=".woff2,font/woff2"
              className="hidden"
              aria-describedby={error ? errorId : undefined}
              data-testid={`font-file-${role}`}
              onChange={(event) => {
                const file = event.target.files?.[0]
                // The same file may be picked again after a discard.
                event.target.value = ""
                if (file) void pick(file)
              }}
            />
            <FieldDescription>{t("platformSettings.fonts.hint")}</FieldDescription>
            {error ? <FieldError id={errorId}>{error}</FieldError> : null}
          </Field>
        </FieldGroup>
      </CardContent>
      <CardFooter className="flex-wrap gap-2">
        {staged ? (
          <>
            <Button disabled={busy} onClick={() => save.mutate(staged.file)}>
              {save.isPending ? <Spinner data-icon="inline-start" /> : null}
              {t("common.save")}
            </Button>
            <Button
              variant="outline"
              disabled={busy}
              onClick={() => {
                unstage(staged)
                setStaged(null)
              }}
            >
              {t("platformSettings.fonts.discard")}
            </Button>
          </>
        ) : (
          <>
            <Button variant={font ? "outline" : "default"} disabled={busy} onClick={() => inputRef.current?.click()}>
              {reading ? <Spinner data-icon="inline-start" /> : <UploadIcon data-icon="inline-start" />}
              {t(font ? "platformSettings.fonts.replace" : "platformSettings.fonts.upload")}
            </Button>
            {font ? (
              <Button variant="ghost" disabled={busy} onClick={() => setConfirmRemove(true)}>
                <TrashIcon data-icon="inline-start" />
                {t("platformSettings.fonts.remove")}
              </Button>
            ) : null}
          </>
        )}
      </CardFooter>
      <ConfirmDialog
        open={confirmRemove}
        onOpenChange={setConfirmRemove}
        title={t("platformSettings.fonts.removeTitle", { name: name ?? "" })}
        description={t("platformSettings.fonts.removeDescription", { fallback })}
        confirmLabel={t("platformSettings.fonts.remove")}
        destructive
        loading={remove.isPending}
        onConfirm={() => remove.mutate()}
      />
    </Card>
  )
}

/**
 * The Fonts tab of the platform settings: the heading font and the body font (owner decision
 * D7: WOFF2 only, at most 2 MB each, one file per role), used by the console, the accounts app
 * and every sign-in page design, never by emails.
 */
export function FontsTab({
  settings,
  onSaved,
  onUnsavedChange,
}: {
  settings: Schemas["PlatformSettingsDto"]
  onSaved: (saved: Schemas["PlatformSettingsDto"] | undefined) => void
  onUnsavedChange: (state: UnsavedState) => void
}) {
  const { t } = useTranslation()
  const { reporters, combined } = useUnsavedParts(FONT_ROLES)
  useReportUnsaved(onUnsavedChange, combined.isDirty, combined.isSaving)

  return (
    <div className="flex flex-col gap-6">
      <p className="text-sm text-muted-foreground">{t("platformSettings.fonts.subtitle")}</p>
      {FONT_ROLES.map((role) => (
        <FontRoleCard
          key={role}
          role={role}
          font={settings.fonts?.[role]}
          bodyFont={settings.fonts?.body}
          onSaved={onSaved}
          onUnsavedChange={reporters[role]}
        />
      ))}
    </div>
  )
}
