import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { FormDialog } from "@authsystem/ui/common/form-dialog"
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { Textarea } from "@authsystem/ui/textarea"
import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

export function RoleFormDialog({
  open,
  onOpenChange,
  role,
  defaultApplicationId,
  lockedApplication,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  role?: Schemas["RoleDto"]
  defaultApplicationId?: string
  /**
   * Opened from inside an application: the record is created there, and the
   * picker shows that application without letting it change. The name comes
   * with it so the picker does not depend on the shared list holding it.
   */
  lockedApplication?: { id: string; name?: string | null }
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const isEdit = Boolean(role)

  // In edit mode the API only updates name/description and the
  // application/code fields are not rendered, so they are not required.
  const schema = z.object({
    // Optional: an empty value is the platform's own scope, which is where
    // every seeded role lives and the only scope that exists at all until an
    // application is registered.
    applicationId: z.string(),
    code: isEdit ? z.string() : z.string().min(1, t("validation.required")),
    name: z.string().min(1, t("validation.required")),
    description: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      applicationId: lockedApplication?.id ?? defaultApplicationId ?? "",
      code: "",
      name: "",
      description: "",
    },
  })

  React.useEffect(() => {
    if (!open) return
    form.reset({
      applicationId:
        role?.applicationId ??
        lockedApplication?.id ??
        defaultApplicationId ??
        "",
      code: role?.code ?? "",
      name: role?.name ?? "",
      description: role?.description ?? "",
    })
  }, [open, role, lockedApplication?.id, defaultApplicationId, form])

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
          // Empty means the platform itself; "" would fail binding on a Guid?.
          applicationId: emptyToNull(values.applicationId),
          code: values.code,
          name: values.name,
          description: emptyToNull(values.description),
        },
      })
      if (error) throw error
    },
    onSuccess: (_data, values) => {
      void queryClient.invalidateQueries({ queryKey: ["roles"] })
      // An application's own tab reads its roles under its own key, so the
      // main list's key alone left that tab stale until a reload.
      const applicationId = role?.applicationId ?? values.applicationId
      if (applicationId) {
        void queryClient.invalidateQueries({
          queryKey: ["applications", applicationId, "roles"],
        })
      }
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
                    allowPlatform={!lockedApplication}
                    options={
                      lockedApplication
                        ? [
                            {
                              id: lockedApplication.id,
                              name: lockedApplication.name ?? undefined,
                            },
                          ]
                        : undefined
                    }
                    disabled={Boolean(lockedApplication)}
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
                  <Input
                    placeholder="support-agent"
                    dir="ltr"
                    autoCapitalize="none"
                    autoCorrect="off"
                    spellCheck={false}
                    {...field}
                    onChange={(event) => {
                      // Role codes are stored lowercase, so the field shows
                      // what will be saved. Codes are ASCII, so toLowerCase
                      // keeps every offset and the caret stays where it was.
                      // Left alone mid-composition: rewriting an IME's text
                      // breaks it, and the server lowercases anyway.
                      const input = event.currentTarget
                      const lowered = input.value.toLowerCase()
                      const composing = (event.nativeEvent as InputEvent)
                        .isComposing
                      if (lowered !== input.value && !composing) {
                        const { selectionStart, selectionEnd } = input
                        input.value = lowered
                        input.setSelectionRange(selectionStart, selectionEnd)
                      }
                      field.onChange(input.value)
                    }}
                  />
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
