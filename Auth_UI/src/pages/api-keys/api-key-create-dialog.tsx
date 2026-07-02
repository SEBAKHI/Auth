import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { FormDialog } from "@/components/common/form-dialog"
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { api } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

function toIntOrNull(value: string | undefined): number | null {
  if (!value || value.trim().length === 0) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

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
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      environment: "production",
      description: "",
      rateLimitPerMinute: "",
      rateLimitPerDay: "",
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
          rateLimitPerMinute: toIntOrNull(values.rateLimitPerMinute),
          rateLimitPerDay: toIntOrNull(values.rateLimitPerDay),
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
              <Input {...field} />
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
              <Input {...field} />
            </FormControl>
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
              <Input {...field} />
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
              <Input type="number" min={1} {...field} />
            </FormControl>
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
              <Input type="number" min={1} {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </FormDialog>
  )
}
