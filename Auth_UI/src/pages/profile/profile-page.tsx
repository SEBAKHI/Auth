import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Loader2 } from "lucide-react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { PageHeader } from "@/components/common/page-header"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { FieldGroup } from "@/components/ui/field"
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui/form"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { getErrorMessage } from "@/lib/errors"
import type { Schemas } from "@/lib/api/types"
import { ProfileSecurity } from "./profile-security"
import { ProfileSessions } from "./profile-sessions"

function emptyToNull(value: string | undefined): string | null {
  return value && value.trim().length > 0 ? value : null
}

function AccountTab({ me }: { me: Schemas["UserDto"] }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const schema = z.object({
    firstName: z.string().min(1, t("validation.required")),
    lastName: z.string().min(1, t("validation.required")),
    displayName: z.string().optional(),
    phoneNumber: z.string().optional(),
    preferredLanguage: z.string().optional(),
    timeZone: z.string().optional(),
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
        },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["me"] })
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
        <Form {...form}>
          <form
            onSubmit={form.handleSubmit((values) => mutation.mutate(values))}
          >
            <FieldGroup>
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
              <FormField
                control={form.control}
                name="displayName"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>{t("users.displayName")}</FormLabel>
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
                      <Input {...field} />
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
                      <Input {...field} />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              <Button
                type="submit"
                className="w-fit"
                disabled={mutation.isPending}
              >
                {mutation.isPending ? (
                  <Loader2 className="animate-spin" />
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

export function ProfilePage() {
  const { t } = useTranslation()

  const meQuery = useQuery({
    queryKey: ["me"],
    queryFn: () => unwrap(api.GET("/api/v1/Users/me")),
  })

  return (
    <div className="space-y-6">
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
            <AccountTab me={meQuery.data} />
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
