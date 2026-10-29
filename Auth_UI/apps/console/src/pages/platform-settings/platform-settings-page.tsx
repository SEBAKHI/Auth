import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { LogoAvatar } from "@authsystem/ui/common/logo-avatar"
import { ThemedLogo } from "@authsystem/ui/common/themed-logo"
import { Spinner } from "@authsystem/ui/spinner"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import { FieldGroup } from "@authsystem/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { Input } from "@authsystem/ui/input"
import { useSaveQueue } from "@authsystem/ui/hooks/use-save-queue"
import { Skeleton } from "@authsystem/ui/skeleton"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"

import { AppearanceCard } from "./appearance-card"

const SETTINGS_QUERY_KEY = ["platform-settings"] as const

function SettingsCard({ settings }: { settings: Schemas["PlatformSettingsDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  /**
   * The update endpoint returns the saved settings, so every write below feeds
   * the response straight into the cache instead of invalidating and fetching
   * the same row again.
   *
   * That refetch was not free: /admin/** is one rate-limit bucket at the
   * gateway, so each save cost two permits, and five logo changes in a minute
   * were enough to get an administrator throttled out of their own console.
   */
  const applySaved = React.useCallback(
    (saved: Schemas["PlatformSettingsDto"] | undefined) => {
      if (saved) queryClient.setQueryData(SETTINGS_QUERY_KEY, saved)
    },
    [queryClient]
  )

  /**
   * Public branding genuinely does need a refetch: different projection,
   * different route — and that route sits on the roomier "api" bucket, not the
   * admin one, so it is not what was costing an administrator their quota.
   */
  const refreshBranding = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
  }, [queryClient])

  /**
   * Saves the branding: `changes` over the settings as stored when the save is
   * SENT, not as this card rendered them. Every save resends the other fields,
   * and one that started earlier (a logo still uploading) would otherwise send
   * back what this card showed before it, undoing a change made meanwhile. The
   * saves run one after another, and each writes its answer to the cache
   * (`applySaved`) before the next one reads it.
   */
  const queue = useSaveQueue()
  const save = React.useCallback(
    (changes: Partial<Schemas["UpdatePlatformSettingsRequest"]>) =>
      queue(async () => {
        const current = queryClient.getQueryData<Schemas["PlatformSettingsDto"]>(SETTINGS_QUERY_KEY) ?? settings
        const { data, error } = await api.PUT("/api/v1/admin/platform-settings", {
          body: {
            platformName: current.platformName ?? "",
            logoUrl: current.logoUrl ?? null,
            logoUrlDark: current.logoUrlDark ?? null,
            faviconUrl: current.faviconUrl ?? null,
            ...changes,
          },
        })
        if (error) throw error
        applySaved(data)
        return data
      }),
    [queue, queryClient, settings, applySaved]
  )

  const schema = z.object({
    platformName: z.string().min(1, t("validation.required")),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { platformName: settings.platformName ?? "" },
  })

  const saveName = useMutation({
    mutationFn: (values: Values) => save({ platformName: values.platformName }),
    onSuccess: () => {
      refreshBranding()
      toast.success(t("platformSettings.updated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  // Logo changes persist immediately (same flow as organization logos).
  const persistLogoLight = React.useCallback(
    async (logoKey: string | null) => {
      await save({ logoUrl: logoKey })
    },
    [save]
  )
  const persistLogoDark = React.useCallback(
    async (logoKey: string | null) => {
      await save({ logoUrlDark: logoKey })
    },
    [save]
  )
  const persistFavicon = React.useCallback(
    async (logoKey: string | null) => {
      await save({ faviconUrl: logoKey })
    },
    [save]
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
        {/* The light and dark logos are one mark, edited the way an
            application's is: one circle showing the current mode's logo, with
            a dialog holding one circle per mode. The favicon stays its own
            field beside it, since a wordmark is illegible at tab-icon size. */}
        <div className="mb-6 flex items-center gap-6">
          <div className="flex flex-col items-center gap-1.5">
            <ThemedLogo
              name={settings.platformName}
              label={t("platformSettings.logo")}
              lightSrc={settings.logoUrl}
              darkSrc={settings.logoUrlDark}
              canEdit
              persistLight={persistLogoLight}
              persistDark={persistLogoDark}
              invalidate={refreshBranding}
              successMessage={t("platformSettings.updated")}
              dialogTitle={t("platformSettings.logoDialogTitle")}
              dialogDescription={t("platformSettings.logoDialogDescription")}
            />
            <p className="text-xs text-muted-foreground">
              {t("platformSettings.logo")}
            </p>
          </div>
          <div className="flex flex-col items-center gap-1.5">
            <LogoAvatar
              src={settings.faviconUrl}
              name={settings.platformName}
              label={t("platformSettings.favicon")}
              canEdit
              persist={persistFavicon}
              frame="favicon"
              invalidate={refreshBranding}
              successMessage={t("platformSettings.updated")}
            />
            <p className="text-xs text-muted-foreground">
              {t("platformSettings.favicon")}
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
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/admin/platform-settings")),
  })

  return (
    // Same cap as the system settings page, and for the same reason: this page
    // is a form, and its one free-text input is `w-full` from the primitive, so
    // an uncapped column stretches the platform name across a 2560px monitor.
    <div className="flex max-w-(--content-measure) flex-col gap-6">
      <PageHeader
        title={t("platformSettings.title")}
        description={t("platformSettings.subtitle")}
      />
      {query.isLoading || !query.data ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <>
          <SettingsCard settings={query.data} />
          <AppearanceCard
            settings={query.data}
            onSaved={(saved) => {
              if (saved) queryClient.setQueryData(SETTINGS_QUERY_KEY, saved)
            }}
          />
        </>
      )}
    </div>
  )
}
