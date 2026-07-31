import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { ApplicationSelect } from "@astoom/ui/common/application-select"
import { FormDialog } from "@astoom/ui/common/form-dialog"
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { Input } from "@astoom/ui/input"
import { Textarea } from "@astoom/ui/textarea"
import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import type { Schemas } from "@astoom/api/types"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

export function RoleFormDialog({
  open,
  onOpenChange,
  role,
  defaultApplicationId,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  role?: Schemas["RoleDto"]
  defaultApplicationId?: string
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const isEdit = Boolean(role)

  // In edit mode the API only updates name/description and the
  // application/code fields are not rendered, so they are not required.
  const schema = z.object({
    applicationId: isEdit
      ? z.string()
      : z.string().min(1, t("validation.required")),
    code: isEdit ? z.string() : z.string().min(1, t("validation.required")),
    name: z.string().min(1, t("validation.required")),
    description: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      applicationId: defaultApplicationId ?? "",
      code: "",
      name: "",
      description: "",
    },
  })

  React.useEffect(() => {
    if (!open) return
    form.reset({
      applicationId: role?.applicationId ?? defaultApplicationId ?? "",
      code: role?.code ?? "",
      name: role?.name ?? "",
      description: role?.description ?? "",
    })
  }, [open, role, defaultApplicationId, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      if (isEdit && role?.id) {
        const { error } = await api.PUT("/api/v1/Roles/{id}", {
          params: { path: { id: role.id } },
          body: {
            name: values.name,
            description: emptyToNull(values.description),
          },
        })
        if (error) throw error
        return
      }
      const { error } = await api.POST("/api/v1/Roles", {
        body: {
          applicationId: values.applicationId,
          code: values.code,
          name: values.name,
          description: emptyToNull(values.description),
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["roles"] })
      toast.success(isEdit ? t("roles.updated") : t("roles.created"))
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={isEdit ? t("roles.editTitle") : t("roles.createTitle")}
      description={t("roles.subtitle")}
      formId="role-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={isEdit ? t("common.save") : t("common.create")}
      pending={mutation.isPending}
    >
      {!isEdit ? (
        <>
          <FormField
            control={form.control}
            name="applicationId"
            render={({ field }) => (
              <FormItem>
                <FormLabel>{t("common.application")}</FormLabel>
                <FormControl>
                  <ApplicationSelect
                    value={field.value || undefined}
                    onChange={(value) => field.onChange(value ?? "")}
                    className="w-full"
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="code"
            render={({ field }) => (
              <FormItem>
                <FormLabel>{t("common.code")}</FormLabel>
                <FormControl>
                  <Input placeholder="support-agent" dir="ltr" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
        </>
      ) : null}
      <FormField
        control={form.control}
        name="name"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("common.name")}</FormLabel>
            <FormControl>
              <Input placeholder={t("roles.namePlaceholder")} {...field} />
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
              <Textarea rows={2} placeholder={t("roles.descriptionPlaceholder")} {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </FormDialog>
  )
}
