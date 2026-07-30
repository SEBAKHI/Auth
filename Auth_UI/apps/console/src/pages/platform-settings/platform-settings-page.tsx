import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { LogoAvatar } from "@astoom/ui/common/logo-avatar"
import { Spinner } from "@astoom/ui/spinner"
import { PageHeader } from "@astoom/ui/common/page-header"
import { Button } from "@astoom/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@astoom/ui/card"
import { FieldGroup } from "@astoom/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@astoom/ui/form"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { BRANDING_QUERY_KEY } from "@astoom/ui/branding"
import { getErrorMessage } from "@astoom/api/errors"
import type { Schemas } from "@astoom/api/types"

const SETTINGS_QUERY_KEY = ["platform-settings"] as const

function SettingsCard({ settings }: { settings: Schemas["PlatformSettingsDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const invalidate = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: SETTINGS_QUERY_KEY })
    void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
  }, [queryClient])

  const schema = z.object({
    platformName: z.string().min(1, t("validation.required")),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { platformName: settings.platformName ?? "" },
  })

  const saveName = useMutation({
    mutationFn: async (values: Values) => {
      const { error } = await api.PUT("/api/v1/admin/platform-settings", {
        body: {
          platformName: values.platformName,
          logoUrl: settings.logoUrl ?? null,
          logoUrlDark: settings.logoUrlDark ?? null,
          faviconUrl: settings.faviconUrl ?? null,
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("platformSettings.updated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  // Logo changes persist immediately (same flow as organization logos).
  const persistLogoLight = React.useCallback(
    async (logoKey: string | null) => {
      const { error } = await api.PUT("/api/v1/admin/platform-settings", {
        body: {
          platformName: settings.platformName ?? "",
          logoUrl: logoKey,
          logoUrlDark: settings.logoUrlDark ?? null,
          faviconUrl: settings.faviconUrl ?? null,
        },
      })
      if (error) throw error
    },
    [settings.platformName, settings.logoUrlDark, settings.faviconUrl]
  )

  const persistLogoDark = React.useCallback(
    async (logoKey: string | null) => {
      const { error } = await api.PUT("/api/v1/admin/platform-settings", {
        body: {
          platformName: settings.platformName ?? "",
          logoUrl: settings.logoUrl ?? null,
          logoUrlDark: logoKey,
          faviconUrl: settings.faviconUrl ?? null,
        },
      })
      if (error) throw error
    },
    [settings.platformName, settings.logoUrl, settings.faviconUrl]
  )

  const persistFavicon = React.useCallback(
    async (logoKey: string | null) => {
      const { error } = await api.PUT("/api/v1/admin/platform-settings", {
        body: {
          platformName: settings.platformName ?? "",
          logoUrl: settings.logoUrl ?? null,
          logoUrlDark: settings.logoUrlDark ?? null,
          faviconUrl: logoKey,
        },
      })
      if (error) throw error
    },
    [settings.platformName, settings.logoUrl, settings.logoUrlDark]
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("platformSettings.brandingTitle")}</CardTitle>
        <CardDescription>
          {t("platformSettings.brandingSubtitle")}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div className="mb-6 flex items-center gap-6">
          <div className="flex flex-col items-center gap-1.5">
            <LogoAvatar
              src={settings.logoUrl}
              name={settings.platformName}
              canEdit
              persist={persistLogoLight}
              invalidate={invalidate}
              successMessage={t("platformSettings.updated")}
            />
            <p className="text-xs text-muted-foreground">
              {t("platformSettings.logoLight")}
            </p>
          </div>
          <div className="flex flex-col items-center gap-1.5">
            <LogoAvatar
              src={settings.logoUrlDark}
              name={settings.platformName}
              canEdit
              persist={persistLogoDark}
              invalidate={invalidate}
              successMessage={t("platformSettings.updated")}
            />
            <p className="text-xs text-muted-foreground">
              {t("platformSettings.logoDark")}
            </p>
          </div>
          <div className="flex flex-col items-center gap-1.5">
            <LogoAvatar
              src={settings.faviconUrl}
              name={settings.platformName}
              canEdit
              persist={persistFavicon}
              invalidate={invalidate}
              successMessage={t("platformSettings.updated")}
            />
            <p className="text-xs text-muted-foreground">
              {t("platformSettings.favicon")}
            </p>
          </div>
          <div className="min-w-0">
            <p className="truncate font-medium">{settings.platformName}</p>
            <p className="truncate text-sm text-muted-foreground">
              {t("platformSettings.logoHint")}
            </p>
          </div>
        </div>
        <Form {...form}>
          <form onSubmit={form.handleSubmit((values) => saveName.mutate(values))}>
            <FieldGroup>
              <FormField
                control={form.control}
                name="platformName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("platformSettings.platformName")}</FormLabel>
                    <FormControl>
                      <Input placeholder="Acme" {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <Button type="submit" className="w-fit" disabled={saveName.isPending}>
                {saveName.isPending ? <Spinner /> : null}
                {t("common.save")}
              </Button>
            </FieldGroup>
          </form>
        </Form>
      </CardContent>
    </Card>
  )
}

export function PlatformSettingsPage() {
  const { t } = useTranslation()

  const query = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/admin/platform-settings")),
  })

  return (
    <div className="space-y-6">
      <PageHeader
        title={t("platformSettings.title")}
        description={t("platformSettings.subtitle")}
      />
      {query.isLoading || !query.data ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <SettingsCard settings={query.data} />
      )}
    </div>
  )
}
