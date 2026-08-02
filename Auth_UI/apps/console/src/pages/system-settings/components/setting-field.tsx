import { Link } from "react-router-dom"
import { useTranslation } from "react-i18next"
import type { Control, FieldValues } from "react-hook-form"

import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { Switch } from "@authsystem/ui/switch"
import { Textarea } from "@authsystem/ui/textarea"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"

import {
  fieldI18nKey,
  formFieldName,
  type SystemSettingsField,
} from "../lib/sections"

/**
 * Row geometry, applied to every setting alike (Windows 11 SettingsCard /
 * macOS System Settings): the row spans the card, the label and its hint read
 * at the start, and the control is pinned to the END of the row at a width
 * that suits its value. That way a wide card is used by the layout instead of
 * by a stretched input, and the controls line up as one column no matter how
 * wide the window gets.
 *
 * On a large monitor that puts real distance between a label and its control,
 * so each row is ruled off from the next: the line pairs the two ends of a
 * row while simply reading down the page, and the hover tint (plus
 * focus-within, so the aid is not mouse-only) confirms the one in play. The
 * rule and the tint reuse the tokens the separator and the data tables
 * already use.
 *
 * The row bleeds into the card padding rather than indenting the content:
 * that needs the explicit width, because Field is `w-full` and a negative
 * margin alone would slide the row sideways instead of widening it.
 */
const ROW =
  "justify-between border-b -mx-3 w-[calc(100%+1.5rem)] px-3 py-4 transition-colors hover:bg-muted/50 focus-within:bg-muted/50"

/** Explanatory text stops at a comfortable measure rather than the card edge. */
const TEXT_BLOCK = "max-w-2xl"

/**
 * Control width, sized to the value it holds. Declared on the ROW targeting
 * its last child rather than on the control: the responsive Field variant
 * already sets `[&>*]:w-auto` once the row is horizontal, and a plain width
 * class on the control loses to it on specificity. Below the row breakpoint
 * the primitive's own `w-full` still applies, so stacked controls fill the
 * width as they should.
 */
const CONTROL = {
  int: "@md/field-group:[&>*:last-child]:w-40",
  text: "@md/field-group:[&>*:last-child]:w-80",
  area: "@md/field-group:[&>*:last-child]:w-[28rem]",
} as const

function useFieldTexts(sectionI18n: string | undefined, field: SystemSettingsField) {
  const { t } = useTranslation()
  const key = fieldI18nKey(field.path ?? "")
  const base = sectionI18n ? `systemSettings.${sectionI18n}.${key}` : null
  // Fall back to the raw path so an untranslated section stays usable.
  const label = base ? t(base, { defaultValue: field.path ?? "" }) : (field.path ?? "")
  const hint = base ? t(`${base}Hint`, { defaultValue: "" }) : ""
  return { label, hint }
}

function FieldBadges({ field }: { field: SystemSettingsField }) {
  const { t } = useTranslation()
  return (
    <>
      {field.source === "database" ? (
        <Badge variant="secondary">{t("systemSettings.overridden")}</Badge>
      ) : null}
      {field.isPendingRestart ? (
        <Badge variant="destructive">{t("systemSettings.pendingRestart")}</Badge>
      ) : field.restartRequired ? (
        <Badge variant="outline">{t("systemSettings.restartRequired")}</Badge>
      ) : null}
    </>
  )
}

/** Shows the default a customized field would fall back to. */
function BaselineNote({ field }: { field: SystemSettingsField }) {
  const { t } = useTranslation()
  if (field.source !== "database") return null
  const baseline = field.baselineValue
  const rendered = Array.isArray(baseline)
    ? baseline.join(", ")
    : baseline === null || baseline === undefined || baseline === ""
      ? t("systemSettings.notSet")
      : String(baseline)
  return <FieldDescription>{t("systemSettings.fileValue", { value: rendered })}</FieldDescription>
}

/** A secret-owned field: the value lives in Secret Management, never here. */
export function SecretFieldRow({
  sectionI18n,
  field,
}: {
  sectionI18n: string | undefined
  field: SystemSettingsField
}) {
  const { t } = useTranslation()
  const { label } = useFieldTexts(sectionI18n, field)
  return (
    <Field orientation="responsive" className={ROW}>
      <FieldContent className={TEXT_BLOCK}>
        <FieldLabel>{label}</FieldLabel>
        <FieldDescription>{t("systemSettings.managedInSecrets")}</FieldDescription>
      </FieldContent>
      <Button variant="outline" size="sm" asChild>
        <Link to="/admin/secrets">{t("systemSettings.openSecrets")}</Link>
      </Button>
    </Field>
  )
}

