import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { ApplicationSelect } from "@/components/common/application-select"
import { FormDialog } from "@/components/common/form-dialog"
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { Textarea } from "@/components/ui/textarea"
import { api } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"
import type { Schemas } from "@/lib/api/types"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

export function PermissionFormDialog({
  open,
  onOpenChange,
  permission,
  defaultApplicationId,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  permission?: Schemas["PermissionDto"]
  defaultApplicationId?: string
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const isEdit = Boolean(permission)

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
      applicationId: permission?.applicationId ?? defaultApplicationId ?? "",
      code: permission?.code ?? "",
      name: permission?.name ?? "",
      description: permission?.description ?? "",
    })
  }, [open, permission, defaultApplicationId, form])

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      if (isEdit && permission?.id) {
        const { error } = await api.PUT("/api/v1/Permissions/{id}", {
          params: { path: { id: permission.id } },
          body: {
            name: values.name,
            description: emptyToNull(values.description),
          },
        })
        if (error) throw error
        return
      }
      const { error } = await api.POST("/api/v1/Permissions", {
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
      void queryClient.invalidateQueries({ queryKey: ["permissions"] })
      toast.success(
        isEdit ? t("permissions.updated") : t("permissions.created")
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
      title={isEdit ? t("permissions.editTitle") : t("permissions.createTitle")}
      description={t("permissions.subtitle")}
      formId="permission-form"
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
                  <Input placeholder="resource:action" {...field} />
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
    </FormDialog>
  )
}
