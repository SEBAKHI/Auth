import { Info } from "lucide-react"
import { useTranslation } from "react-i18next"

import { ACCOUNTS_URL } from "@authsystem/api/env"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { CopyButton } from "@authsystem/ui/common/copy-button"
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"

import { accountBarSnippet } from "./account-bar-snippet"

/** The snippet on an application's page, ready to copy, with what it needs to work. */
export function ApplicationAccountBarCard() {
  const { t } = useTranslation()
  const snippet = accountBarSnippet()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("applications.accountBar.title")}</CardTitle>
        <CardDescription>{t("applications.accountBar.description")}</CardDescription>
        <CardAction>
          <CopyButton value={snippet} label={t("applications.accountBar.copy")} />
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <pre className="overflow-x-auto rounded-lg border bg-muted p-3 text-xs">
          <code>{snippet}</code>
        </pre>
        <p className="text-sm text-muted-foreground">{t("applications.accountBar.attributes")}</p>
        <Alert>
          <Info />
          <AlertDescription>
            {t("applications.accountBar.subdomainsOnly", { origin: ACCOUNTS_URL })}
          </AlertDescription>
        </Alert>
      </CardContent>
    </Card>
  )
}
