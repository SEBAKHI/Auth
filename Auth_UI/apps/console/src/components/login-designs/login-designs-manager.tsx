import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import type { ColumnFiltersState } from "@tanstack/react-table"
import { Plus } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"
import { z } from "zod"

import { getErrorMessage } from "@authsystem/api/errors"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { FormDialog } from "@authsystem/ui/common/form-dialog"
import { Button } from "@authsystem/ui/button"
import { DataTable } from "@authsystem/ui/data-table/data-table"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import {
  enumArrayUrlFilter,
  useListUrlState,
  type ListUrlStateOptions,
} from "@authsystem/ui/hooks/use-search-query"
import { Input } from "@authsystem/ui/input"

import { DEFAULT_PAGE_SIZE } from "@/lib/constants"
import { loginDesignHref } from "@/lib/record-hrefs"

import {
  copyDesign,
  createDesign,
  deleteDesign,
  designsKey,
  listDesigns,
  setDesignActive,
  type DesignScope,
} from "./login-design-api"
import {
  LOGIN_DESIGN_SORTABLE_COLUMNS,
  useLoginDesignColumns,
  type LoginDesignAction,
} from "./login-design-columns"

type DesignListFilters = {
  status: Array<"active" | "inactive">
  logo: Array<"shown" | "hidden">
}

/**
 * The list's state in the URL: its search, sort and filter chips survive a visit to a
 * design's page and back, and a reload. Namespaced, so they never meet the page's own
 * `tab` parameter or another table on the same page.
 */
function designListUrlOptions(namespace: string) {
  return {
    namespace,
    defaultPageSize: DEFAULT_PAGE_SIZE,
    sortableColumns: LOGIN_DESIGN_SORTABLE_COLUMNS,
    defaultSorting: [],
    filters: {
      status: enumArrayUrlFilter(["active", "inactive"], "status"),
      logo: enumArrayUrlFilter(["shown", "hidden"], "logo"),
    },
  } satisfies ListUrlStateOptions<DesignListFilters>
}

/**
 * A scope's sign-in page designs, the way email templates are listed: a table whose rows
 * open each design on its own page. Serves an application's "Templates" tab and the
 * platform settings "Templates" tab alike (`scope`), each with its own `tableId` and URL
 * namespace; the columns are one definition (`useLoginDesignColumns`).
 *
 * There is no draft: saving the active design is what visitors see next, a new design and
 * a copy start inactive, and only "Activate" makes one visible. The active design cannot
 * be deleted; its menu item says why instead of going missing.
 */