/** A read-only field: shown for transparency, not editable. */
export function ReadOnlyFieldRow({
  sectionI18n,
  field,
}: {
  sectionI18n: string | undefined
  field: SystemSettingsField
}) {
  const { t } = useTranslation()
  const { label, hint } = useFieldTexts(sectionI18n, field)
  const value = field.effectiveValue
  return (
    <Field orientation="responsive" className={`${ROW} ${CONTROL.text}`} data-disabled>
      <FieldContent className={TEXT_BLOCK}>
        <FieldLabel>
          {label}
          <Badge variant="outline">{t("systemSettings.readOnly")}</Badge>
        </FieldLabel>
        {hint ? <FieldDescription>{hint}</FieldDescription> : null}
      </FieldContent>
      <Input
        value={value === null || value === undefined ? "" : String(value)}
        disabled
        dir="ltr"
      />
    </Field>
  )
}

/**
 * One editable setting bound into the section form. Form values are kept in
 * input-friendly shapes (numbers as strings, arrays as one-per-line text)
 * and normalized on submit.
 */
export function SettingField({
  control,
  sectionI18n,
  field,
}: {
  control: Control<FieldValues>
  sectionI18n: string | undefined
  field: SystemSettingsField
}) {
  const { t } = useTranslation()
  const { label, hint } = useFieldTexts(sectionI18n, field)
  const name = formFieldName(field.path ?? "")
  const kind = field.kind ?? "string"

  // The hint, the default-value note and any validation message all belong to
  // the text block; as direct row children they would become extra columns.
  const textBlock = (extraHint?: string) => (
    <FieldContent className={TEXT_BLOCK}>
      <FormLabel className={kind === "bool" ? "font-normal" : undefined}>
        {label}
        <FieldBadges field={field} />
      </FormLabel>
      {hint || extraHint ? (
        <FormDescription>{[hint, extraHint].filter(Boolean).join(" ")}</FormDescription>
      ) : null}
      <BaselineNote field={field} />
      <FormMessage />
    </FieldContent>
  )

  if (kind === "bool") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem orientation="horizontal" className={ROW}>
            {textBlock()}
            <FormControl>
              <Switch checked={rhf.value === true} onCheckedChange={rhf.onChange} />
            </FormControl>
          </FormItem>
        )}
      />
    )
  }

  if (kind === "enum") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem orientation="responsive" className={ROW}>
            {textBlock()}
            <FormControl>
              <ToggleGroup
                type="single"
                spacing={2}
                variant="outline"
                // Wraps only when the row is too narrow to hold every option.
                className="flex-wrap"
                value={typeof rhf.value === "string" ? rhf.value : ""}
                onValueChange={(value) => {
                  if (value) rhf.onChange(value)
                }}
              >
                {(field.allowedValues ?? []).map((option) => (
                  <ToggleGroupItem key={option} value={option}>
                    {option}
                  </ToggleGroupItem>
                ))}
              </ToggleGroup>
            </FormControl>
          </FormItem>
        )}
      />
    )
  }

  if (kind === "stringArray") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem orientation="responsive" className={`${ROW} ${CONTROL.area}`}>
            {textBlock(t("systemSettings.arrayFieldHint"))}
            <FormControl>
              <Textarea
                value={typeof rhf.value === "string" ? rhf.value : ""}
                onChange={rhf.onChange}
                onBlur={rhf.onBlur}
                rows={4}
                dir="ltr"
              />
            </FormControl>
          </FormItem>
        )}
      />
    )
  }

  const isInt = kind === "int"
  return (
    <FormField
      control={control}
      name={name}
      rules={
        isInt
          ? {
              validate: (raw: unknown) => {
                const text = String(raw ?? "").trim()
                if (text.length === 0) return t("validation.required")
                if (!/^-?\d+$/.test(text)) return t("validation.wholeNumber")
                const value = Number(text)
                if (field.min !== null && field.min !== undefined && value < Number(field.min))
                  return t("validation.min", { min: field.min })
                if (field.max !== null && field.max !== undefined && value > Number(field.max))
                  return t("validation.max", { max: field.max })
                return true
              },
            }
          : undefined
      }
      render={({ field: rhf }) => (
        <FormItem
          orientation="responsive"
          className={`${ROW} ${isInt ? CONTROL.int : CONTROL.text}`}
        >
          {textBlock()}
          <FormControl>
            <Input
              value={typeof rhf.value === "string" ? rhf.value : ""}
              onChange={rhf.onChange}
              onBlur={rhf.onBlur}
              inputMode={isInt ? "numeric" : undefined}
              dir="ltr"
            />
          </FormControl>
        </FormItem>
      )}
    />
  )
}
