import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { FormDialog } from "@/components/common/form-dialog"
import { LanguageSelect } from "@/components/common/language-select"
import { TimeZoneSelect } from "@/components/common/timezone-select"
import { Field, FieldLabel } from "@/components/ui/field"
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
import { fullName } from "@/lib/format"
import type { Schemas } from "@/lib/api/types"

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

export function UserFormDialog({
  open,
  onOpenChange,
  user,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  user?: Schemas["UserDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const isEdit = Boolean(user)

  const schema = React.useMemo(
    () =>
      z
        .object({
          email: z.string().optional(),
          password: z.string().optional(),
          firstName: z.string().min(1, t("validation.required")),
          lastName: z.string().min(1, t("validation.required")),
          phoneNumber: z.string().optional(),
          preferredLanguage: z.string().optional(),
          timeZone: z.string().optional(),
        })
        .superRefine((values, ctx) => {
          if (!isEdit) {
            if (!values.email || !EMAIL_RE.test(values.email)) {
              ctx.addIssue({
                code: "custom",
                path: ["email"],
                message: t("validation.email"),
              })
            }
            if (!values.password || values.password.length < 8) {
              ctx.addIssue({
                code: "custom",
                path: ["password"],
                message: t("validation.minLength", { count: 8 }),
              })
            }
          }
        }),
    [isEdit, t]
  )

  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: "",
      password: "",
      firstName: "",
      lastName: "",
      phoneNumber: "",
      preferredLanguage: "",
      timeZone: "",
    },
  })

  // Reset the form when opening for a different record.
  React.useEffect(() => {
    if (!open) return
    form.reset({
      email: user?.email ?? "",
      password: "",
      firstName: user?.firstName ?? "",
      lastName: user?.lastName ?? "",
      phoneNumber: user?.phoneNumber ?? "",
      preferredLanguage: user?.preferredLanguage ?? "",
      timeZone: user?.timeZone ?? "",
    })
  }, [open, user, form])

  // Display name is always the live combination of first + last name.
  const displayName = fullName(form.watch("firstName"), form.watch("lastName"))

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const combinedDisplayName = emptyToNull(
        fullName(values.firstName, values.lastName)
      )
      if (isEdit && user?.id) {
        const { error } = await api.PUT("/api/v1/Users/{id}", {
          params: { path: { id: user.id } },
          body: {
            firstName: values.firstName,
            lastName: values.lastName,
            displayName: combinedDisplayName,
            phoneNumber: emptyToNull(values.phoneNumber),
            preferredLanguage: emptyToNull(values.preferredLanguage),
            timeZone: emptyToNull(values.timeZone),
          },
        })
        if (error) throw error
        return
      }
      const { error } = await api.POST("/api/v1/Users", {
        body: {
          email: values.email ?? "",
          password: values.password ?? "",
          firstName: values.firstName,
          lastName: values.lastName,
          displayName: combinedDisplayName,
          phoneNumber: emptyToNull(values.phoneNumber),
          preferredLanguage: emptyToNull(values.preferredLanguage),
          timeZone: emptyToNull(values.timeZone),
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["users"] })
      toast.success(isEdit ? t("users.updated") : t("users.created"))
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      form={form}
      title={isEdit ? t("users.editTitle") : t("users.createTitle")}
      description={t("users.subtitle")}
      formId="user-form"
      onSubmit={(values) => mutation.mutate(values)}
      submitLabel={isEdit ? t("common.save") : t("common.create")}
      pending={mutation.isPending}
      contentClassName="sm:max-w-2xl"
    >
      <div className="grid gap-7 sm:grid-cols-2">
        {!isEdit ? (
          <>
            <FormField
              control={form.control}
              name="email"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("common.email")}</FormLabel>
                  <FormControl>
                    <Input type="email" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="password"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>{t("users.password")}</FormLabel>
                  <FormControl>
                    <Input
                      type="password"
                      autoComplete="new-password"
                      {...field}
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
          name="firstName"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("users.firstName")}</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="lastName"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("users.lastName")}</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <Field>
          <FieldLabel htmlFor="user-display-name">
            {t("users.displayName")}
          </FieldLabel>
          <Input id="user-display-name" value={displayName} readOnly disabled />
        </Field>
        <FormField
          control={form.control}
          name="phoneNumber"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("users.phoneNumber")}</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="preferredLanguage"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("users.preferredLanguage")}</FormLabel>
              <FormControl>
                <LanguageSelect
                  value={field.value}
                  onChange={field.onChange}
                  className="w-full"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="timeZone"
          render={({ field }) => (
            <FormItem>
              <FormLabel>{t("users.timeZone")}</FormLabel>
              <FormControl>
                <TimeZoneSelect value={field.value} onChange={field.onChange} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
      </div>
    </FormDialog>
  )
}
