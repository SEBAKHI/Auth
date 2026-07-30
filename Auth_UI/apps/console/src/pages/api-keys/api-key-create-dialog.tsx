import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { FormDialog } from "@astoom/ui/common/form-dialog"
import { DatePicker, monthsFromNow } from "@astoom/ui/common/date-picker"
import { PresetField } from "@astoom/ui/common/preset-field"
import { ENVIRONMENTS, RATE_PER_DAY, RATE_PER_MINUTE } from "@/lib/presets"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { Input } from "@astoom/ui/input"
import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

/**
 * `RateLimitPerMinute`/`RateLimitPerDay` are non-nullable `int` on the server,
 * validated `GreaterThan(0)`. Fall back to the server's own defaults rather than
 * sending null, which is what the previous blank-by-default form did — a create
 * with untouched rate limits was rejected.
 */
function toIntOr(value: string | undefined, fallback: number): number {
  const parsed = Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

const DEFAULT_RATE_PER_MINUTE = 60
const DEFAULT_RATE_PER_DAY = 10000

export function ApiKeyCreateDialog({
  open,
  onOpenChange,
  applicationId,
  onCreated,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  applicationId: string
  onCreated: (apiKey: string) => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const schema = z.object({
    name: z.string().min(1, t("validation.required")),
    environment: z.string().optional(),
    description: z.string().optional(),
    rateLimitPerMinute: z.string().optional(),
    rateLimitPerDay: z.string().optional(),
    expiresAt: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      environment: "production",
      description: "",
      rateLimitPerMinute: String(DEFAULT_RATE_PER_MINUTE),
      rateLimitPerDay: String(DEFAULT_RATE_PER_DAY),
      expiresAt: "",
    },
  })

  React.useEffect(() => {
    if (open) form.reset()
  }, [open, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const { data, error } = await api.POST("/api/v1/ApiKeys", {
        body: {
          applicationId,
          name: values.name,
          description: emptyToNull(values.description),
          environment: emptyToNull(values.environment),
          rateLimitPerMinute: toIntOr(
            values.rateLimitPerMinute,
            DEFAULT_RATE_PER_MINUTE
          ),
          rateLimitPerDay: toIntOr(values.rateLimitPerDay, DEFAULT_RATE_PER_DAY),
          expiresAt: values.expiresAt
            ? new Date(values.expiresAt).toISOString()
            : null,
        },
      })
      if (error) throw error
      return data
    },
    onSuccess: (data) => {
      void queryClient.invalidateQueries({ queryKey: ["api-keys"] })
      toast.success(t("apiKeys.created"))
      onOpenChange(false)
      if (data?.apiKey) onCreated(data.apiKey)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={t("apiKeys.createTitle")}
      description={t("apiKeys.subtitle")}
      formId="api-key-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={t("common.create")}
      pending={mutation.isPending}
    >
      <FormField
        control={form.control}
        name="name"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.name")}</FormLabel>
            <FormControl>
              <Input placeholder={t("apiKeys.namePlaceholder")} {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="environment"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("apiKeys.environment")}</FormLabel>
            <FormControl>
              <PresetField
                presets={ENVIRONMENTS}
                value={field.value ?? ""}
                onChange={field.onChange}
              >
                {({ value, onChange }) => (
                  <Input
                    value={value}
                    onChange={(event) => onChange(event.target.value)}
                    placeholder="production"
                    dir="ltr"
                  />
                )}
              </PresetField>
            </FormControl>
            <FormDescription>{t("apiKeys.environmentHint")}</FormDescription>
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
              <Input placeholder={t("apiKeys.descriptionPlaceholder")} {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="rateLimitPerMinute"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("apiKeys.rateLimitPerMinute")}</FormLabel>
            <FormControl>
              <PresetField
                presets={RATE_PER_MINUTE}
                value={field.value ?? ""}
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
              {t("apiKeys.rateLimitPerMinuteHint")}
            </FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="rateLimitPerDay"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("apiKeys.rateLimitPerDay")}</FormLabel>
            <FormControl>
              <PresetField
                presets={RATE_PER_DAY}
                value={field.value ?? ""}
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
            <FormDescription>{t("apiKeys.rateLimitPerDayHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="expiresAt"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.expiresAt")}</FormLabel>
            <FormControl>
              <DatePicker
                value={field.value}
                onChange={(value) => field.onChange(value ?? "")}
                minDate={new Date()}
                maxDate={monthsFromNow(10)}
                placeholder={t("common.never")}
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </FormDialog>
  )
}
