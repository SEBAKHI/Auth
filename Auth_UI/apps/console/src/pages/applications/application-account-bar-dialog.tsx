import { Info, TriangleAlert } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { ACCOUNTS_URL } from "@authsystem/api/env"
import type { Schemas } from "@authsystem/api/types"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { CopyButton } from "@authsystem/ui/common/copy-button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { Field, FieldDescription, FieldGroup, FieldLabel, FieldTitle } from "@authsystem/ui/field"
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

import {
  accountBarCsp,
  accountBarSnippet,
  endSessionUrl,
  IAM_SIGN_IN,
  signInChoices,
  type AccountBarShows,
  type AccountBarTheme,
} from "./account-bar-snippet"

type Application = Schemas["ApplicationDto"]

const SHOWS: AccountBarShows[] = ["both", "apps", "avatar"]
const THEMES: AccountBarTheme[] = ["person", "light", "dark"]

/**
 * The account bar's code for one application (owner, 2026-11-02), behind the application
 * page's actions menu. What IAM knows about the application fills itself in; what has more
 * than one possible answer starts empty and must be chosen, so a guess (the first return
 * address, which may be a localhost one) never reaches the code unnoticed. A question with
 * one answer only is answered for the administrator. The code appears once nothing is left
 * open. Nothing here is saved.
 */
