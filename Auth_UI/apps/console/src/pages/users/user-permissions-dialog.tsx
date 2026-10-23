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
import type { Schemas } from "@authsystem/api/types"

import {
  heldIds,
  parseScopedAssignmentKey,
  pickedAssignmentKey,
  scopedAssignmentKey,
} from "./scoped-assignment-key"

interface PermissionDraft {
  permissionId: string
  expiresAt: string | null
}

export function UserPermissionsDialog({
  open,
  onOpenChange,
  user,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  user: Schemas["UserDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const userId = user.id as string
  const [selected, setSelected] = React.useState<string>()
  const [expiresAt, setExpiresAt] = React.useState("")

  const grantedQuery = useQuery({
    queryKey: ["users", userId, "permissions"],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/permissions", {
          params: { path: { id: userId } },
        })
      ),
  })

  const allQuery = useQuery({
    queryKey: ["permissions", "all"],
    enabled: open,
    queryFn: () =>
      unwrap(api.GET("/api/v1/Permissions", { params: { query: {} } })),
  })

  // One chip per grant, and its application named: the API grants a permission
  // inside one application too, and the chip says which grant its ✕ revokes.
  const items = (grantedQuery.data ?? []).map((permission) => {
    const scope = permission.applicationName ?? permission.applicationCode
    return {
      key: scopedAssignmentKey(
        permission.permissionId as string,
        permission.applicationId
      ),
      label: scope
        ? `${permission.permissionCode as string} (${scope})`
        : (permission.permissionCode as string),
    }
  })
  const serverKeys = items.map((item) => item.key)

  return (
    <AssignmentDialog<PermissionDraft>
      open={open}
      onOpenChange={onOpenChange}
      title={t("users.managePermissions")}
      description={user.email}
      items={items}
      loading={grantedQuery.isLoading}
      emptyLabel={t("users.noPermissions")}
      assignedLabel={t("users.managePermissions")}
      picker={({ assignedKeys, add }) => {
        const held = heldIds(assignedKeys)
        const available = (allQuery.data ?? []).filter(
          (permission) => permission.id && !held.has(permission.id)
        )

        return (
          <AssignmentPicker
            addLabel={t("users.grantPermission")}
            canAdd={Boolean(selected)}
            expiresAt={expiresAt}
            onExpiresAtChange={setExpiresAt}
            onAdd={() => {
              const permission = available.find((item) => item.id === selected)
              if (!permission?.id) return
              add({
                key: pickedAssignmentKey(serverKeys, permission.id),
                label: permission.code as string,
                draft: {
                  permissionId: permission.id,
                  expiresAt: expiresAt
                    ? new Date(expiresAt).toISOString()
                    : null,
                },
              })
              setSelected(undefined)
              setExpiresAt("")
            }}
          >
            {/*
              A searchable picker rather than a Select: with every enforced code
              seeded the catalogue runs past fifty entries, and a Select offers
              no way to reach one except scrolling past the rest.
            */}
            <SearchableSelect
              value={selected}
              options={available.map((permission) => ({
                id: permission.id,
                label: permission.code,
                description: permission.name ?? undefined,
              }))}
              onChange={setSelected}
              placeholder={t("users.grantPermission")}
            />
          </AssignmentPicker>
        )
      }}
      onAdd={async (draft) => {
        const { error } = await api.POST("/api/v1/Users/{id}/permissions", {
          params: { path: { id: userId } },
          body: {
            permissionId: draft.permissionId,
            expiresAt: draft.expiresAt,
          },
        })
        if (error) throw error
      }}
      onRemove={async (key) => {
        const { id: permissionId, applicationId } =
          parseScopedAssignmentKey(key)
        const { error } = await api.DELETE(
          "/api/v1/Users/{id}/permissions/{permissionId}",
          {
            params: {
              path: { id: userId, permissionId },
              query: applicationId ? { applicationId } : undefined,
            },
          }
        )
        if (error) throw error
      }}
      onApplied={() =>
        void queryClient.invalidateQueries({
          queryKey: ["users", userId, "permissions"],
        })
      }
    />
  )
}
