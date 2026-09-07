import * as React from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { TriangleAlert } from "lucide-react"
import { useForm, useWatch, type Control, type FieldValues } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { Link, useSearchParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { RequirePermission } from "@authsystem/auth/require-permission"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
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
import {
  FieldDescription,
  FieldGroup,
  FieldLegend,
  FieldSet,
} from "@authsystem/ui/field"
import { Form } from "@authsystem/ui/form"
import { Spinner } from "@authsystem/ui/spinner"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"

import {
  requiredCredentials,
  resolveSectionLayout,
  type ResolvedBlock,
} from "../lib/section-layout"
import {
  SECTION_COMPANION_PAGES,
  SECTION_I18N,
  SETTINGS_QUERY_KEY,
  formFieldName,
  settingAnchorId,
  type SystemSettingsField,
  type SystemSettingsSection,
} from "../lib/sections"
import {
  ReadOnlyFieldRow,
  SecretFieldRow,
  SettingField,
  type RowPlacement,
} from "./setting-field"

/** Input-friendly form value for a field (numbers/arrays as text). */
function toFormValue(field: SystemSettingsField): string | boolean {
  const effective = field.effectiveValue
  switch (field.kind) {
    case "bool":
      return effective === true
    case "stringArray":
      return Array.isArray(effective) ? effective.join("\n") : ""
    default:
      return effective === null || effective === undefined ? "" : String(effective)
  }
}

/** Normalized value to compare against the baseline and send to the API. */
function toApiValue(field: SystemSettingsField, raw: unknown): unknown {
  switch (field.kind) {
    case "bool":
      return raw === true
    case "int":
      return Number(String(raw ?? "").trim())
    case "stringArray":
      return String(raw ?? "")
        .split("\n")
        .map((line) => line.trim())
        .filter((line) => line.length > 0)
    default:
      return String(raw ?? "")
  }
}

function normalizedBaseline(field: SystemSettingsField): unknown {
  const baseline = field.baselineValue
  switch (field.kind) {
    case "bool":
      return baseline === true
    case "int":
      return baseline === null || baseline === undefined ? null : Number(baseline)
    case "stringArray":
      return Array.isArray(baseline) ? baseline : []
    default:
      return baseline === null || baseline === undefined ? "" : String(baseline)
  }
}

function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null)
}

/** Builds the sparse nested override object ("A:B:C" → {A:{B:{C: value}}}). */
function setNested(target: Record<string, unknown>, path: string, value: unknown) {
  const segments = path.split(":")
  let cursor = target
  for (let i = 0; i < segments.length - 1; i++) {
    cursor[segments[i]] ??= {}
    cursor = cursor[segments[i]] as Record<string, unknown>
  }
  cursor[segments[segments.length - 1]] = value
}

/**
 * Brings the setting named by `?field=` into view and marks it briefly, so a
 * search result lands on one row rather than on a page of forty.
 *
 * A missing target is not an error: a deployed console can be a release behind
 * the backend and simply not render a field the server already knows about.
 * The navigation still put the user on the right section.
 */
function useFieldAnchor(sectionKey: string) {
  const [searchParams] = useSearchParams()
  const target = searchParams.get("field")

  React.useEffect(() => {
    if (!target) return
    const element = document.getElementById(settingAnchorId(target))
    if (!element) return

    const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
    element.scrollIntoView({
      block: "center",
      behavior: reduceMotion ? "auto" : "smooth",
    })
    element.setAttribute("data-highlight", "true")
    const timer = setTimeout(() => element.removeAttribute("data-highlight"), 2000)
    return () => clearTimeout(timer)
    // Re-runs when the section changes too, since the row only exists once
    // its own section is rendered.
  }, [target, sectionKey])
}

/**
 * The way out of a section whose real controls are operations rather than
 * settings. Generic on purpose: which sections have one is declared in
 * `SECTION_COMPANION_PAGES`, and a section without an entry renders nothing
 * here. The permission gate is the companion page's own, not the section's.
 */
function SectionCompanionAction({ sectionKey }: { sectionKey: string }) {
  const { t } = useTranslation()
  const companion = SECTION_COMPANION_PAGES[sectionKey]
  if (!companion) return null

  return (
    <RequirePermission permission={companion.permission}>
      <CardFooter>
        <Button asChild>
          <Link to={companion.route}>{t(companion.actionLabelKey)}</Link>
        </Button>
      </CardFooter>
    </RequirePermission>
  )
}