export function ApplicationAccountBarDialog({
  application,
  open,
  onOpenChange,
  onEditApplication,
}: {
  application: Application
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Opens the application's edit dialog: where a missing return address is added. */
  onEditApplication?: () => void
}) {
  const { t } = useTranslation()
  const [shows, setShows] = React.useState<AccountBarShows | null>(null)
  const [theme, setTheme] = React.useState<AccountBarTheme | null>(null)
  const [signOutHref, setSignOutHref] = React.useState("")
  const [signIn, setSignIn] = React.useState<string | null>(null)
  const [returnUri, setReturnUri] = React.useState<string | null>(null)

  const returnUris = application.postLogoutRedirectUris ?? []
  const signIns = signInChoices(application)
  // One possible answer is no question: it is taken. More than one waits for a choice.
  const chosenReturnUri = returnUris.length === 1 ? returnUris[0] : returnUri
  const chosenSignIn = signIns.length === 1 ? signIns[0] : signIn
  const clientId = application.code ?? ""

  const complete =
    shows !== null &&
    theme !== null &&
    signOutHref.trim() !== "" &&
    chosenSignIn !== null &&
    chosenReturnUri !== null &&
    clientId !== ""

  const signInLabel = (value: string) =>
    value === IAM_SIGN_IN
      ? t("applications.accountBar.signInIam")
      : value === application.initiateLoginUri?.trim()
        ? t("applications.accountBar.signInInitiate", { address: value })
        : t("applications.accountBar.signInBase", { address: value })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>{t("applications.accountBar.title")}</DialogTitle>
          <DialogDescription>{t("applications.accountBar.description")}</DialogDescription>
        </DialogHeader>

        <FieldGroup>
          <Field>
            <FieldTitle id="account-bar-shows">{t("applications.accountBar.shows")}</FieldTitle>
            <ToggleGroup
              type="single"
              spacing={0}
              variant="outline"
              value={shows ?? ""}
              aria-labelledby="account-bar-shows"
              onValueChange={(next) => next && setShows(next as AccountBarShows)}
            >
              {SHOWS.map((option) => (
                <ToggleGroupItem key={option} value={option}>
                  {t(`applications.accountBar.showsOptions.${option}`)}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
          </Field>
          <Field>
            <FieldTitle id="account-bar-theme">{t("applications.accountBar.theme")}</FieldTitle>
            <ToggleGroup
              type="single"
              spacing={0}
              variant="outline"
              value={theme ?? ""}
              aria-labelledby="account-bar-theme"
              onValueChange={(next) => next && setTheme(next as AccountBarTheme)}
            >
              {THEMES.map((option) => (
                <ToggleGroupItem key={option} value={option}>
                  {t(`applications.accountBar.themeOptions.${option}`)}
                </ToggleGroupItem>
              ))}
            </ToggleGroup>
            <FieldDescription>{t("applications.accountBar.themeHint")}</FieldDescription>
          </Field>
          <Field>
            <FieldLabel htmlFor="account-bar-signout">{t("applications.accountBar.signOutHref")}</FieldLabel>
            <Input
              id="account-bar-signout"
              required
              placeholder="/sign-out"
              value={signOutHref}
              onChange={(event) => setSignOutHref(event.target.value)}
            />
            <FieldDescription>{t("applications.accountBar.signOutHrefHint")}</FieldDescription>
          </Field>
          {signIns.length > 1 ? (
            <Field>
              <FieldLabel htmlFor="account-bar-signin">{t("applications.accountBar.signIn")}</FieldLabel>
              <Select value={signIn ?? undefined} onValueChange={setSignIn} required>
                <SelectTrigger id="account-bar-signin">
                  <SelectValue placeholder={t("applications.accountBar.choose")} />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {signIns.map((value) => (
                      <SelectItem key={value} value={value}>
                        {signInLabel(value)}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          ) : null}
          {returnUris.length > 1 ? (
            <Field>
              <FieldLabel htmlFor="account-bar-return">{t("applications.accountBar.returnUri")}</FieldLabel>
              <Select value={returnUri ?? undefined} onValueChange={setReturnUri} required>
                <SelectTrigger id="account-bar-return">
                  <SelectValue placeholder={t("applications.accountBar.choose")} />
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
              <FieldDescription>{t("applications.accountBar.returnUriHint")}</FieldDescription>
            </Field>
          ) : null}
        </FieldGroup>

        {returnUris.length === 0 ? (
          // No dead end: the address is added in the application's own edit dialog.
          <Alert>
            <TriangleAlert />
            <AlertDescription className="flex flex-col items-start gap-2">
              {t("applications.accountBar.noReturnUri")}
              {onEditApplication ? (
                <Button variant="outline" size="sm" onClick={onEditApplication}>
                  {t("applications.accountBar.editApplication")}
                </Button>
              ) : null}
            </AlertDescription>
          </Alert>
        ) : !complete ? (
          <p className="text-sm text-muted-foreground">{t("applications.accountBar.incomplete")}</p>
        ) : (
          <>
            <CodeBlock
              title={t("applications.accountBar.headerCode")}
              code={accountBarSnippet({
                shows: shows!,
                theme: theme!,
                signOutHref,
                signInHref: chosenSignIn === IAM_SIGN_IN ? "" : chosenSignIn!,
              })}
            />
            <CodeBlock
              title={t("applications.accountBar.endSessionCode")}
              description={t("applications.accountBar.endSessionHint")}
              code={endSessionUrl(clientId, chosenReturnUri!)}
            />
            <CodeBlock
              title={t("applications.accountBar.cspCode")}
              description={t("applications.accountBar.cspHint")}
              code={accountBarCsp()}
            />
          </>
        )}

        <Alert>
          <Info />
          <AlertDescription>
            {t("applications.accountBar.subdomainsOnly", { origin: ACCOUNTS_URL })}
          </AlertDescription>
        </Alert>
      </DialogContent>
    </Dialog>
  )
}

/** Code to copy, under its title, with a copy button that names it. */
function CodeBlock({ title, description, code }: { title: string; description?: string; code: string }) {
  const { t } = useTranslation()
  return (
    // `min-w-0`: a grid child, which a long line would otherwise widen past the dialog.
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{title}</h3>
        <CopyButton value={code} label={t("applications.accountBar.copy", { what: title })} />
      </div>
      {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
      {/* Wrapped, never scrolled: in a right-to-left page a scrolled box opens at its right
          end, so a long address showed without its beginning. Copying takes the text as is. */}
      <pre className="rounded-lg border bg-muted p-3 text-xs break-all whitespace-pre-wrap">
        <code>{code}</code>
      </pre>
    </div>
  )
}
