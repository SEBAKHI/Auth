import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { FormDialog } from "@astoom/ui/common/form-dialog"
import { PresetField } from "@astoom/ui/common/preset-field"
import { FieldContent } from "@astoom/ui/field"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { Input } from "@astoom/ui/input"
import { Switch } from "@astoom/ui/switch"
import { Textarea } from "@astoom/ui/textarea"
import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import type { Schemas } from "@astoom/api/types"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

/** Empty input means "disabled" (null); otherwise a parsed integer. */
function emptyToNullNumber(value: string | undefined): number | null {
  if (!value || value.trim().length === 0) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

type ToggleName =
  | "allowSelfRegistration"
  | "requireTwoFactor"
  | "requireEmailVerification"

function useToggles(): { name: ToggleName; label: string; hint: string }[] {
  const { t } = useTranslation()
  return [
    {
      name: "allowSelfRegistration",
      label: t("applications.allowSelfRegistration"),
      hint: t("applications.allowSelfRegistrationHint"),
    },
    {
      name: "requireTwoFactor",
      label: t("applications.requireTwoFactor"),
      hint: t("applications.requireTwoFactorHint"),
    },
    {
      name: "requireEmailVerification",
      label: t("applications.requireEmailVerification"),
      hint: t("applications.requireEmailVerificationHint"),
    },
  ]
}

/**
 * Preset choices for the three numeric session settings, so the common cases are
 * one click and nobody has to reason in raw minutes.
 *
 * Shared by the create and edit dialogs, which is the point: the two forms
 * previously duplicated every field definition and could drift apart.
 *
 * Bounds mirror the server. `SessionTimeoutMinutes` and `MaxConcurrentSessions`
 * are `GreaterThan(0)`, so neither offers an "unlimited" choice — it would be
 * rejected. `ReauthenticationMaxAgeMinutes` is null-or-1..10080, so its "off"
 * choice is the empty string and its largest preset is exactly the 10080 cap.
 */
function useSessionPresets() {
  const { t } = useTranslation()
  const minutes = (count: number) => t("common.minutesShort", { count })
  const hours = (count: number) => t("common.hoursShort", { count })
  const days = (count: number) => t("common.daysShort", { count })

  return {
    sessionTimeout: [
      { value: "15", label: minutes(15) },
      { value: "30", label: minutes(30) },
      { value: "60", label: hours(1) },
      { value: "480", label: hours(8) },
      { value: "1440", label: hours(24) },
    ],
    maxSessions: [
      { value: "1", label: "1" },
      { value: "3", label: "3" },
      { value: "5", label: "5" },
      { value: "10", label: "10" },
      { value: "25", label: "25" },
    ],
    reauthMaxAge: [
      { value: "", label: t("common.off") },
      { value: "15", label: minutes(15) },
      { value: "60", label: hours(1) },
      { value: "1440", label: hours(24) },
      { value: "10080", label: days(7) },
    ],
  }
}

/** Create dialog — full application configuration. */
export function ApplicationCreateDialog({
  open,
  onOpenChange,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const toggles = useToggles()
  const presets = useSessionPresets()

  const schema = z.object({
    code: z.string().min(1, t("validation.required")),
    name: z.string().min(1, t("validation.required")),
    description: z.string().optional(),
    baseUrl: z.string().optional(),
    logoUrl: z.string().optional(),
    contactEmail: z.string().optional(),
    allowSelfRegistration: z.boolean(),
    requireTwoFactor: z.boolean(),
    requireEmailVerification: z.boolean(),
    sessionTimeoutMinutes: z.string().min(1, t("validation.required")),
    maxConcurrentSessions: z.string().min(1, t("validation.required")),
    reauthMaxAgeMinutes: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: "",
      name: "",
      description: "",
      baseUrl: "",
      logoUrl: "",
      contactEmail: "",
      allowSelfRegistration: false,
      requireTwoFactor: false,
      requireEmailVerification: false,
      sessionTimeoutMinutes: "60",
      maxConcurrentSessions: "5",
      reauthMaxAgeMinutes: "",
    },
  })

  React.useEffect(() => {
    if (open) form.reset()
  }, [open, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const { error } = await api.POST("/api/v1/Applications", {
        body: {
          code: values.code,
          name: values.name,
          description: emptyToNull(values.description),
          baseUrl: emptyToNull(values.baseUrl),
          logoUrl: emptyToNull(values.logoUrl),
          contactEmail: emptyToNull(values.contactEmail),
          allowSelfRegistration: values.allowSelfRegistration,
          requireTwoFactor: values.requireTwoFactor,
          requireEmailVerification: values.requireEmailVerification,
          sessionTimeoutMinutes: Number(values.sessionTimeoutMinutes) || 60,
          maxConcurrentSessions: Number(values.maxConcurrentSessions) || 5,
          reauthenticationMaxAgeMinutes: emptyToNullNumber(
            values.reauthMaxAgeMinutes,
          ),
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["applications"] })
      toast.success(t("applications.created"))
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={t("applications.createTitle")}
      description={t("applications.subtitle")}
      formId="application-create-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={t("common.create")}
      pending={mutation.isPending}
    >
      <FormField
        control={form.control}
        name="code"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.code")}</FormLabel>
            <FormControl>
              <Input placeholder="billing-portal" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>{t("applications.codeHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="name"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.name")}</FormLabel>
            <FormControl>
              <Input placeholder={t("applications.namePlaceholder")} {...field} />
            </FormControl>
            <FormDescription>{t("applications.nameHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="description"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.description")}</FormLabel>
            <FormControl>
              <Textarea rows={2} placeholder={t("applications.descriptionPlaceholder")} {...field} />
            </FormControl>
            <FormDescription>
              {t("applications.descriptionHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="baseUrl"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.baseUrl")}</FormLabel>
            <FormControl>
              <Input placeholder="https://app.example.com" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>{t("applications.baseUrlHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="logoUrl"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.logoUrl")}</FormLabel>
            <FormControl>
              <Input placeholder="https://cdn.example.com/logo.svg" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>{t("applications.logoUrlHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="contactEmail"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.contactEmail")}</FormLabel>
            <FormControl>
              <Input type="email" placeholder="support@example.com" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>
              {t("applications.contactEmailHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="sessionTimeoutMinutes"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.sessionTimeout")}</FormLabel>
            <FormControl>
              <PresetField
                presets={presets.sessionTimeout}
                value={field.value}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    type="number"
                    min={1}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>
              {t("applications.sessionTimeoutHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="maxConcurrentSessions"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.maxSessions")}</FormLabel>
            <FormControl>
              <PresetField
                presets={presets.maxSessions}
                value={field.value}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    type="number"
                    min={1}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>
              {t("applications.maxSessionsHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="reauthMaxAgeMinutes"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.reauthMaxAge")}</FormLabel>
            <FormControl>
              <PresetField
                presets={presets.reauthMaxAge}
                value={field.value ?? ""}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    type="number"
                    min={1}
                    max={10080}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>
              {t("applications.reauthMaxAgeHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      {toggles.map((item) => (
        <FormField
          key={item.name}
          control={form.control}
          name={item.name}
          render={({ field }) => (
            <FormItem orientation="horizontal">
              <FieldContent>
                <FormLabel className="font-normal">{item.label}</FormLabel>
                <FormDescription>{item.hint}</FormDescription>
              </FieldContent>
              <FormControl>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                />
              </FormControl>
            </FormItem>
          )}
        />
      ))}
    </FormDialog>
  )
}

/**
 * Edit dialog — a full update of the application's editable fields. `code` is
 * immutable and is not part of the update contract.
 */
export function ApplicationEditDialog({
  open,
  onOpenChange,
  application,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  application: Schemas["ApplicationDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const toggles = useToggles()
  const presets = useSessionPresets()

  const schema = z.object({
    name: z.string().min(1, t("validation.required")),
    description: z.string().optional(),
    baseUrl: z.string().optional(),
    logoUrl: z.string().optional(),
    contactEmail: z.string().optional(),
    allowSelfRegistration: z.boolean(),
    requireTwoFactor: z.boolean(),
    requireEmailVerification: z.boolean(),
    sessionTimeoutMinutes: z.string().min(1, t("validation.required")),
    maxConcurrentSessions: z.string().min(1, t("validation.required")),
    redirectUris: z.string().optional(),
    reauthMaxAgeMinutes: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      description: "",
      baseUrl: "",
      logoUrl: "",
      contactEmail: "",
      allowSelfRegistration: false,
      requireTwoFactor: false,
      requireEmailVerification: false,
      sessionTimeoutMinutes: "60",
      maxConcurrentSessions: "5",
      redirectUris: "",
      reauthMaxAgeMinutes: "",
    },
  })

  React.useEffect(() => {
    if (!open) return
    form.reset({
      name: application.name ?? "",
      description: application.description ?? "",
      baseUrl: application.baseUrl ?? "",
      logoUrl: application.logoUrl ?? "",
      contactEmail: application.contactEmail ?? "",
      allowSelfRegistration: application.allowSelfRegistration ?? false,
      requireTwoFactor: application.requireTwoFactor ?? false,
      requireEmailVerification: application.requireEmailVerification ?? false,
      sessionTimeoutMinutes: String(application.sessionTimeoutMinutes ?? 60),
      maxConcurrentSessions: String(application.maxConcurrentSessions ?? 5),
      redirectUris: (application.redirectUris ?? []).join("\n"),
      reauthMaxAgeMinutes:
        application.reauthenticationMaxAgeMinutes != null
          ? String(application.reauthenticationMaxAgeMinutes)
          : "",
    })
  }, [open, application, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const { error } = await api.PUT("/api/v1/Applications/{id}", {
        params: { path: { id: application.id as string } },
        body: {
          name: values.name,
          description: emptyToNull(values.description),
          baseUrl: emptyToNull(values.baseUrl),
          logoUrl: emptyToNull(values.logoUrl),
          contactEmail: emptyToNull(values.contactEmail),
          allowSelfRegistration: values.allowSelfRegistration,
          requireTwoFactor: values.requireTwoFactor,
          requireEmailVerification: values.requireEmailVerification,
          sessionTimeoutMinutes: Number(values.sessionTimeoutMinutes) || 60,
          maxConcurrentSessions: Number(values.maxConcurrentSessions) || 5,
          redirectUris: (values.redirectUris ?? "")
            .split("\n")
            .map((uri) => uri.trim())
            .filter((uri) => uri.length > 0),
          reauthenticationMaxAgeMinutes: emptyToNullNumber(
            values.reauthMaxAgeMinutes,
          ),
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["applications"] })
      toast.success(t("applications.updated"))
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={t("applications.editTitle")}
      description={application.name}
      formId="application-edit-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={t("common.save")}
      pending={mutation.isPending}
    >
      <FormField
        control={form.control}
        name="name"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.name")}</FormLabel>
            <FormControl>
              <Input placeholder={t("applications.namePlaceholder")} {...field} />
            </FormControl>
            <FormDescription>{t("applications.nameHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="description"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.description")}</FormLabel>
            <FormControl>
              <Textarea rows={2} placeholder={t("applications.descriptionPlaceholder")} {...field} />
            </FormControl>
            <FormDescription>
              {t("applications.descriptionHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="baseUrl"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.baseUrl")}</FormLabel>
            <FormControl>
              <Input placeholder="https://app.example.com" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>{t("applications.baseUrlHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="logoUrl"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.logoUrl")}</FormLabel>
            <FormControl>
              <Input placeholder="https://cdn.example.com/logo.svg" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>{t("applications.logoUrlHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="contactEmail"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.contactEmail")}</FormLabel>
            <FormControl>
              <Input type="email" placeholder="support@example.com" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>
              {t("applications.contactEmailHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="redirectUris"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.redirectUris")}</FormLabel>
            <FormControl>
              <Textarea rows={3} placeholder="https://app.example.com/callback" dir="ltr" {...field} />
            </FormControl>
            <FormDescription>
              {t("applications.redirectUrisHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="sessionTimeoutMinutes"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.sessionTimeout")}</FormLabel>
            <FormControl>
              <PresetField
                presets={presets.sessionTimeout}
                value={field.value}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    type="number"
                    min={1}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>
              {t("applications.sessionTimeoutHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="maxConcurrentSessions"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.maxSessions")}</FormLabel>
            <FormControl>
              <PresetField
                presets={presets.maxSessions}
                value={field.value}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    type="number"
                    min={1}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>
              {t("applications.maxSessionsHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="reauthMaxAgeMinutes"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("applications.reauthMaxAge")}</FormLabel>
            <FormControl>
              <PresetField
                presets={presets.reauthMaxAge}
                value={field.value ?? ""}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    type="number"
                    min={1}
                    max={10080}
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>
              {t("applications.reauthMaxAgeHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      {toggles.map((item) => (
        <FormField
          key={item.name}
          control={form.control}
          name={item.name}
          render={({ field }) => (
            <FormItem orientation="horizontal">
              <FieldContent>
                <FormLabel className="font-normal">{item.label}</FormLabel>
                <FormDescription>{item.hint}</FormDescription>
              </FieldContent>
              <FormControl>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                />
              </FormControl>
            </FormItem>
          )}
        />
      ))}
    </FormDialog>
  )
}
