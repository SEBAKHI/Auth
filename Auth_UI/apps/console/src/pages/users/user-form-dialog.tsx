import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { CircleAlert, RotateCw } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { FormDialog } from "@authsystem/ui/common/form-dialog"
import { LanguageSelect } from "@authsystem/ui/common/language-select"
import { TimeZoneSelect } from "@authsystem/ui/common/timezone-select"
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"
import { api } from "@authsystem/api/client"
import {
  getErrorFeedback,
  getFieldErrors,
  type ApiErrorFeedback,
} from "@authsystem/api/errors"
import { usePasswordPolicy } from "@authsystem/api/password-policy"
import type { Schemas } from "@authsystem/api/types"
import { PasswordField } from "@authsystem/auth/password-field"
import {
  applyPasswordServerErrors,
  passwordIssue,
} from "@authsystem/auth/password-rules"

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
  const { policy } = usePasswordPolicy()

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
            const passwordMessage = passwordIssue(values.password ?? "", policy)
            if (passwordMessage) {
              ctx.addIssue({
                code: "custom",
                path: ["password"],
                message: passwordMessage,
              })
            }
          }
        }),
    [isEdit, policy, t]
  )

  type Values = z.infer<typeof schema>
  type FormFailure = { feedback: ApiErrorFeedback; values: Values }

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
  const [formFailure, setFormFailure] = React.useState<FormFailure | null>(null)

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

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      if (isEdit && user?.id) {
        const { error } = await api.PUT("/api/v1/Users/{id}", {
          params: { path: { id: user.id } },
          body: {
            firstName: values.firstName,
            lastName: values.lastName,
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
      setFormFailure(null)
      onOpenChange(false)
    },
    onError: (error, values) => {
      // A refused password is a domain rule, not a field name, so
      // getFieldErrors below would never place it; put every reason under the
      // control instead of one sentence in the alert.
      if (!isEdit && applyPasswordServerErrors(form, "password", error)) {
        setFormFailure(null)
        return
      }
      const fieldErrors = getFieldErrors(error)
      const availableFields: ReadonlyArray<keyof Values> = isEdit
        ? [
            "firstName",
            "lastName",
            "phoneNumber",
            "preferredLanguage",
            "timeZone",
          ]
        : [
            "email",
            "password",
            "firstName",
            "lastName",
            "phoneNumber",
            "preferredLanguage",
            "timeZone",
          ]
      const invalidFields = availableFields.filter(
        (field) => fieldErrors[field]
      )

      if (invalidFields.length > 0) {
        setFormFailure(null)
        for (const field of invalidFields) {
          form.setError(field, { type: "server", message: fieldErrors[field] })
        }
        form.setFocus(invalidFields[0])
        return
      }

      setFormFailure({ feedback: getErrorFeedback(error), values })
    },
  })

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) setFormFailure(null)
    onOpenChange(nextOpen)
  }

  return (
    <FormDialog
      open={open}
      onOpenChange={handleOpenChange}
      form={form}
      title={isEdit ? t("users.editTitle") : t("users.createTitle")}
      description={t("users.subtitle")}
      formId="user-form"
      onSubmit={(values) => {
        setFormFailure(null)
        mutation.mutate(values)
      }}
      submitLabel={isEdit ? t("common.save") : t("common.create")}
      pending={mutation.isPending}
      size="xl"
    >
      {formFailure ? (
        <Alert variant="destructive">
          <CircleAlert />
          <AlertTitle>{formFailure.feedback.title}</AlertTitle>
          <AlertDescription>
            <p>{formFailure.feedback.description}</p>
            {formFailure.feedback.retryable ? (
              <Button
                type="button"
                size="sm"
                variant="outline"
                disabled={mutation.isPending}
                onClick={() => mutation.mutate(formFailure.values)}
              >
                {mutation.isPending ? (
                  <Spinner data-icon="inline-start" aria-hidden="true" />
                ) : (
                  <RotateCw data-icon="inline-start" />
                )}
                {formFailure.feedback.actionLabel}
              </Button>
            ) : null}
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-7 sm:grid-cols-2">
        {!isEdit ? (
          <>
            <FormField
              control={form.control}
              name="email"
              render={({ field, fieldState }) => (
                <FormItem data-invalid={fieldState.invalid}>
                  <FormLabel>{t("common.email")}</FormLabel>
                  <FormControl>
                    <Input
                      type="email"
                      placeholder="name@example.com"
                      dir="ltr"
                      {...field}
                    />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <PasswordField
              control={form.control}
              name="password"
              label={t("users.password")}
            />
          </>
        ) : null}

        <FormField
          control={form.control}
          name="firstName"
          render={({ field, fieldState }) => (
            <FormItem data-invalid={fieldState.invalid}>
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
          render={({ field, fieldState }) => (
            <FormItem data-invalid={fieldState.invalid}>
              <FormLabel>{t("users.lastName")}</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="phoneNumber"
          render={({ field, fieldState }) => (
            <FormItem data-invalid={fieldState.invalid}>
              <FormLabel>{t("users.phoneNumber")}</FormLabel>
              <FormControl>
                <Input placeholder="+966 50 000 0000" dir="ltr" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="preferredLanguage"
          render={({ field, fieldState }) => (
            <FormItem data-invalid={fieldState.invalid}>
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
          render={({ field, fieldState }) => (
            <FormItem data-invalid={fieldState.invalid}>
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
