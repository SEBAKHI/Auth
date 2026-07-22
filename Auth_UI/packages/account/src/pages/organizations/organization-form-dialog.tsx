import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { FormDialog } from "@astoom/ui/common/form-dialog"
import {
  FormControl,
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

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

export function OrganizationFormDialog({
  open,
  onOpenChange,
  organization,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  organization?: Schemas["OrganizationDetailDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const isEdit = Boolean(organization)

  const schema = z.object({
    code: z.string().min(1, t("validation.required")),
    name: z.string().min(1, t("validation.required")),
    contactEmail: z
      .string()
      .min(1, t("validation.required"))
      .regex(EMAIL_RE, t("validation.email")),
    website: z.string().optional(),
    logoUrl: z.string().optional(),
    description: z.string().optional(),
    isActive: z.boolean(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: "",
      name: "",
      contactEmail: "",
      website: "",
      logoUrl: "",
      description: "",
      isActive: true,
    },
  })

  React.useEffect(() => {
    if (!open) return
    form.reset({
      code: organization?.code ?? "",
      name: organization?.name ?? "",
      contactEmail: organization?.contactEmail ?? "",
      website: organization?.website ?? "",
      logoUrl: organization?.logoUrl ?? "",
      description: organization?.description ?? "",
      isActive: organization?.isActive ?? true,
    })
  }, [open, organization, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      if (isEdit && organization?.id) {
        const { error } = await api.PUT("/api/v1/Organizations/{id}", {
          params: { path: { id: organization.id } },
          body: {
            name: values.name,
            contactEmail: values.contactEmail,
            website: emptyToNull(values.website),
            logoUrl: emptyToNull(values.logoUrl),
            description: emptyToNull(values.description),
            isActive: values.isActive,
          },
        })
        if (error) throw error
        return
      }
      const { error } = await api.POST("/api/v1/Organizations", {
        body: {
          code: values.code,
          name: values.name,
          contactEmail: values.contactEmail,
          website: emptyToNull(values.website),
          logoUrl: emptyToNull(values.logoUrl),
          description: emptyToNull(values.description),
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["organizations"] })
      void queryClient.invalidateQueries({ queryKey: ["organizations-all"] })
      toast.success(
        isEdit ? t("organizations.updated") : t("organizations.created")
      )
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={
        isEdit ? t("organizations.editTitle") : t("organizations.createTitle")
      }
      description={t("organizations.subtitle")}
      formId="organization-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={isEdit ? t("common.save") : t("common.create")}
      pending={mutation.isPending}
    >
      {!isEdit ? (
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
      ) : null}
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
        name="contactEmail"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("organizations.contactEmail")}</FormLabel>
            <FormControl>
              <Input type="email" {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="website"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("organizations.website")}</FormLabel>
            <FormControl>
              <Input {...field} />
            </FormControl>
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
      {isEdit ? (
        <FormField
          control={form.control}
          name="isActive"
          render={({ field }) => (
            <FormItem
              orientation="horizontal"
              className="rounded-lg border p-3"
            >
              <FormLabel className="font-normal">
                {t("common.active")}
              </FormLabel>
              <FormControl>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                />
              </FormControl>
            </FormItem>
          )}
        />
      ) : null}
    </FormDialog>
  )
}