/**
 * One field as whichever kind of row it is, so the two places that render a
 * list of settings — the section card and a category panel — decide only
 * WHERE a row goes, never what it is.
 *
 * `control` is optional because the bootstrap sections have no form: those are
 * read before the database layer exists, so every field there is shown rather
 * than edited.
 */
function SettingRow({
  field,
  sectionI18n,
  control,
  placement = "card",
  media,
}: {
  field: SystemSettingsField
  sectionI18n: string | undefined
  control?: Control<FieldValues>
  placement?: RowPlacement
  media?: React.ReactNode
}) {
  if (field.sensitive) {
    return (
      <SecretFieldRow
        sectionI18n={sectionI18n}
        field={field}
        placement={placement}
      />
    )
  }
  if (field.readOnly || !control) {
    return (
      <ReadOnlyFieldRow
        sectionI18n={sectionI18n}
        field={field}
        placement={placement}
      />
    )
  }
  return (
    <SettingField
      control={control}
      sectionI18n={sectionI18n}
      field={field}
      placement={placement}
      media={media}
    />
  )
}

/**
 * The one state a gated category can be in that reports itself nowhere else:
 * switched on with a setting it needs still blank. Nothing errors, nothing
 * logs, and the feature simply never happens — so the panel says it, from the
 * values in the form rather than from what was last saved.
 *
 * Only the settings this form can actually read are counted. A secret-owned
 * field's value lives in Secret management, so its emptiness is not knowable
 * here and is not claimed.
 */
function CategoryIncomplete({
  control,
  sectionI18n,
  switchField,
  credentials,
}: {
  control: Control<FieldValues>
  sectionI18n: string | undefined
  switchField: SystemSettingsField
  credentials: SystemSettingsField[]
}) {
  const { t } = useTranslation()
  const names = React.useMemo(
    () => [switchField, ...credentials].map((f) => formFieldName(f.path ?? "")),
    [switchField, credentials]
  )
  const [enabled, ...values] = useWatch({ control, name: names }) as unknown[]

  const title = sectionI18n
    ? t(`systemSettings.${sectionI18n}.incompleteTitle`, { defaultValue: "" })
    : ""

  if (enabled !== true || !title) return null
  if (values.every((value) => String(value ?? "").trim().length > 0)) return null

  return (
    <div className="px-4 pt-4">
      <Alert variant="destructive">
        <TriangleAlert />
        <AlertTitle>{title}</AlertTitle>
        <AlertDescription>
          {t(`systemSettings.${sectionI18n}.incompleteBody`, { defaultValue: "" })}
        </AlertDescription>
      </Alert>
    </div>
  )
}

/**
 * A category, drawn as a panel: the switch that governs it wears the panel's
 * header, and the settings it gates sit under it inside the same border. Where
 * the subject has an official mark it goes in the header too — a panel is
 * found by its logo before its title is read.
 *
 * `min-w-0`: a fieldset's own `min-inline-size: min-content` would stop it
 * shrinking with the card, and the overflow would be the CARD's, not this
 * element's — the kind of break that shows up two components away.
 */
function CategoryPanel({
  sectionI18n,
  control,
  entry,
}: {
  sectionI18n: string | undefined
  control: Control<FieldValues>
  entry: ResolvedBlock
}) {
  const { block, switchField, fields } = entry
  if (block.kind !== "category" || !switchField) return null
  const Icon = block.icon

  return (
    <FieldSet className="min-w-0 gap-0 rounded-xl border">
      <SettingRow
        field={switchField}
        sectionI18n={sectionI18n}
        control={control}
        placement="categoryHeader"
        media={Icon ? <Icon className="size-5 shrink-0" /> : undefined}
      />
      <CategoryIncomplete
        control={control}
        sectionI18n={sectionI18n}
        switchField={switchField}
        credentials={requiredCredentials(fields)}
      />
      {fields.map((field) => (
        <SettingRow
          key={field.path}
          field={field}
          sectionI18n={sectionI18n}
          control={control}
          placement="category"
        />
      ))}
    </FieldSet>
  )
}

/**
 * A group, drawn as a legend over rows that stay at card level. No border:
 * proximity and a heading already group them, and a container around every
 * related pair would spend the one device that means "this has an off switch".
 *
 * The air goes on the MARGIN, not the padding — a `legend` is laid out against
 * the fieldset's border box, so padding-top moves the rows down and leaves the
 * heading welded to whatever sits above it. An untranslated heading renders as
 * no heading rather than as a raw key.
 */
