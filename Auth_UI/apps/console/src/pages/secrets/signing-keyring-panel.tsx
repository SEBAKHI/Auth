import { useQuery } from "@tanstack/react-query"
import {
  KeyRound,
  MoreHorizontal,
  RefreshCw,
  ShieldAlert,
  TriangleAlert,
} from "lucide-react"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import { getErrorCodes, getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import { Alert, AlertDescription, AlertTitle } from "@authsystem/ui/alert"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@authsystem/ui/card"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { formatDateTime } from "@authsystem/ui/format"
import { Skeleton } from "@authsystem/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@authsystem/ui/table"

import type { PendingSecretOperation } from "./secret-operation-flow"
import {
  compromisedStillAccepted,
  signingKeyringCapabilities,
  signingKeyringRowCapabilities,
  signingStateOf,
  type SigningKeyState,
  type SigningKeyringEntry,
} from "./signing-keyring-capabilities"

/**
 * The signing keyring (S12): which key signs every access token, which key is
 * staged and since when it is published, and how long each older key is still
 * accepted. A scheduled rotation signs nobody out: stage, restart the API so it
 * loads the key, wait 24 hours, promote. Every change goes through the same
 * three-gate confirmation as the other key operations; this card only chooses the
 * change and names its key.
 */
export function SigningKeyringPanel({
  busy,
  onChange,
  onStageImport,
}: {
  /** A confirmation is already open: no second change starts meanwhile. */
  busy: boolean
  /** Starts the confirmation of a keyring change. */
  onChange: (pending: PendingSecretOperation) => void
  /** Opens the dialog that collects a private key to stage. */
  onStageImport: () => void
}) {
  const { t, i18n } = useTranslation()

  // The keyring read the HMAC panel makes: the signing ring is its `rsa` member,
  // so the two cards share one request and one cache entry.
  const query = useQuery({
    queryKey: ["secrets", "keyring", i18n.language],
    retry: false,
    queryFn: () => unwrap(api.GET("/api/v1/admin/Secrets/keyring")),
  })

  if (query.isLoading) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{t("secrets.signingKeyring.title")}</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-32 w-full" />
        </CardContent>
      </Card>
    )
  }

  const keyring = query.data?.rsa
  if (query.isError || !keyring || !keyring.available) {
    // PlainText mode answers the whole read with Secret.KeyringUnavailable; a key
    // from another source, or a ring the file cannot give, says so inside `rsa`.
    const unavailable =
      getErrorCodes(query.error).includes("Secret.KeyringUnavailable") ||
      (keyring && !keyring.available && keyring.unavailableReason !== "Unreadable")
    return (
      <Card>
        <CardHeader>
          <CardTitle>{t("secrets.signingKeyring.title")}</CardTitle>
          <CardDescription>{t("secrets.signingKeyring.description")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <Alert variant={unavailable ? "default" : "destructive"}>
            <TriangleAlert />
            <AlertTitle>
              {unavailable
                ? t("secrets.signingKeyring.unavailableTitle")
                : t("secrets.signingKeyring.failedTitle")}
            </AlertTitle>
            <AlertDescription>
              {keyring && !keyring.available
                ? keyring.unavailableReason === "ExternalSource"
                  ? t("secrets.signingKeyring.unavailable.ExternalSource", {
                      source: keyring.keySource,
                    })
                  : keyring.unavailableReason === "Unreadable"
                    ? t("secrets.signingKeyring.unavailable.Unreadable", {
                        code: keyring.errorCode ?? "",
                      })
                    : t("secrets.signingKeyring.unavailable.PlainText")
                : getErrorMessage(query.error)}
            </AlertDescription>
          </Alert>
          {unavailable ? null : (
            <div>
              <Button variant="outline" size="sm" onClick={() => query.refetch()}>
                <RefreshCw data-icon="inline-start" />
                {t("common.retry")}
              </Button>
            </div>
          )}
        </CardContent>
      </Card>
    )
  }

  const can = signingKeyringCapabilities(keyring, t)
  const staged = keyring.keys.find(
    (entry) => entry.inFile && signingStateOf(entry) === "Next"
  )
  const stillAccepted = compromisedStillAccepted(keyring)
  const reasons = [can.stage, can.promote, can.emergency].filter(
    (reason, index, all): reason is string =>
      Boolean(reason) && all.indexOf(reason) === index
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("secrets.signingKeyring.title")}</CardTitle>
        <CardDescription>{t("secrets.signingKeyring.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {stillAccepted.length > 0 ? (
          // An emergency this process applies only at its next start: never leave
          // an administrator believing a leaked key is closed while it is accepted.
          <Alert variant="destructive">
            <ShieldAlert />
            <AlertTitle>{t("secrets.signingKeyring.compromisedAcceptedTitle")}</AlertTitle>
            <AlertDescription>
              {t("secrets.signingKeyring.compromisedAcceptedBody", {
                kids: stillAccepted.join(", "),
              })}
            </AlertDescription>
          </Alert>
        ) : null}

        {keyring.persisted === false ? (
          <Alert>
            <KeyRound />
            <AlertDescription>
              {t("secrets.signingKeyring.migratedNote", { kid: keyring.processCurrentKid })}
            </AlertDescription>
          </Alert>
        ) : null}

        <div className="flex flex-wrap gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" disabled={busy || Boolean(can.stage)}>
                <KeyRound data-icon="inline-start" />
                {t("secrets.signingKeyring.stage")}
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuGroup>
                <DropdownMenuItem
                  onClick={() =>
                    onChange({ operation: "GenerateRsaKey", keyringAction: "Stage" })
                  }
                >
                  {t("secrets.signingKeyring.stageGenerate")}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onStageImport}>
                  {t("secrets.signingKeyring.stageImport")}
                </DropdownMenuItem>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
          <Button
            size="sm"
            disabled={busy || Boolean(can.promote)}
            onClick={() =>
              onChange({
                operation: "GenerateRsaKey",
                keyringAction: "Promote",
                kid: staged?.kid,
              })
            }
          >
            {t("secrets.signingKeyring.promote")}
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={busy || Boolean(can.discard)}
            onClick={() =>
              onChange({
                operation: "GenerateRsaKey",
                keyringAction: "DiscardNext",
                kid: staged?.kid,
              })
            }
          >
            {t("secrets.signingKeyring.discard")}
          </Button>
          <Button
            variant="destructive"
            size="sm"
            disabled={busy || Boolean(can.emergency)}
            onClick={() =>
              onChange({
                operation: "GenerateRsaKey",
                keyringAction: "PromoteEmergency",
                kid: staged?.kid,
              })
            }
          >
            {t("secrets.signingKeyring.emergency")}
          </Button>
        </div>

        {reasons.length > 0 ? (
          <ul className="flex flex-col gap-1 text-sm text-muted-foreground">
            {reasons.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        ) : null}

        {staged && !staged.loadedInProcess ? (
          <p className="text-sm text-muted-foreground">
            {t("secrets.signingKeyring.emergencyAtRestart")}
          </p>
        ) : null}

        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("secrets.signingKeyring.columns.kid")}</TableHead>
                <TableHead>{t("secrets.signingKeyring.columns.state")}</TableHead>
                <TableHead>{t("secrets.signingKeyring.columns.createdAtUtc")}</TableHead>
                <TableHead>{t("secrets.signingKeyring.columns.publishedAtUtc")}</TableHead>
                <TableHead>{t("secrets.signingKeyring.columns.verifyUntilUtc")}</TableHead>
                <TableHead>{t("secrets.signingKeyring.columns.loadedInProcess")}</TableHead>
                <TableHead>
                  <span className="sr-only">{t("common.actions")}</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {keyring.keys.map((entry) => (
                <SigningKeyringRow
                  key={`${entry.kid}-${entry.inFile}`}
                  entry={entry}
                  processCurrentKid={keyring.processCurrentKid}
                  busy={busy}
                  onChange={onChange}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      </CardContent>
    </Card>
  )
}

function SigningKeyringRow({
  entry,
  processCurrentKid,
  busy,
  onChange,
}: {
  entry: SigningKeyringEntry
  processCurrentKid: string | null | undefined
  busy: boolean
  onChange: (pending: PendingSecretOperation) => void
}) {
  const { t } = useTranslation()
  const state = signingStateOf(entry)
  const row = signingKeyringRowCapabilities(entry, t, processCurrentKid)

  const stateLabels: Record<SigningKeyState, string> = {
    Current: t("secrets.signingKeyring.state.current"),
    Next: t("secrets.signingKeyring.state.next"),
    Previous: t("secrets.signingKeyring.state.previous"),
    Compromised: t("secrets.signingKeyring.state.compromised"),
  }

  return (
    <TableRow>
      {/* A thumbprint is 43 characters: cut to the cell, the full kid on hover.
          No dir: the browser renders an identifier for the reader. */}
      <TableCell className="max-w-48 font-mono text-xs">
        <span className="block truncate" title={entry.kid}>
          {entry.kid}
        </span>
      </TableCell>
      <TableCell>
        <div className="flex flex-wrap gap-1">
          <Badge
            variant={
              state === "Current" ? "default" : state === "Compromised" ? "destructive" : "outline"
            }
          >
            {stateLabels[state]}
          </Badge>
          {entry.migrated ? (
            <Badge variant="outline">{t("secrets.signingKeyring.migrated")}</Badge>
          ) : null}
        </div>
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {entry.createdAtUtc
          ? formatDateTime(entry.createdAtUtc)
          : t("secrets.signingKeyring.unknownAge")}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {entry.publishedAtUtc ? formatDateTime(entry.publishedAtUtc) : "—"}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {state === "Current"
          ? t("secrets.signingKeyring.signing")
          : state === "Compromised"
            ? t("secrets.signingKeyring.closed")
            : entry.verifyUntilUtc
              ? formatDateTime(entry.verifyUntilUtc)
              : "—"}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {entry.loadedInProcess ? t("common.yes") : t("common.no")}
      </TableCell>
      <TableCell className="text-end">
        {row.note ? (
          <span className="text-xs text-muted-foreground">{row.note}</span>
        ) : (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={busy}
                aria-label={t("secrets.signingKeyring.rowActions", { kid: entry.kid })}
              >
                <MoreHorizontal />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              {/* onSelect, not onClick: Radix withholds onSelect from a disabled item. */}
              <DropdownMenuGroup>
                <DropdownMenuItem
                  variant="destructive"
                  disabled={Boolean(row.retire)}
                  onSelect={() =>
                    onChange({
                      operation: "GenerateRsaKey",
                      keyringAction: "Retire",
                      kid: entry.kid,
                    })
                  }
                >
                  {t("secrets.signingKeyring.retire")}
                </DropdownMenuItem>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </TableCell>
    </TableRow>
  )
}
