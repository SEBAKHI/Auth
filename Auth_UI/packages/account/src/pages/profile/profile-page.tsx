import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { AvatarMenu } from "@authsystem/ui/common/avatar-menu"
import { LanguageSelect } from "@authsystem/ui/common/language-select"
import { PageHeader } from "@authsystem/ui/common/page-header"
import { TimeZoneSelect } from "@authsystem/ui/common/timezone-select"
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
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { Skeleton } from "@authsystem/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@authsystem/ui/tabs"
import { isTheme, useTheme } from "@authsystem/ui/theme-provider"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"
import { useProfileImage } from "@authsystem/api/use-profile-image"
import { getErrorMessage } from "@authsystem/api/errors"
import { fullName } from "@authsystem/ui/format"
import i18n, {
  applyLanguage,
  persistLanguage,
  SUPPORTED_LANGUAGES,
  type LanguageCode,
} from "@authsystem/i18n"
import { setActiveTimeZone } from "@authsystem/i18n/timezone"
import type { Schemas } from "@authsystem/api/types"
import { ProfileDangerZone } from "./profile-danger-zone"
import { ProfileSecurity } from "./profile-security"
import { ProfileSessions } from "./profile-sessions"
import { Spinner } from "@authsystem/ui/spinner"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

function AccountTab({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const profileImage = useProfileImage()
  const { theme: activeTheme, setTheme } = useTheme()
  const meName = me.displayName || fullName(me.firstName, me.lastName, me.email ?? "")

  const schema = z.object({
    firstName: z.string().min(1, t("validation.required")),
    lastName: z.string().min(1, t("validation.required")),
    displayName: z.string().optional(),
    phoneNumber: z.string().optional(),
    preferredLanguage: z.string().optional(),
    timeZone: z.string().optional(),
    theme: z.string().optional(),
  })
  type Values = z.infer<typeof schema>

  const form = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      firstName: me.firstName ?? "",
      lastName: me.lastName ?? "",
      displayName: me.displayName ?? "",
      phoneNumber: me.phoneNumber ?? "",
      preferredLanguage: me.preferredLanguage ?? "",
      timeZone: me.timeZone ?? "",
      theme: me.theme ?? "system",
    },
  })

  const mutation = useMutation({
    mutationFn: async (values: Values) => {
      const { error } = await api.PUT("/api/v1/Users/me", {
        body: {
          firstName: values.firstName,
          lastName: values.lastName,
          displayName: emptyToNull(values.displayName),
          phoneNumber: emptyToNull(values.phoneNumber),
          preferredLanguage: emptyToNull(values.preferredLanguage),
          timeZone: emptyToNull(values.timeZone),
          theme: emptyToNull(values.theme),
        },
      })
      if (error) throw error
    },
    onSuccess: (_, values) => {
      void queryClient.invalidateQueries({ queryKey: ["me"] })
      // Apply the saved preferences immediately, without a reload.
      setActiveTimeZone(emptyToNull(values.timeZone))
      const code = values.preferredLanguage
      if (
        code &&
        SUPPORTED_LANGUAGES.some((lang) => lang.code === code) &&
        i18n.language !== code
      ) {
        persistLanguage(code as LanguageCode)
        void applyLanguage(code as LanguageCode)
      }
      const theme = values.theme
      if (theme && isTheme(theme) && activeTheme !== theme) {
        setTheme(theme)
      }
      toast.success(t("profile.profileUpdated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("profile.accountDetails")}</CardTitle>
        <CardDescription>{t("profile.accountDetailsSubtitle")}</CardDescription>
      </CardHeader>
      <CardContent>
        <div className="mb-6 flex items-center gap-4">
          <AvatarMenu
            src={me.profileImageUrl}
            name={meName}
            size="xl"
            onChange={profileImage.onChange}
            onRemove={profileImage.onRemove}
            pending={profileImage.pending}
          />
          <div className="min-w-0">
            <p className="truncate font-medium">{meName}</p>
            <p className="truncate text-sm text-muted-foreground">{me.email}</p>
          </div>
        </div>
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <FieldGroup>
              <div className="grid gap-7 sm:grid-cols-2">
                <FormField
                  control={form.control}
                  name="firstName"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("users.firstName")}</FormLabel>
                      <FormControl>
                        <Input placeholder="Sara" {...field} />
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
                        <Input placeholder="Al-Rashid" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="displayName"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("users.displayName")}</FormLabel>
                      <FormControl>
                        <Input placeholder="Sara Al-Rashid" {...field} />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="phoneNumber"
                  render={({ field }) => (
                    <FormItem>
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
                        <TimeZoneSelect
                          value={field.value}
                          onChange={field.onChange}
                        />
                      </FormControl>
                      <FormMessage />
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="theme"
                  render={({ field }) => (
                    <FormItem>
                      <FormLabel>{t("common.theme")}</FormLabel>
                      <Select
                        value={field.value}
                        onValueChange={field.onChange}
                      >
                        <FormControl>
                          <SelectTrigger className="w-full">
                            <SelectValue />
                          </SelectTrigger>
                        </FormControl>
                        <SelectContent>
                          <SelectGroup>
                            <SelectItem value="light">
                              {t("common.light")}
                            </SelectItem>
                            <SelectItem value="dark">
                              {t("common.dark")}
                            </SelectItem>
                            <SelectItem value="system">
                              {t("common.system")}
                            </SelectItem>
                          </SelectGroup>
                        </SelectContent>
                      </Select>
                      <FormMessage />
                    </FormItem>
                  )}
                />
              </div>
              <Button
                type="submit"
                className="w-fit"
                disabled={mutation.isPending}
              >
                {mutation.isPending ? (
                  <Spinner />
                ) : null}
                {t("profile.updateProfile")}
              </Button>
            </FieldGroup>
          </form>
        </Form>
      </CardContent>
    </Card>
  )
}

export function ProfilePage({
  showDangerZone = false,
}: {
  /**
   * Renders the self-service account-deletion card. Only the accounts app
   * opts in — it owns the signed-out /deletion-scheduled destination.
   */
  showDangerZone?: boolean
} = {}) {
  const { t } = useTranslation()

  const meQuery = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
  })

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t("profile.title")}
        description={t("profile.subtitle")}
      />

      <Tabs defaultValue="account">
        <TabsList>
          <TabsTrigger value="account">{t("profile.account")}</TabsTrigger>
          <TabsTrigger value="sessions">{t("profile.sessions")}</TabsTrigger>
          <TabsTrigger value="security">{t("profile.security")}</TabsTrigger>
        </TabsList>

        <TabsContent value="account" className="mt-4">
          {meQuery.isLoading || !meQuery.data ? (
            <Skeleton className="h-64 w-full" />
          ) : (
            <div className="flex flex-col gap-6">
              <AccountTab me={meQuery.data} />
              {showDangerZone ? <ProfileDangerZone me={meQuery.data} /> : null}
            </div>
          )}
        </TabsContent>
        <TabsContent value="sessions" className="mt-4">
          <ProfileSessions />
        </TabsContent>
        <TabsContent value="security" className="mt-4">
          {meQuery.isLoading || !meQuery.data ? (
            <Skeleton className="h-64 w-full" />
          ) : (
            <ProfileSecurity me={meQuery.data} />
          )}
        </TabsContent>
      </Tabs>
    </div>
  )
}
