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
import { useTabParam } from "@authsystem/ui/hooks/use-tab-param"
import { useUnsavedChangesPrompt } from "@authsystem/ui/hooks/use-unsaved-changes"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { cn } from "@authsystem/ui/utils"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { getErrorMessage } from "@authsystem/api/errors"
import type { Schemas } from "@authsystem/api/types"

import { AppearanceCard } from "./appearance-card"
import { FontsTab } from "./fonts-tab"
import { LogoDisplaySizeCard } from "../../components/logo-display-size/logo-display-size-card"
import { useReportUnsaved, useUnsavedParts, type UnsavedState } from "../../lib/unsaved-state"
import {
  PLATFORM_SETTINGS_TABS,
  type PlatformSettingsTabValue,
} from "./platform-settings-tabs"

/** The platform's sign-in page designs: their own chunk, loaded when the tab first opens. */
const LoginDesignsTab = React.lazy(() => import("./login-designs-tab"))

const SETTINGS_QUERY_KEY = ["platform-settings"] as const

function SettingsCard({
  settings,
  onUnsavedChange,
}: {
  settings: Schemas["PlatformSettingsDto"]
  onUnsavedChange?: (state: UnsavedState) => void
}) {
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
    onSuccess: (_saved, values) => {
      // The saved name is the new baseline, so the leave-prompt below stops asking.
      form.reset(values)
      refreshBranding()
      toast.success(t("platformSettings.updated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  // The tabs keep an unsaved name across a switch; leaving the page (opening a design from
  // the Templates tab, any other link) asks first, through the page's one prompt.
  useReportUnsaved(onUnsavedChange, form.formState.isDirty, saveName.isPending)

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

/** The Identity tab's two parts, each with edits of its own to keep. */
const IDENTITY_PARTS = ["branding", "logoSize"] as const

/**
 * The Identity tab: the name, logos and favicon, then the height the logo is shown at on the
 * sign-in pages, beside the logo and outside its upload dialog (owner decision D6).
 */
function IdentityTab({
  settings,
  onUnsavedChange,
}: {
  settings: Schemas["PlatformSettingsDto"]
  onUnsavedChange: (state: UnsavedState) => void
}) {
  const queryClient = useQueryClient()
  const { reporters, combined } = useUnsavedParts(IDENTITY_PARTS)
  useReportUnsaved(onUnsavedChange, combined.isDirty, combined.isSaving)

  const saveLogoHeight = React.useCallback(
    async (logoHeightPx: number | null) => {
      const { data, error } = await api.PUT("/api/v1/admin/platform-settings/logo-height", {
        body: { logoHeightPx },
      })
      if (error) throw error
      // The height alone into the page's copy: the branding card's saves queue on that copy
      // (useSaveQueue), and a whole answer landing out of turn could hand an older logo back to
      // its next save.
      queryClient.setQueryData<Schemas["PlatformSettingsDto"]>(SETTINGS_QUERY_KEY, (old) =>
        old ? { ...old, logoHeightPx: data?.logoHeightPx ?? null } : old
      )
      void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
      return data?.logoHeightPx
    },
    [queryClient]
  )

  return (
    <div className="flex flex-col gap-6">
      <SettingsCard settings={settings} onUnsavedChange={reporters.branding} />
      <LogoDisplaySizeCard
        owner="platform"
        storedPx={settings.logoHeightPx}
        name={settings.platformName ?? ""}
        lightSrc={settings.logoUrl}
        darkSrc={settings.logoUrlDark}
        canEdit
        save={saveLogoHeight}
        onUnsavedChange={reporters.logoSize}
      />
    </div>
  )
}

interface PlatformSettingsTabPanel {
  labelKey: string
  /** Forms are capped at the reading measure; a table is never capped. */
  capped: boolean
  render: (context: {
    settings: Schemas["PlatformSettingsDto"]
    onSaved: (saved: Schemas["PlatformSettingsDto"] | undefined) => void
    /** The tab's unsaved edits, for the page's one leave-prompt. */
    onUnsavedChange: (state: UnsavedState) => void
  }) => React.ReactNode
}

/**
 * Each tab's panel. Adding a tab (fonts, languages) is its value in
 * `platform-settings-tabs.ts` and its entry here; the allow-list of the `tab` parameter,
 * the tab strip, the panels and the keep-mounted rule all follow. Each tab also has its
 * row in the command palette (`static-surfaces.ts`).
 */
const TAB_PANELS: Record<PlatformSettingsTabValue, PlatformSettingsTabPanel> = {
  identity: {
    labelKey: "platformSettings.tabIdentity",
    capped: true,
    render: ({ settings, onUnsavedChange }) => (
      <IdentityTab settings={settings} onUnsavedChange={onUnsavedChange} />
    ),
  },
  appearance: {
    labelKey: "platformSettings.appearance.title",
    capped: true,
    render: ({ settings, onSaved, onUnsavedChange }) => (
      <AppearanceCard settings={settings} onSaved={onSaved} onUnsavedChange={onUnsavedChange} />
    ),
  },
  templates: {
    labelKey: "loginDesigns.tab",
    capped: false,
    render: () => (
      <React.Suspense fallback={<Skeleton className="h-64 w-full" />}>
        <LoginDesignsTab />
      </React.Suspense>
    ),
  },
  fonts: {
    labelKey: "platformSettings.tabFonts",
    capped: true,
    render: ({ settings, onSaved, onUnsavedChange }) => (
      <FontsTab settings={settings} onSaved={onSaved} onUnsavedChange={onUnsavedChange} />
    ),
  },
}

const TABS = PLATFORM_SETTINGS_TABS.map((value) => ({ value, ...TAB_PANELS[value] }))

export function PlatformSettingsPage() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [activeTab, setActiveTab] = useTabParam(PLATFORM_SETTINGS_TABS)
  // A tab once opened stays mounted (hidden) across switches: unmounting it would drop an
  // unsaved name, an appearance draft and its live preview, or the list's place.
  const [opened, setOpened] = React.useState<ReadonlySet<string>>(() => new Set([activeTab]))
  if (!opened.has(activeTab)) setOpened(new Set([...opened, activeTab]))

  const query = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: () => unwrap(api.GET("/api/v1/admin/platform-settings")),
  })

  // One leave-prompt for every tab: a router honours a single blocker, so each tab reports
  // its unsaved edits here instead of holding a prompt of its own.
  const { reporters, combined } = useUnsavedParts(PLATFORM_SETTINGS_TABS)
  const unsavedPrompt = useUnsavedChangesPrompt(combined)

  const onSaved = React.useCallback(
    (saved: Schemas["PlatformSettingsDto"] | undefined) => {
      if (saved) queryClient.setQueryData(SETTINGS_QUERY_KEY, saved)
    },
    [queryClient]
  )

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("platformSettings.title")}
        description={t("platformSettings.subtitle")}
      />
      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList>
          {TABS.map((tab) => (
            <TabsTrigger key={tab.value} value={tab.value}>
              {t(tab.labelKey)}
            </TabsTrigger>
          ))}
        </TabsList>
        {TABS.map((tab) => (
          <TabsContent
            key={tab.value}
            value={tab.value}
            // The cap, for the form tabs: the one free-text input is `w-full` from the
            // primitive, so an uncapped column stretches the platform name across a
            // 2560px monitor. Same cap as the system settings page.
            className={cn("mt-4 data-[state=inactive]:hidden", tab.capped && "max-w-(--content-measure)")}
            forceMount={opened.has(tab.value) ? true : undefined}
          >
            {!opened.has(tab.value) ? null : query.isLoading || !query.data ? (
              <Skeleton className="h-64 w-full" />
            ) : (
              tab.render({ settings: query.data, onSaved, onUnsavedChange: reporters[tab.value] })
            )}
          </TabsContent>
        ))}
      </Tabs>
      {unsavedPrompt}
    </div>
  )
}
