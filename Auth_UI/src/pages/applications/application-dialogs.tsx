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
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { api } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"
import type { Schemas } from "@/lib/api/types"

const CONTENT_CLASS = "max-h-[90svh] overflow-y-auto sm:max-w-lg"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

type ToggleName =
  | "allowSelfRegistration"
  | "requireTwoFactor"
  | "requireEmailVerification"

function useToggles(): { name: ToggleName; label: string }[] {
  const { t } = useTranslation()
  return [
    {
      name: "allowSelfRegistration",
      label: t("applications.allowSelfRegistration"),
    },
    { name: "requireTwoFactor", label: t("applications.requireTwoFactor") },
    {
      name: "requireEmailVerification",
      label: t("applications.requireEmailVerification"),
    },
  ]
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

  const schema = z.object({
    code: z.string().min(1, t("validation.required")),
    name: z.string().min(1, t("validation.required")),
    description: z.string().optional(),
    baseUrl: z.string().optional(),
    contactEmail: z.string().optional(),
    allowSelfRegistration: z.boolean(),
    requireTwoFactor: z.boolean(),
    requireEmailVerification: z.boolean(),
    sessionTimeoutMinutes: z.string().min(1, t("validation.required")),
    maxConcurrentSessions: z.string().min(1, t("validation.required")),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: "",
      name: "",
      description: "",
      baseUrl: "",
      contactEmail: "",
      allowSelfRegistration: false,
      requireTwoFactor: false,
      requireEmailVerification: false,
      sessionTimeoutMinutes: "60",
      maxConcurrentSessions: "5",
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
          contactEmail: emptyToNull(values.contactEmail),
          allowSelfRegistration: values.allowSelfRegistration,
          requireTwoFactor: values.requireTwoFactor,
          requireEmailVerification: values.requireEmailVerification,
          sessionTimeoutMinutes: Number(values.sessionTimeoutMinutes) || 60,
          maxConcurrentSessions: Number(values.maxConcurrentSessions) || 5,
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
      contentClassName={CONTENT_CLASS}
    >
      <FormField
        control={form.control}
        name="code"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.code")}</FormLabel>
            <FormControl>
              <Input {...field} />
            </FormControl>
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
              <Textarea rows={2} {...field} />
            </FormControl>
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
              <Input {...field} />
            </FormControl>
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
              <Input type="email" {...field} />
            </FormControl>
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
              <Input type="number" min={1} {...field} />
            </FormControl>
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
              <Input type="number" min={1} {...field} />
            </FormControl>
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
            <FormItem orientation="horizontal" className="rounded-lg border p-3">
              <FormLabel className="font-normal">{item.label}</FormLabel>
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

  const schema = z.object({
    name: z.string().min(1, t("validation.required")),
    description: z.string().optional(),
    baseUrl: z.string().optional(),
    contactEmail: z.string().optional(),
    allowSelfRegistration: z.boolean(),
    requireTwoFactor: z.boolean(),
    requireEmailVerification: z.boolean(),
    sessionTimeoutMinutes: z.string().min(1, t("validation.required")),
    maxConcurrentSessions: z.string().min(1, t("validation.required")),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      description: "",
      baseUrl: "",
      contactEmail: "",
      allowSelfRegistration: false,
      requireTwoFactor: false,
      requireEmailVerification: false,
      sessionTimeoutMinutes: "60",
      maxConcurrentSessions: "5",
    },
  })

  React.useEffect(() => {
    if (!open) return
    form.reset({
      name: application.name ?? "",
      description: application.description ?? "",
      baseUrl: application.baseUrl ?? "",
      contactEmail: application.contactEmail ?? "",
      allowSelfRegistration: application.allowSelfRegistration ?? false,
      requireTwoFactor: application.requireTwoFactor ?? false,
      requireEmailVerification: application.requireEmailVerification ?? false,
      sessionTimeoutMinutes: String(application.sessionTimeoutMinutes ?? 60),
      maxConcurrentSessions: String(application.maxConcurrentSessions ?? 5),
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
          logoUrl: application.logoUrl ?? null,
          contactEmail: emptyToNull(values.contactEmail),
          allowSelfRegistration: values.allowSelfRegistration,
          requireTwoFactor: values.requireTwoFactor,
          requireEmailVerification: values.requireEmailVerification,
          sessionTimeoutMinutes: Number(values.sessionTimeoutMinutes) || 60,
          maxConcurrentSessions: Number(values.maxConcurrentSessions) || 5,
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
      contentClassName={CONTENT_CLASS}
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
        name="description"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.description")}</FormLabel>
            <FormControl>
              <Textarea rows={2} {...field} />
            </FormControl>
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
              <Input {...field} />
            </FormControl>
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
              <Input type="email" {...field} />
            </FormControl>
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
              <Input type="number" min={1} {...field} />
            </FormControl>
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
              <Input type="number" min={1} {...field} />
            </FormControl>
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
            <FormItem orientation="horizontal" className="rounded-lg border p-3">
              <FormLabel className="font-normal">{item.label}</FormLabel>
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
