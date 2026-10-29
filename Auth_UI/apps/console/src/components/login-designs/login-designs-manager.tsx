import { zodResolver } from "@hookform/resolvers/zod"
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { MoreHorizontal, Plus } from "lucide-react"
import * as React from "react"
import { useForm } from "react-hook-form"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"
import { z } from "zod"

import { getErrorMessage } from "@authsystem/api/errors"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { BRANDING_QUERY_KEY } from "@authsystem/ui/branding"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import { FormDialog } from "@authsystem/ui/common/form-dialog"
import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@authsystem/ui/dropdown-menu"
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@authsystem/ui/empty"
import {
  FormControl,
  FormDescription,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@authsystem/ui/form"
import { formatDateTime } from "@authsystem/ui/format"
import { Input } from "@authsystem/ui/input"
import { Item, ItemActions, ItemContent, ItemDescription, ItemGroup, ItemTitle } from "@authsystem/ui/item"
import { Skeleton } from "@authsystem/ui/skeleton"
import type { ThemeConfig } from "@authsystem/ui/theme/theme-config"

import {
  copyDesign,
  createDesign,
  deleteDesign,
  designsKey,
  listDesigns,
  setDesignActive,
  type DesignScope,
  type LoginDesignSummary,
} from "./login-design-api"
import { LoginDesignEditor } from "./login-design-editor"

type Pending =
  | { kind: "copy"; design: LoginDesignSummary }
  | { kind: "activate"; design: LoginDesignSummary }
  | { kind: "deactivate"; design: LoginDesignSummary }
  | { kind: "delete"; design: LoginDesignSummary }

/**
 * A scope's sign-in page designs, the way notification templates are managed: any number
 * of designs, one active or none, each edited in place. Serves an application's
 * "Templates" tab and the platform settings card alike (`scope`).
 *
 * There is no draft: saving the active design is what visitors see next, a new design
 * and a copy start inactive, and only "Activate" makes one visible. The active design
 * cannot be deleted; its row says why instead of offering the action.
 */
export function LoginDesignsManager({
  scope,
  canManage,
  appearance,
}: {
  scope: DesignScope
  canManage: boolean
  /**
   * The appearance designs follow in the preview: an application's own while it is
   * switched on; null, the platform's (the console's own).
   */
  appearance: ThemeConfig | null
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [editing, setEditing] = React.useState<string | null>(null)
  const [pending, setPending] = React.useState<Pending | null>(null)
  const [createOpen, setCreateOpen] = React.useState(false)

  const list = useQuery({ queryKey: designsKey(scope), queryFn: () => listDesigns(scope) })
  const designs = list.data ?? []
  const active = designs.find((design) => design.isActive) ?? null

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: designsKey(scope) })
    // The console's own sign-in page shows the platform's design.
    if (scope.kind === "platform") void queryClient.invalidateQueries({ queryKey: BRANDING_QUERY_KEY })
  }

  const create = useMutation({
    mutationFn: (name: string) => createDesign(scope, name),
    onSuccess: (design) => {
      setCreateOpen(false)
      toast.success(t("loginDesigns.created"))
      refresh()
      setEditing(design.id)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const act = useMutation({
    mutationFn: async ({ action, name }: { action: Pending; name: string }) => {
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

  if (editing) {
    return (
      <LoginDesignEditor
        key={editing}
        scope={scope}
        designId={editing}
        canManage={canManage}
        appearance={appearance}
        onClose={() => {
          setEditing(null)
          refresh()
        }}
        onSaved={refresh}
      />
    )
  }

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

      {list.isLoading ? (
        <Skeleton className="h-32 w-full" />
      ) : list.isError ? (
        <Alert variant="destructive">
          <AlertDescription>{getErrorMessage(list.error, t("loginDesigns.loadFailed"))}</AlertDescription>
        </Alert>
      ) : designs.length === 0 ? (
        <Empty className="border">
          <EmptyHeader>
            <EmptyTitle>{t("loginDesigns.empty")}</EmptyTitle>
            <EmptyDescription>{fallback}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      ) : (
        <ItemGroup className="gap-2">
          {designs.map((design) => (
            <Item key={design.id} variant="outline">
              <ItemContent>
                <ItemTitle>
                  {/* A design's name is the administrator's text in any language. */}
                  <bdi className="wrap-anywhere">{design.name}</bdi>
                  {design.isActive ? <Badge>{t("loginDesigns.active")}</Badge> : null}
                  {design.showPlatformLogo ? null : (
                    <Badge variant="outline">{t("loginDesigns.hidesPlatformLogo")}</Badge>
                  )}
                </ItemTitle>
                <ItemDescription>
                  {design.isActive && canManage
                    ? t("loginDesigns.deleteActiveHint")
                    : t("loginDesigns.changedAt", {
                        date: formatDateTime(design.modifiedAt ?? design.createdAt),
                      })}
                </ItemDescription>
              </ItemContent>
              <ItemActions>
                <Button variant="outline" size="sm" onClick={() => setEditing(design.id)}>
                  {canManage ? t("loginDesigns.edit") : t("loginDesigns.view")}
                </Button>
                {canManage ? (
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        size="icon-sm"
                        aria-label={t("loginDesigns.actions", { name: design.name })}
                      >
                        <MoreHorizontal />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuGroup>
                        {design.isActive ? (
                          <DropdownMenuItem onSelect={() => setPending({ kind: "deactivate", design })}>
                            {t("loginDesigns.deactivate")}
                          </DropdownMenuItem>
                        ) : (
                          <DropdownMenuItem onSelect={() => setPending({ kind: "activate", design })}>
                            {t("loginDesigns.activate")}
                          </DropdownMenuItem>
                        )}
                        <DropdownMenuItem onSelect={() => setPending({ kind: "copy", design })}>
                          {t("loginDesigns.copy")}
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
                      {design.isActive ? null : (
                        <>
                          <DropdownMenuSeparator />
                          <DropdownMenuGroup>
                            <DropdownMenuItem
                              variant="destructive"
                              onSelect={() => setPending({ kind: "delete", design })}
                            >
                              {t("loginDesigns.delete")}
                            </DropdownMenuItem>
                          </DropdownMenuGroup>
                        </>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                ) : null}
              </ItemActions>
            </Item>
          ))}
        </ItemGroup>
      )}

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
