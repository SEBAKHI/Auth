import { useQuery } from "@tanstack/react-query"
import { Info, TriangleAlert } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"

import { api } from "@authsystem/api/client"
import { ACCOUNTS_URL } from "@authsystem/api/env"
import { unwrap } from "@authsystem/api/helpers"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@authsystem/ui/card"
import { ApplicationSelect } from "@authsystem/ui/common/application-select"
import { CopyButton } from "@authsystem/ui/common/copy-button"
import {
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  FieldTitle,
} from "@authsystem/ui/field"
import { Input } from "@authsystem/ui/input"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@authsystem/ui/select"
import { ToggleGroup, ToggleGroupItem } from "@authsystem/ui/toggle-group"

import { applicationHref } from "@/lib/record-hrefs"

import {
  accountBarCsp,
  accountBarSnippet,
  DEFAULT_ACCOUNT_BAR_CHOICES,
  endSessionUrl,
  type AccountBarShows,
  type AccountBarTheme,
} from "./account-bar-snippet"

const SHOWS: AccountBarShows[] = ["both", "apps", "avatar"]
const THEMES: AccountBarTheme[] = ["person", "light", "dark"]

/**
 * Platform settings › Account bar (owner, 2026-11-02): the code a linked application adds
 * to show IAM's account bar, written from a few choices instead of read from instructions.
 * The bar is the platform's, so it lives here and not on an application's page; the one
 * part that is an application's own, where its sign-out sends the browser afterwards, is
 * built from the application picked here and one of its registered return addresses.
 * Nothing on this tab is saved: it only writes code to copy.
 */
export function AccountBarTab() {
  const { t } = useTranslation()
  const [choices, setChoices] = React.useState(DEFAULT_ACCOUNT_BAR_CHOICES)
  const [applicationId, setApplicationId] = React.useState<string | undefined>()
  const [returnUri, setReturnUri] = React.useState<string | undefined>()

  const application = useQuery({
    queryKey: ["applications", applicationId],
    enabled: Boolean(applicationId),
    queryFn: () =>
      unwrap(api.GET("/api/v1/Applications/{id}", { params: { path: { id: applicationId! } } })),
  })
  const returnUris = application.data?.postLogoutRedirectUris ?? []
  // The first registered address until one is picked, and never one the application dropped.
  const chosenReturnUri = returnUri && returnUris.includes(returnUri) ? returnUri : returnUris[0]
  const clientId = application.data?.code

  const snippet = accountBarSnippet(choices)
  const csp = accountBarCsp()
  const update = (patch: Partial<typeof choices>) => setChoices((current) => ({ ...current, ...patch }))

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("platformSettings.accountBar.title")}</CardTitle>
        <CardDescription>{t("platformSettings.accountBar.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        <FieldGroup>
          <Field>
            <FieldTitle id="account-bar-shows">{t("platformSettings.accountBar.shows")}</FieldTitle>
            <ToggleGroup
              type="single"
              spacing={0}
              variant="outline"
              value={choices.shows}
              aria-labelledby="account-bar-shows"
              onValueChange={(next) => next && update({ shows: next as AccountBarShows })}
            >
              {SHOWS.map((option) => (
                <ToggleGroupItem key={option} value={option}>
                  {t(`platformSettings.accountBar.showsOptions.${option}`)}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
          </Field>
          <Field>
            <FieldTitle id="account-bar-theme">{t("platformSettings.accountBar.theme")}</FieldTitle>
            <ToggleGroup
              type="single"
              spacing={0}
              variant="outline"
              value={choices.theme}
              aria-labelledby="account-bar-theme"
              onValueChange={(next) => next && update({ theme: next as AccountBarTheme })}
            >
              {THEMES.map((option) => (
                <ToggleGroupItem key={option} value={option}>
                  {t(`platformSettings.accountBar.themeOptions.${option}`)}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
            <FieldDescription>{t("platformSettings.accountBar.themeHint")}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel htmlFor="account-bar-signout">{t("platformSettings.accountBar.signOutHref")}</FieldLabel>
            <Input
              id="account-bar-signout"
              value={choices.signOutHref}
              onChange={(event) => update({ signOutHref: event.target.value })}
            />
            <FieldDescription>{t("platformSettings.accountBar.signOutHrefHint")}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel htmlFor="account-bar-signin">{t("platformSettings.accountBar.signInHref")}</FieldLabel>
            <Input
              id="account-bar-signin"
              placeholder="/login"
              value={choices.signInHref}
              onChange={(event) => update({ signInHref: event.target.value })}
            />
            <FieldDescription>{t("platformSettings.accountBar.signInHrefHint")}</FieldDescription>
          </Field>
        </FieldGroup>

        <CodeBlock title={t("platformSettings.accountBar.headerCode")} code={snippet} />

        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="account-bar-application">{t("platformSettings.accountBar.application")}</FieldLabel>
            <ApplicationSelect
              id="account-bar-application"
              value={applicationId}
              onChange={(next) => {
                setApplicationId(next)
                setReturnUri(undefined)
              }}
              placeholder={t("platformSettings.accountBar.applicationPlaceholder")}
            />
            <FieldDescription>{t("platformSettings.accountBar.applicationHint")}</FieldDescription>
          </Field>
          {applicationId && application.data && returnUris.length > 1 ? (
            <Field>
              <FieldLabel htmlFor="account-bar-return">{t("platformSettings.accountBar.returnUri")}</FieldLabel>
              <Select value={chosenReturnUri} onValueChange={setReturnUri}>
                <SelectTrigger id="account-bar-return">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {returnUris.map((uri) => (
                      <SelectItem key={uri} value={uri}>
                        {uri}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          ) : null}
        </FieldGroup>

        {applicationId && application.data && clientId ? (
          chosenReturnUri ? (
            <CodeBlock
              title={t("platformSettings.accountBar.endSessionCode")}
              description={t("platformSettings.accountBar.endSessionHint")}
              code={endSessionUrl(clientId, chosenReturnUri)}
            />
          ) : (
            // No dead end: the address is registered on the application's own page.
            <Alert>
              <TriangleAlert />
              <AlertDescription>
                {t("platformSettings.accountBar.noReturnUri")}{" "}
                <Link to={applicationHref(applicationId) ?? "/applications"} className="underline underline-offset-4">
                  {t("platformSettings.accountBar.openApplication", { name: application.data.name })}
                </Link>
              </AlertDescription>
            </Alert>
          )
        ) : null}

        <CodeBlock
          title={t("platformSettings.accountBar.cspCode")}
          description={t("platformSettings.accountBar.cspHint")}
          code={csp}
        />

        <Alert>
          <Info />
          <AlertDescription>
            {t("platformSettings.accountBar.subdomainsOnly", { origin: ACCOUNTS_URL })}
          </AlertDescription>
        </Alert>
      </CardContent>
    </Card>
  )
}

/** Code to copy, under its title, with a copy button that names it. */
function CodeBlock({ title, description, code }: { title: string; description?: string; code: string }) {
  const { t } = useTranslation()
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{title}</h3>
        <CopyButton value={code} label={t("platformSettings.accountBar.copy", { what: title })} />
      </div>
      {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
      <pre className="overflow-x-auto rounded-lg border bg-muted p-3 text-xs">
        <code>{code}</code>
      </pre>
    </div>
  )
}

export default AccountBarTab
