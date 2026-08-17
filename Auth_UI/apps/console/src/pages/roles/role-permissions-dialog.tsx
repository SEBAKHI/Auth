import { useQuery, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"

import {
  AssignmentDialog,
  AssignmentPicker,
} from "@authsystem/ui/common/assignment-dialog"
import { SearchableSelect } from "@authsystem/ui/common/searchable-select"
import { api } from "@authsystem/api/client"
import { unwrap } from "@authsystem/api/helpers"

// The two labels below live under `users.` because that dialog needed them
// first. They are generic ("Manage permissions", "Grant permission") and are
// reused verbatim rather than duplicated under `roles.`; the key names are due
// a move to `common.`, which is a rename, not a second string.

interface PermissionDraft {
  permissionId: string
}

/**
 * Edits what a role grants.
 *
 * Until this existed a role's permission set was fixed at creation — and in
 * practice fixed forever, because the create dialog never sent one either. The
 * only way to change what a role could do was to edit RolePermissions in the
 * database by hand.
 *
 * Deliberately the same shape as UserPermissionsDialog, down to the shared
 * AssignmentDialog, AssignmentPicker and SearchableSelect: the two differ in
 * which endpoints they call and in nothing a reader should have to relearn.
 */
export function RolePermissionsDialog({
  open,
  onOpenChange,
  roleId,
  roleName,
  grantedCodes,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  roleId: string
  roleName: string
  /**
   * Codes the role already holds. The role detail response carries codes rather
   * than ids, so they are matched against the catalogue below — cheaper than a
   * second endpoint returning the same rows a different way.
   */
  grantedCodes: string[]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [selected, setSelected] = React.useState<string>()

  const allQuery = useQuery({
    queryKey: ["permissions", "all"],
    enabled: open,
    queryFn: () =>
      unwrap(api.GET("/api/v1/Permissions", { params: { query: {} } })),
  })

  const catalogue = React.useMemo(() => allQuery.data ?? [], [allQuery.data])

  const items = React.useMemo(() => {
    const byCode = new Map(catalogue.map((p) => [p.code, p]));
    return grantedCodes.map((code) => ({
      // A code with no catalogue entry keeps its own name as the key: it can
      // still be listed and removed, which matters precisely for the rows that
      // should not be there.
      key: (byCode.get(code)?.id ?? code) as string,
      label: code,
    }))
  }, [catalogue, grantedCodes])

  return (
    <AssignmentDialog<PermissionDraft>
      open={open}
      onOpenChange={onOpenChange}
      title={t("users.managePermissions")}
      description={roleName}
      items={items}
      loading={allQuery.isLoading}
      emptyLabel={t("common.empty")}
      assignedLabel={t("users.managePermissions")}
      picker={({ assignedKeys, add }) => {
        const available = catalogue.filter(
          (permission) => permission.id && !assignedKeys.has(permission.id)
        )

        return (
          <AssignmentPicker
            addLabel={t("users.grantPermission")}
            canAdd={Boolean(selected)}
            onAdd={() => {
              const permission = available.find((item) => item.id === selected)
              if (!permission?.id) return
              add({
                key: permission.id,
                label: permission.code as string,
                draft: { permissionId: permission.id },
              })
              setSelected(undefined)
            }}
          >
            {/* Isolation, not alignment: the code keeps its own character
                order, the column keeps the page's. */}
            <SearchableSelect
              value={selected}
              options={available.map((permission) => ({
                id: permission.id,
                label: permission.code,
                description: permission.name ?? undefined,
              }))}
              onChange={setSelected}
              placeholder={t("users.grantPermission")}
              ltrLabel
            />
          </AssignmentPicker>
        )
      }}
      onAdd={async (draft) => {
        const { error } = await api.POST("/api/v1/Roles/{id}/permissions", {
          params: { path: { id: roleId } },
          body: { permissionId: draft.permissionId },
        })
        if (error) throw error
      }}
      onRemove={async (permissionId) => {
        const { error } = await api.DELETE(
          "/api/v1/Roles/{id}/permissions/{permissionId}",
          { params: { path: { id: roleId, permissionId } } }
        )
        if (error) throw error
      }}
      onApplied={() => {
        // The role's own row carries the codes, and every token minted from
        // this role changes meaning — so the user lists go too.
        void queryClient.invalidateQueries({ queryKey: ["roles"] })
        void queryClient.invalidateQueries({ queryKey: ["users"] })
      }}
    />
  )
}
