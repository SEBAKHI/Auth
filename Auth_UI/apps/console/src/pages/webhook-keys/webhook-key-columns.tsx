import type { ColumnDef } from "@tanstack/react-table"
import { MoreHorizontal } from "lucide-react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { formatDateTime } from "@authsystem/ui/format"
import type { Schemas } from "@authsystem/api/types"
import { WebhookKeyStatus } from "./webhook-key-status"
import { credentialExpiryState } from "../credential-key-list-url"
import { revokeReasonColumn } from "../credential-key-rows"

type WebhookKeyDto = Schemas["WebhookKeyDto"]

/**
 * One row type, one set of columns (OI-264): the main-menu page and an
 * application's tab both read this list, so a column, a cell or a row action
 * changed here changes on every surface that lists webhook keys.
 */
export const WEBHOOK_KEY_COLUMN_IDS = [
  "name",
  "applicationName",
  "targetUrl",
  "environment",
  "status",
  "revokeReason",
  "expiresAt",
  "createdAt",
] as const

export type WebhookKeyColumnId = (typeof WEBHOOK_KEY_COLUMN_IDS)[number]

/**
 * The columns a URL may sort by. The application column is absent: the server's
 * sort list for this endpoint has no application name.
 */
export const WEBHOOK_KEY_SORTABLE_COLUMNS = [
  "name",
  "targetUrl",
  "environment",
  "status",
  "expiresAt",
  "createdAt",
] as const satisfies readonly WebhookKeyColumnId[]

export interface WebhookKeyColumnsOptions {
  /** Ids to start hidden on this surface; they stay in the columns menu. */
  defaultHidden?: readonly WebhookKeyColumnId[]
  /** Row actions; a missing callback leaves its menu item out. */
  onRotate?: (key: WebhookKeyDto) => void
  onRevoke?: (key: WebhookKeyDto) => void
}

export function useWebhookKeyColumns({
  defaultHidden,
  onRotate,
  onRevoke,
}: WebhookKeyColumnsOptions): ColumnDef<WebhookKeyDto, unknown>[] {
  const { t } = useTranslation()
  const hidden = new Set<WebhookKeyColumnId>(defaultHidden ?? [])
  const isHidden = (id: WebhookKeyColumnId) =>
    hidden.has(id) ? { defaultHidden: true } : {}

  return [
    {
      id: "name",
      accessorFn: (row) => row.name ?? "",
      header: t("common.name"),
      meta: {
        label: t("common.name"),
        covers: ["keyPrefix"],
        ...isHidden("name"),
      },
      cell: ({ row }) => (
        <div className="min-w-0">
          <p className="truncate font-medium">{row.original.name}</p>
          <p className="truncate font-mono text-xs text-muted-foreground">
            {/* The ellipsis is neutral, so in an RTL paragraph it took the
                paragraph level and rendered before the prefix. */}
            <bdi dir="ltr">{row.original.keyPrefix}…</bdi>
          </p>
        </div>
      ),
    },
    {
      id: "applicationName",
      accessorFn: (row) => row.applicationName ?? "",
      enableSorting: false,
      header: t("fields.applicationName"),
      meta: {
        label: t("fields.applicationName"),
        covers: ["applicationId"],
        ...isHidden("applicationName"),
      },
    },
    {
      id: "targetUrl",
      accessorFn: (row) => row.targetUrl ?? "",
      header: t("webhookKeys.targetUrl"),
      meta: { label: t("webhookKeys.targetUrl"), ...isHidden("targetUrl") },
      cell: ({ row }) => (
        <span className="block max-w-[220px] truncate text-sm text-muted-foreground">
          {row.original.targetUrl}
        </span>
      ),
    },
    {
      accessorKey: "environment",
      filterFn: "faceted",
      header: t("apiKeys.environment"),
      meta: {
        label: t("apiKeys.environment"),
        filterVariant: "faceted",
        ...isHidden("environment"),
      },
    },
    {
      id: "status",
      accessorFn: (row) => (row.isRevoked ? "revoked" : "active"),
      filterFn: "faceted",
      header: t("common.status"),
      meta: {
        label: t("common.status"),
        covers: ["isRevoked"],
        filterVariant: "faceted",
        filterOptions: [
          { value: "active", label: t("common.active") },
          { value: "revoked", label: t("common.revoked") },
        ],
        ...isHidden("status"),
      },
      cell: ({ row }) => <WebhookKeyStatus webhookKey={row.original} />,
    },
    revokeReasonColumn<WebhookKeyDto>(t),
    {
      id: "expiresAt",
      // The accessor stays the raw instant so sorting, CSV export and the row
      // detail sheet all show a date; the bucket exists only inside the predicate.
      accessorFn: (row) => row.expiresAt ?? "",
      filterFn: (row, _columnId, selected: string[]) =>
        selected.includes(credentialExpiryState(row.original.expiresAt)),
      header: t("common.expiresAt"),
      meta: {
        label: t("common.expiresAt"),
        filterVariant: "faceted",
        filterOptions: [
          { value: "expired", label: t("webhookKeys.expired") },
          { value: "soon", label: t("webhookKeys.expiringSoon") },
          { value: "later", label: t("webhookKeys.expiresLater") },
          { value: "none", label: t("webhookKeys.noExpiry") },
        ],
        ...isHidden("expiresAt"),
      },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.expiresAt
            ? formatDateTime(row.original.expiresAt)
            : t("webhookKeys.noExpiry")}
        </span>
      ),
    },
    {
      id: "createdAt",
      accessorFn: (row) => row.createdAt ?? "",
      header: t("common.createdAt"),
      meta: { label: t("common.createdAt"), ...isHidden("createdAt") },
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {formatDateTime(row.original.createdAt)}
        </span>
      ),
    },
    ...(onRotate || onRevoke
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: () => (
              <span className="sr-only">{t("common.actions")}</span>
            ),
            cell: ({ row }) => {
              const key = row.original
              if (key.isRevoked) return null
              return (
                <div className="text-end">
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={t("common.actions")}
                      >
                        <MoreHorizontal />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuGroup>
                        {onRotate ? (
                          <DropdownMenuItem onClick={() => onRotate(key)}>
                            {t("webhookKeys.rotate")}
                          </DropdownMenuItem>
                        ) : null}
                      </DropdownMenuGroup>
                      {onRevoke ? (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuGroup>
                            <DropdownMenuItem
                              variant="destructive"
                              onClick={() => onRevoke(key)}
                            >
                              {t("webhookKeys.revoke")}
                            </DropdownMenuItem>
                          </DropdownMenuGroup>
                        </>
                      ) : null}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              )
            },
          } satisfies ColumnDef<WebhookKeyDto, unknown>,
        ]
      : []),
  ]
}