export function LoginDesignsManager({
  scope,
  canManage,
  tableId,
  urlNamespace,
}: {
  scope: DesignScope
  canManage: boolean
  /** Where this surface keeps its column choices; never shared with the other surface. */
  tableId: string
  /** The prefix of this list's URL parameters. */
  urlNamespace: string
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [pending, setPending] = React.useState<LoginDesignAction | null>(null)
  const [createOpen, setCreateOpen] = React.useState(false)

  const urlOptions = React.useMemo(() => designListUrlOptions(urlNamespace), [urlNamespace])
  const { search, sorting, filters, setSearch, setSorting, setFilters } = useListUrlState(urlOptions)
  const columnFilters: ColumnFiltersState = [
    ...(filters.status.length ? [{ id: "status", value: filters.status }] : []),
    ...(filters.logo.length ? [{ id: "showPlatformLogo", value: filters.logo }] : []),
  ]
  const onColumnFiltersChange = (next: ColumnFiltersState) =>
    setFilters({
      status: (next.find((filter) => filter.id === "status")?.value as DesignListFilters["status"]) ?? [],
      logo: (next.find((filter) => filter.id === "showPlatformLogo")?.value as DesignListFilters["logo"]) ?? [],
    })

  const list = useQuery({ queryKey: designsKey(scope), queryFn: () => listDesigns(scope) })
  const designs = list.data ?? []
  const active = designs.find((design) => design.isActive) ?? null

  const refresh = React.useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: designsKey(scope) })
    // The console's own sign-in page shows the platform's design.
    if (scope.kind === "platform") void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
  }, [queryClient, scope])

  const create = useMutation({
    mutationFn: (name: string) => createDesign(scope, name),
    onSuccess: (design) => {
      setCreateOpen(false)
      toast.success(t("loginDesigns.created"))
      refresh()
      const href = loginDesignHref(scope, design.id)
      if (href) navigate(href)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const act = useMutation({
    mutationFn: async ({ action, name }: { action: LoginDesignAction; name: string }) => {
      switch (action.kind) {
        case "copy":
          return copyDesign(scope, action.design.id, name)
        case "activate":
          return setDesignActive(scope, action.design.id, true)
        case "deactivate":
          return setDesignActive(scope, action.design.id, false)
        case "delete":
          return deleteDesign(scope, action.design.id)
      }
    },
    onSuccess: (_result, { action }) => {
      setPending(null)
      const name = action.design.name
      toast.success(
        action.kind === "copy"
          ? t("loginDesigns.copied")
          : action.kind === "activate"
            ? t("loginDesigns.activated", { name })
            : action.kind === "deactivate"
              ? t("loginDesigns.deactivated", { name })
              : t("loginDesigns.deleted")
      )
      refresh()
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const columns = useLoginDesignColumns({ scope, canManage, onAction: setPending })

  const fallback =
    scope.kind === "application" ? t("loginDesigns.visitorsSeePlatform") : t("loginDesigns.visitorsSeePlain")

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          {active ? t("loginDesigns.visitorsSee", { name: active.name }) : fallback}
        </p>
        {canManage ? (
          <Button onClick={() => setCreateOpen(true)}>
            <Plus data-icon="inline-start" />
            {t("loginDesigns.newDesign")}
          </Button>
        ) : null}
      </div>

      <DataTable
        tableId={tableId}
        columns={columns}
        data={designs}
        isLoading={list.isLoading}
        error={list.isError ? list.error : undefined}
        onRetry={() => void list.refetch()}
        // The fallback sentence is already above the table.
        emptyMessage={t("loginDesigns.empty")}
        globalSearch
        globalFilter={search}
        onGlobalFilterChange={setSearch}
        searchPlaceholder={t("loginDesigns.searchPlaceholder")}
        columnFilters={columnFilters}
        onColumnFiltersChange={onColumnFiltersChange}
        sorting={sorting}
        onSortingChange={setSorting}
        enableRowDetail={false}
        exportFileName={tableId}
      />

      {createOpen ? (
        <NameDialog
          title={t("loginDesigns.newDesign")}
          description={t("loginDesigns.newDesignDescription")}
          submitLabel={t("loginDesigns.create")}
          initialName=""
          pending={create.isPending}
          onOpenChange={setCreateOpen}
          onSubmit={(name) => create.mutate(name)}
        />
      ) : null}

      {pending?.kind === "copy" ? (
        <NameDialog
          title={t("loginDesigns.copyTitle", { name: pending.design.name })}
          description={t("loginDesigns.copyDescription")}
          submitLabel={t("loginDesigns.copy")}
          initialName={t("loginDesigns.copyNameDefault", { name: pending.design.name })}
          pending={act.isPending}
          onOpenChange={(open) => {
            if (!open) setPending(null)
          }}
          onSubmit={(name) => act.mutate({ action: pending, name })}
        />
      ) : null}

      <ConfirmDialog
        open={pending !== null && pending.kind !== "copy"}
        onOpenChange={(open) => {
          if (!open && !act.isPending) setPending(null)
        }}
        title={
          pending?.kind === "activate"
            ? t("loginDesigns.activateTitle", { name: pending.design.name })
            : pending?.kind === "deactivate"
              ? t("loginDesigns.deactivateTitle", { name: pending.design.name })
              : t("loginDesigns.deleteTitle", { name: pending?.design.name ?? "" })
        }
        description={
          pending?.kind === "activate"
            ? scope.kind === "application"
              ? t("loginDesigns.activateDescriptionApp")
              : t("loginDesigns.activateDescriptionPlatform")
            : pending?.kind === "deactivate"
              ? scope.kind === "application"
                ? t("loginDesigns.deactivateDescriptionApp")
                : t("loginDesigns.deactivateDescriptionPlatform")
              : t("loginDesigns.deleteDescription")
        }
        confirmLabel={
          pending?.kind === "activate"
            ? t("loginDesigns.activate")
            : pending?.kind === "deactivate"
              ? t("loginDesigns.deactivate")
              : t("loginDesigns.delete")
        }
        destructive={pending?.kind === "delete"}
        loading={act.isPending}
        onConfirm={() => {
          if (pending) act.mutate({ action: pending, name: pending.design.name })
        }}
      />
    </div>
  )
}

/** The name of a new design or of a copy. */
function NameDialog({
  title,
  description,
  submitLabel,
  initialName,
  pending,
  onOpenChange,
  onSubmit,
}: {
  title: string
  description: string
  submitLabel: string
  initialName: string
  pending: boolean
  onOpenChange: (open: boolean) => void
  onSubmit: (name: string) => void
}) {
  const { t } = useTranslation()
  const schema = React.useMemo(
    () => z.object({ name: z.string().trim().min(1, t("loginDesigns.nameRequired")) }),
    [t]
  )
  const form = useForm<{ name: string }>({
    resolver: zodResolver(schema),
    defaultValues: { name: initialName },
  })

  return (
    <FormDialog
      open
      onOpenChange={onOpenChange}
      form={form}
      formId="login-design-name"
      title={title}
      description={description}
      submitLabel={submitLabel}
      pending={pending}
      onSubmit={(values) => onSubmit(values.name.trim())}
    >
      <FormField
        control={form.control}
        name="name"
        render={({ field }) => (
          <FormItem>
            <FormLabel>{t("loginDesigns.name")}</FormLabel>
            <FormControl>
              <Input autoComplete="off" {...field} />
            </FormControl>
            <FormDescription>{t("loginDesigns.nameHint")}</FormDescription>
            <FormMessage />
          </FormItem>
        )}
      />
    </FormDialog>
  )
}
