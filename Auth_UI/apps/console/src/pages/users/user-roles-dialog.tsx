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

interface RoleDraft {
  roleId: string
  expiresAt: string | null
}

export function UserRolesDialog({
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
  const [selectedRole, setSelectedRole] = React.useState<string>()
  const [expiresAt, setExpiresAt] = React.useState("")

  const rolesQuery = useQuery({
    queryKey: ["users", userId, "roles"],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Users/{id}/roles", {
          params: { path: { id: userId } },
        })
      ),
  })

  const allRolesQuery = useQuery({
    queryKey: ["roles", "all"],
    enabled: open,
    queryFn: () => unwrap(api.GET("/api/v1/Roles", { params: { query: {} } })),
  })

  const roleLabel = (role: Schemas["RoleDto"]) =>
    `${role.name}${
      role.applicationName
        ? ` (${role.applicationName})`
        : role.applicationId
          ? ""
          : ` (${t("nav.platform")})`
    }`

  const items = (rolesQuery.data ?? []).map((role) => ({
    key: role.roleId as string,
    label: role.roleName as string,
  }))

  return (
    <AssignmentDialog<RoleDraft>
      open={open}
      onOpenChange={onOpenChange}
      title={t("users.manageRoles")}
      description={user.email}
      items={items}
      loading={rolesQuery.isLoading}
      emptyLabel={t("users.noRoles")}
      assignedLabel={t("users.manageRoles")}
      picker={({ assignedKeys, add }) => {
        const available = (allRolesQuery.data ?? []).filter(
          (role) => role.id && !assignedKeys.has(role.id)
        )

        return (
          <AssignmentPicker
            addLabel={t("users.assignRole")}
            canAdd={Boolean(selectedRole)}
            expiresAt={expiresAt}
            onExpiresAtChange={setExpiresAt}
            onAdd={() => {
              const role = available.find((item) => item.id === selectedRole)
              if (!role?.id) return
              add({
                key: role.id,
                label: roleLabel(role),
                draft: {
                  roleId: role.id,
                  expiresAt: expiresAt
                    ? new Date(expiresAt).toISOString()
                    : null,
                },
              })
              setSelectedRole(undefined)
              setExpiresAt("")
            }}
          >
            {/* Searchable for the same reason the permission picker is: the
                list grows with every role anyone defines, and a Select offers
                no way to reach one except scrolling. Role names follow the
                interface language, so no direction override here. */}
            <SearchableSelect
              value={selectedRole}
              options={available.map((role) => ({
                id: role.id,
                label: roleLabel(role),
                description: role.description ?? undefined,
              }))}
              onChange={setSelectedRole}
              placeholder={t("users.assignRole")}
            />
          </AssignmentPicker>
        )
      }}
      onAdd={async (draft) => {
        const { error } = await api.POST("/api/v1/Users/{id}/roles", {
          params: { path: { id: userId } },
          body: { roleId: draft.roleId, expiresAt: draft.expiresAt },
        })
        if (error) throw error
      }}
      onRemove={async (roleId) => {
        const { error } = await api.DELETE("/api/v1/Users/{id}/roles/{roleId}", {
          params: { path: { id: userId, roleId } },
        })
        if (error) throw error
      }}
      onApplied={() => {
        void queryClient.invalidateQueries({
          queryKey: ["users", userId, "roles"],
        })
        void queryClient.invalidateQueries({ queryKey: ["users"] })
      }}
    />
  )
}
