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
import { toNumber, unwrap } from "@authsystem/api/helpers"
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

import {
  keyringCapabilities,
  keyringRowCapabilities,
  stateOf,
  type HmacKeyringEntry,
  type KeyState,
} from "./keyring-capabilities"
import type { PendingSecretOperation } from "./secret-operation-flow"

/**
 * The HMAC keyring (S11): which key fingerprints every new credential, which key
 * is staged, and how long each older key keeps verifying. Every change goes
 * through the same three-gate confirmation as the other key operations; this
 * panel only chooses the change and names its key.
 */
export function KeyringPanel({
  busy,
  onChange,
  onStageImport,
}: {
  /** A confirmation is already open: no second change starts meanwhile. */
  busy: boolean
  /** Starts the confirmation of a keyring change. */
  onChange: (pending: PendingSecretOperation) => void
  /** Opens the dialog that collects a key to stage. */
  onStageImport: () => void
}) {
  const { t, i18n } = useTranslation()

  const query = useQuery({
    // Its own key under ["secrets"], which every confirmed change invalidates;
    // language-scoped because a failure renders the server's own sentence.
    queryKey: ["secrets", "keyring", i18n.language],
    retry: false,
    queryFn: () => unwrap(api.GET("/api/v1/admin/Secrets/keyring")),
  })

  if (query.isLoading) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{t("secrets.keyring.title")}</CardTitle>
        </CardHeader>
        <CardContent>
          <Skeleton className="h-32 w-full" />
        </CardContent>
      </Card>
    )
  }

  if (query.isError || !query.data) {
    const unavailable = getErrorCodes(query.error).includes(
      "Secret.KeyringUnavailable"
    )
    return (
      <Card>
        <CardHeader>
          <CardTitle>{t("secrets.keyring.title")}</CardTitle>
          <CardDescription>{t("secrets.keyring.description")}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          <Alert variant={unavailable ? "default" : "destructive"}>
            <TriangleAlert />
            <AlertTitle>
              {unavailable
                ? t("secrets.keyring.unavailableTitle")
                : t("secrets.keyring.failedTitle")}
            </AlertTitle>
            <AlertDescription>{getErrorMessage(query.error)}</AlertDescription>
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

  const keyring = query.data
  const can = keyringCapabilities(keyring, t)
  const staged = keyring.keys.find((entry) => stateOf(entry) === "Next")
  const stagedId = staged ? toNumber(staged.id) : undefined
  const reasons = [can.stage, can.promote].filter(
    (reason, index, all): reason is string =>
      Boolean(reason) && all.indexOf(reason) === index
  )

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("secrets.keyring.title")}</CardTitle>
        <CardDescription>{t("secrets.keyring.description")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {keyring.inSync ? null : (
          <Alert variant="destructive">
            <ShieldAlert />
            <AlertTitle>{t("secrets.keyring.outOfSyncTitle")}</AlertTitle>
            <AlertDescription>
              {t("secrets.keyring.outOfSyncBody", {
                fileKey: toNumber(keyring.currentKeyId),
                processKey: toNumber(keyring.processCurrentKeyId),
              })}
            </AlertDescription>
          </Alert>
        )}

        <div className="flex flex-wrap gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" disabled={busy || Boolean(can.stage)}>
                <KeyRound data-icon="inline-start" />
                {t("secrets.keyring.stage")}
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuGroup>
                <DropdownMenuItem
                  onClick={() =>
                    onChange({ operation: "GenerateHmacKey", keyringAction: "Stage" })
                  }
                >
                  {t("secrets.keyring.stageGenerate")}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onStageImport}>
                  {t("secrets.keyring.stageImport")}
                </DropdownMenuItem>
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
          <Button
            size="sm"
            disabled={busy || Boolean(can.promote)}
            onClick={() =>
              onChange({
                operation: "GenerateHmacKey",
                keyringAction: "Promote",
                keyId: stagedId,
              })
            }
          >
            {t("secrets.keyring.promote")}
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={busy || Boolean(can.discard)}
            onClick={() =>
              onChange({
                operation: "GenerateHmacKey",
                keyringAction: "DiscardNext",
                keyId: stagedId,
              })
            }
          >
            {t("secrets.keyring.discard")}
          </Button>
          <Button
            variant="destructive"
            size="sm"
            disabled={busy}
            onClick={() =>
              onChange({ operation: "GenerateHmacKey", keyringAction: "PromoteEmergency" })
            }
          >
            {t("secrets.keyring.emergency")}
          </Button>
        </div>

        {reasons.length > 0 ? (
          <ul className="flex flex-col gap-1 text-sm text-muted-foreground">
            {reasons.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
        ) : null}

        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t("secrets.keyring.columns.id")}</TableHead>
                <TableHead>{t("secrets.keyring.columns.state")}</TableHead>
                <TableHead>{t("secrets.keyring.columns.createdAtUtc")}</TableHead>
                <TableHead>{t("secrets.keyring.columns.demotedAtUtc")}</TableHead>
                <TableHead>{t("secrets.keyring.columns.verifyUntilUtc")}</TableHead>
                <TableHead>{t("secrets.keyring.columns.webhookKeyCount")}</TableHead>
                <TableHead>{t("secrets.keyring.columns.loadedInProcess")}</TableHead>
                <TableHead>
                  <span className="sr-only">{t("common.actions")}</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {keyring.keys.map((entry) => (
                <KeyringRow
                  key={String(entry.id)}
                  entry={entry}
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

function KeyringRow({
  entry,
  busy,
  onChange,
}: {
  entry: HmacKeyringEntry
  busy: boolean
  onChange: (pending: PendingSecretOperation) => void
}) {
  const { t } = useTranslation()
  const id = toNumber(entry.id)
  const state = stateOf(entry)
  const row = keyringRowCapabilities(entry, t)
  const webhookKeys = toNumber(entry.webhookKeyCount)

  const stateLabels: Record<KeyState, string> = {
    Current: t("secrets.keyring.state.current"),
    Next: t("secrets.keyring.state.next"),
    Previous: t("secrets.keyring.state.previous"),
  }

  return (
    <TableRow>
      <TableCell className="font-medium tabular-nums">{id}</TableCell>
      <TableCell>
        <div className="flex flex-wrap gap-1">
          <Badge variant={state === "Current" ? "default" : "outline"}>
            {stateLabels[state]}
          </Badge>
          {entry.compromised ? (
            <Badge variant="destructive">{t("secrets.keyring.compromised")}</Badge>
          ) : null}
        </div>
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {entry.createdAtUtc ? formatDateTime(entry.createdAtUtc) : t("secrets.keyring.unknownAge")}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {entry.demotedAtUtc ? formatDateTime(entry.demotedAtUtc) : "—"}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {state === "Current"
          ? t("secrets.keyring.issuing")
          : entry.verifyUntilUtc
            ? formatDateTime(entry.verifyUntilUtc)
            : t("secrets.keyring.verifiesNothing")}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {webhookKeys > 0 && state !== "Current" && entry.webhookGraceUntilUtc
          ? t("secrets.keyring.webhookGrace", {
              count: webhookKeys,
              date: formatDateTime(entry.webhookGraceUntilUtc),
            })
          : webhookKeys}
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">
        {entry.loadedInProcess ? t("common.yes") : t("common.no")}
      </TableCell>
      <TableCell className="text-end">
        {row.note ? (
          <span className="text-xs text-muted-foreground">{row.note}</span>
        ) : (
          <div className="flex flex-col items-end gap-1">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  disabled={busy}
                  aria-label={t("secrets.keyring.rowActions", { id })}
                >
                  <MoreHorizontal />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {/* onSelect, not onClick: Radix withholds onSelect from a disabled
                    item, while onClick runs whenever the element is clicked; only
                    CSS stands between a disabled item and its click otherwise. */}
                <DropdownMenuGroup>
                  <DropdownMenuItem
                    disabled={Boolean(row.reinstate)}
                    onSelect={() =>
                      onChange({
                        operation: "GenerateHmacKey",
                        keyringAction: "Reinstate",
                        keyId: id,
                      })
                    }
                  >
                    {t("secrets.keyring.reinstate")}
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    variant="destructive"
                    disabled={Boolean(row.retire)}
                    onSelect={() =>
                      onChange({
                        operation: "GenerateHmacKey",
                        keyringAction: "Retire",
                        keyId: id,
                      })
                    }
                  >
                    {t("secrets.keyring.retire")}
                  </DropdownMenuItem>
                </DropdownMenuGroup>
              </DropdownMenuContent>
            </DropdownMenu>
            {/* A disabled menu item cannot say why, so the row does. */}
            {[row.reinstate, row.retire].filter(Boolean).map((reason) => (
              <span key={reason} className="max-w-64 text-xs text-muted-foreground">
                {reason}
              </span>
            ))}
          </div>
        )}
      </TableCell>
    </TableRow>
  )
}
