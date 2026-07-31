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

import { fieldI18nKey, type SystemSettingsField } from "../lib/sections"

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

/** Shows the file/default fallback under a customized field. */
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

/** A secret-owned field: value lives in Secret Management, never here. */
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
    <Field orientation="responsive">
      <FieldContent>
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
    <Field data-disabled>
      <FieldLabel>
        {label}
        <Badge variant="outline">{t("systemSettings.readOnly")}</Badge>
      </FieldLabel>
      <Input
        value={value === null || value === undefined ? "" : String(value)}
        disabled
        dir="ltr"
      />
      {hint ? <FieldDescription>{hint}</FieldDescription> : null}
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
  const name = field.path ?? ""
  const kind = field.kind ?? "string"

  if (kind === "bool") {
    return (
      <FormField
        control={control}
        name={name}
        render={({ field: rhf }) => (
          <FormItem orientation="horizontal">
            <FieldContent>
              <FormLabel className="font-normal">
                {label}
                <FieldBadges field={field} />
              </FormLabel>
              {hint ? <FormDescription>{hint}</FormDescription> : null}
              <BaselineNote field={field} />
            </FieldContent>
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
          <FormItem>
            <FormLabel>
              {label}
              <FieldBadges field={field} />
            </FormLabel>
            <FormControl>
              <ToggleGroup
                type="single"
                spacing={2}
                variant="outline"
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
            {hint ? <FormDescription>{hint}</FormDescription> : null}
            <BaselineNote field={field} />
            <FormMessage />
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
          <FormItem>
            <FormLabel>
              {label}
              <FieldBadges field={field} />
            </FormLabel>
            <FormControl>
              <Textarea
                value={typeof rhf.value === "string" ? rhf.value : ""}
                onChange={rhf.onChange}
                onBlur={rhf.onBlur}
                rows={4}
                dir="ltr"
              />
            </FormControl>
            <FormDescription>
              {[hint, t("systemSettings.arrayFieldHint")].filter(Boolean).join(" ")}
            </FormDescription>
            <BaselineNote field={field} />
            <FormMessage />
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
        <FormItem>
          <FormLabel>
            {label}
            <FieldBadges field={field} />
          </FormLabel>
          <FormControl>
            <Input
              value={typeof rhf.value === "string" ? rhf.value : ""}
              onChange={rhf.onChange}
              onBlur={rhf.onBlur}
              inputMode={isInt ? "numeric" : undefined}
              dir="ltr"
            />
          </FormControl>
          {hint ? <FormDescription>{hint}</FormDescription> : null}
          <BaselineNote field={field} />
          <FormMessage />
        </FormItem>
      )}
    />
  )
}