function GroupBlockRows({
  sectionI18n,
  control,
  headingKey,
  fields,
}: {
  sectionI18n: string | undefined
  control?: Control<FieldValues>
  headingKey: string
  fields: SystemSettingsField[]
}) {
  const { t } = useTranslation()
  const base = sectionI18n ? `systemSettings.${sectionI18n}.${headingKey}` : null
  const heading = base ? t(base, { defaultValue: "" }) : ""
  const description = base ? t(`${base}Description`, { defaultValue: "" }) : ""

  // The fieldset renders whether or not the heading translated: it is the one
  // flex item holding these rows flush against each other, and dropping it
  // would let the parent's gap fall BETWEEN ruled rows that are meant to read
  // as one run.
  return (
    <FieldSet className="mt-4 min-w-0 gap-0 first:mt-0">
      {heading ? <FieldLegend>{heading}</FieldLegend> : null}
      {description ? (
        <FieldDescription className="-mt-2 mb-3">{description}</FieldDescription>
      ) : null}
      {fields.map((field) => (
        <SettingRow
          key={field.path}
          field={field}
          sectionI18n={sectionI18n}
          control={control}
        />
      ))}
    </FieldSet>
  )
}

export function SectionForm({ section }: { section: SystemSettingsSection }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const sectionKey = section.key ?? ""
  const sectionI18n = SECTION_I18N[sectionKey]
  const [confirmReset, setConfirmReset] = React.useState(false)

  useFieldAnchor(sectionKey)

  const fields = React.useMemo(() => section.fields ?? [], [section.fields])
  const editable = React.useMemo(
    () => fields.filter((f) => !f.sensitive && !f.readOnly),
    [fields]
  )
  const { blocks, general } = React.useMemo(
    () => resolveSectionLayout(sectionKey, fields),
    [sectionKey, fields]
  )

  const defaultValues = React.useMemo(() => {
    const values: FieldValues = {}
    for (const field of editable) {
      values[formFieldName(field.path ?? "")] = toFormValue(field)
    }
    return values
  }, [editable])

  const form = useForm<FieldValues>({ defaultValues })
  const unsavedPrompt = useUnsavedChangesPrompt({
    isDirty: form.formState.isDirty,
  })

  const invalidate = React.useCallback(
    () => void queryClient.invalidateQueries({ queryKey: SETTINGS_QUERY_KEY }),
    [queryClient]
  )

  const handleFailure = React.useCallback(
    (error: unknown, status?: number) => {
      if (status === 409) {
        toast.error(t("systemSettings.conflict"))
        invalidate()
        return
      }
      toast.error(getErrorMessage(error))
    },
    [invalidate, t]
  )

  const save = useMutation({
    mutationFn: async (values: FieldValues) => {
      // The payload is the COMPLETE override set: only fields that differ
      // from the file baseline are included, so clearing a customization is
      // as simple as typing the file value back.
      const overrides: Record<string, unknown> = {}
      for (const field of editable) {
        const next = toApiValue(field, values[formFieldName(field.path ?? "")])
        if (!sameValue(next, normalizedBaseline(field))) {
          setNested(overrides, field.path ?? "", next)
        }
      }

      const { error, response } = await api.PUT(
        "/api/v1/admin/system-settings/{sectionKey}",
        {
          params: { path: { sectionKey } },
          body: { overrides, rowVersion: section.rowVersion ?? null },
        }
      )
      if (error) throw Object.assign(new Error("save failed"), { error, status: response.status })
    },
    onSuccess: () => {
      toast.success(t("systemSettings.saved"))
      invalidate()
    },
    onError: (failure: { error?: unknown; status?: number }) =>
      handleFailure(failure.error ?? failure, failure.status),
  })

  const reset = useMutation({
    mutationFn: async () => {
      const { error, response } = await api.POST(
        "/api/v1/admin/system-settings/{sectionKey}/reset",
        { params: { path: { sectionKey } } }
      )
      if (error) throw Object.assign(new Error("reset failed"), { error, status: response.status })
    },
    onSuccess: () => {
      setConfirmReset(false)
      toast.success(t("systemSettings.resetDone"))
      invalidate()
    },
    onError: (failure: { error?: unknown; status?: number }) => {
      setConfirmReset(false)
      handleFailure(failure.error ?? failure, failure.status)
    },
  })

  const sendTestEmail = useMutation({
    mutationFn: async () => {
      const { error, response } = await api.POST(
        "/api/v1/admin/system-settings/email/test",
        {}
      )
      if (error) throw Object.assign(new Error("test failed"), { error, status: response.status })
    },
    onSuccess: () => toast.success(t("systemSettings.testEmailSent")),
    onError: (failure: { error?: unknown }) =>
      toast.error(getErrorMessage(failure.error ?? failure)),
  })

  const hasOverrides = Number(section.version ?? 0) > 0
  const title = sectionI18n
    ? t(`systemSettings.${sectionI18n}.title`, { defaultValue: sectionKey })
    : sectionKey
  const description = sectionI18n
    ? t(`systemSettings.${sectionI18n}.description`, { defaultValue: "" })
    : ""

  // Bootstrap sections are information cards: consumed before the database
  // layer exists, so there is nothing to save from here.
  if (!section.editable) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{title}</CardTitle>
          {description ? <CardDescription>{description}</CardDescription> : null}
        </CardHeader>
        <CardContent>
          <FieldGroup>
            {fields.map((field) => (
              <SettingRow
                key={field.path}
                field={field}
                sectionI18n={sectionI18n}
              />
            ))}
          </FieldGroup>
        </CardContent>
        <SectionCompanionAction sectionKey={sectionKey} />
      </Card>
    )
  }

  return (
    <Card>
      {unsavedPrompt}
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      <CardContent>
        <Form {...form}>
          <form onSubmit={form.handleSubmit((values) => save.mutate(values))}>
            {/* Rows are ruled off from one another, so the breathing room
                lives in the row padding and the group adds no extra gap. */}
            <FieldGroup className="gap-0">
              {/* Blocks in declared order, then whatever no block claimed. The
                  peer gap is 4; a group adds its own `mt-4` on top of it, so a
                  heading gets twice the air of a panel-to-panel seam — more
                  space above a heading than below it, which is what makes it
                  read as heading rather than as another row. */}
              {blocks.length > 0 ? (
                <div className="flex flex-col gap-4">
                  {blocks.map((entry) =>
                    entry.block.kind === "category" ? (
                      <CategoryPanel
                        key={entry.block.switchPath}
                        sectionI18n={sectionI18n}
                        control={form.control}
                        entry={entry}
                      />
                    ) : (
                      <GroupBlockRows
                        key={entry.block.key}
                        sectionI18n={sectionI18n}
                        control={form.control}
                        headingKey={`groups.${entry.block.key}`}
                        fields={entry.fields}
                      />
                    )
                  )}
                  {/* Settings no block claimed stay at card level under their
                      own heading. The difference in level is what says they
                      belong to none of the blocks above. */}
                  {general.length > 0 ? (
                    <GroupBlockRows
                      sectionI18n={sectionI18n}
                      control={form.control}
                      headingKey="groups.general"
                      fields={general}
                    />
                  ) : null}
                </div>
              ) : (
                general.map((field) => (
                  <SettingRow
                    key={field.path}
                    field={field}
                    sectionI18n={sectionI18n}
                    control={form.control}
                  />
                ))
              )}
              {/* Primary actions lead; the destructive-ish reset sits at the
                  far end so it can never be hit while reaching for Save. The
                  last row's rule already separates them. */}
              <div className="flex flex-wrap items-center justify-between gap-3 pt-6">
                <div className="flex flex-wrap items-center gap-3">
                  <Button type="submit" disabled={save.isPending}>
                    {save.isPending ? <Spinner data-icon="inline-start" /> : null}
                    {t("common.save")}
                  </Button>
                  {form.formState.isDirty ? (
                    <Button
                      type="button"
                      variant="ghost"
                      onClick={() => form.reset()}
                    >
                      {t("common.cancel")}
                    </Button>
                  ) : null}
                  {sectionKey === "Email" ? (
                    <Button
                      type="button"
                      variant="outline"
                      disabled={sendTestEmail.isPending}
                      onClick={() => sendTestEmail.mutate()}
                    >
                      {sendTestEmail.isPending ? <Spinner data-icon="inline-start" /> : null}
                      {t("systemSettings.sendTestEmail")}
                    </Button>
                  ) : null}
                </div>
                {hasOverrides ? (
                  <Button
                    type="button"
                    variant="outline"
                    onClick={() => setConfirmReset(true)}
                  >
                    {t("systemSettings.resetSection")}
                  </Button>
                ) : null}
              </div>
            </FieldGroup>
          </form>
        </Form>
      </CardContent>
      <SectionCompanionAction sectionKey={sectionKey} />
      <ConfirmDialog
        open={confirmReset}
        onOpenChange={setConfirmReset}
        title={t("systemSettings.resetConfirmTitle")}
        description={t("systemSettings.resetConfirmBody")}
        destructive
        loading={reset.isPending}
        onConfirm={() => reset.mutate()}
      />
    </Card>
  )
}
