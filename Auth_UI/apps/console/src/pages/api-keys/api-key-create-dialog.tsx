import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { FormDialog } from "@authsystem/ui/common/form-dialog"
import { DatePicker, monthsFromNow } from "@authsystem/ui/common/date-picker"
import { PresetField } from "@authsystem/ui/common/preset-field"
import { ENVIRONMENTS } from "@/lib/presets"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Checkbox } from "@authsystem/ui/checkbox"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldGroup,
  FieldLabel,
} from "@authsystem/ui/field"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { Textarea } from "@authsystem/ui/textarea"
import { api } from "@authsystem/api/client"
import { getErrorMessage, getFieldErrors } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"

type PermissionDto = Schemas["PermissionDto"]

const DIALOG_ID = "api-key-create"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

/** One address or range per line; blank lines dropped. None means any address. */
function toAddressList(value: string | undefined): string[] | null {
  const entries = (value ?? "")
    .split("\n")
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0)
  return entries.length > 0 ? entries : null
}

/**
 * The scopes a new key may carry, one checkbox each: the application's own
 * active permissions that this administrator may grant (OI-128 PR B). The
 * filter is the role dialog's, which is the rule the server applies here as
 * well: a platform code, another application's, an inactive one, or one the
 * creator does not hold is refused after the choice, so it is not offered.
 */
function ApiKeyScopesChoice({
  permissions,
  value,
  onChange,
}: {
  permissions: PermissionDto[]
  value: string[]
  onChange: (value: string[]) => void
}) {
  const toggle = (id: string, checked: boolean) =>
    onChange(
      permissions
        .map((permission) => permission.id as string)
        .filter((candidate) =>
          candidate === id ? checked : value.includes(candidate)
        )
    )

  return (
    <FieldGroup
      data-slot="checkbox-group"
      role="group"
      aria-labelledby={`${DIALOG_ID}-scope-label`}
    >
      {permissions.map((permission) => {
        const id = permission.id as string
        return (
          <Field key={id} orientation="horizontal">
            <Checkbox
              id={`${DIALOG_ID}-scope-${id}`}
              checked={value.includes(id)}
              onCheckedChange={(checked) => toggle(id, checked === true)}
            />
            <FieldContent>
              <FieldLabel
                htmlFor={`${DIALOG_ID}-scope-${id}`}
                className="font-normal"
              >
                {permission.code}
              </FieldLabel>
              {permission.name ? (
                <FieldDescription>{permission.name}</FieldDescription>
              ) : null}
            </FieldContent>
          </Field>
        )
      })}
    </FieldGroup>
  )
}

/**
 * Creates an API key. Mounted afresh for every opening (the page keys it to an
 * open counter), so the form's defaults are read once, at mount, and nothing
 * resets it in an effect.
 */
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

  const permissionsQuery = useQuery({
    queryKey: ["permissions", "all"],
    enabled: open,
    queryFn: () =>
      unwrap(api.GET("/api/v1/Permissions", { params: { query: {} } })),
  })

  const grantable = React.useMemo(
    () =>
      (permissionsQuery.data ?? []).filter(
        (permission) =>
          permission.id &&
          (permission.applicationId ?? null) === applicationId &&
          permission.isActive &&
          permission.canGrant !== false
      ),
    [permissionsQuery.data, applicationId]
  )

  const schema = z.object({
    name: z.string().min(1, t("validation.required")),
    environment: z.string().optional(),
    description: z.string().optional(),
    permissionIds: z.array(z.string()).min(1, t("apiKeys.scopesRequired")),
    allowedIps: z.string().optional(),
    expiresAt: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      environment: "production",
      description: "",
      permissionIds: [],
      allowedIps: "",
      expiresAt: "",
    },
  })

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const { data, error } = await api.POST("/api/v1/ApiKeys", {
        body: {
          applicationId,
          name: values.name,
          description: emptyToNull(values.description),
          environment: emptyToNull(values.environment),
          permissionIds: values.permissionIds,
          allowedIps: toAddressList(values.allowedIps),
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
    onError: (error) => {
      // The server's sentence under the field it concerns, when it names one.
      const fieldErrors = getFieldErrors(error)
      for (const field of ["permissionIds", "allowedIps"] as const) {
        if (fieldErrors[field]) {
          form.setError(field, { message: getErrorMessage(error) })
        }
      }
      toast.error(getErrorMessage(error))
    },
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
              <Input
                placeholder={t("apiKeys.descriptionPlaceholder")}
                {...field}
              />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="permissionIds"
        render={({ field }) => (
          <FormItem>
            <FormLabel id={`${DIALOG_ID}-scope-label`}>
              {t("apiKeys.scopes")}
            </FormLabel>
            {permissionsQuery.isSuccess && grantable.length === 0 ? (
              <Alert>
                <AlertDescription>
                  {t("apiKeys.noGrantableScopes")}
                </AlertDescription>
              </Alert>
            ) : (
              <FormControl>
                <ApiKeyScopesChoice
                  permissions={grantable}
                  value={field.value}
                  onChange={field.onChange}
                />
              </FormControl>
            )}
            <FormDescription>{t("apiKeys.scopesHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
      <FormField
        control={form.control}
        name="allowedIps"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("apiKeys.allowedIps")}</FormLabel>
            <FormControl>
              <Textarea
                rows={3}
                placeholder={"203.0.113.0/24\n2001:db8::1"}
                dir="ltr"
                {...field}
              />
            </FormControl>
            <FormDescription>{t("apiKeys.allowedIpsHint")}</FormDescription>
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
