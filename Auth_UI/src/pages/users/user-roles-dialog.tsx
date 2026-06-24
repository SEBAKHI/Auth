import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Loader2, Plus, X } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { api } from "@/lib/api/client"
import { unwrap } from "@/lib/api/helpers"
import { getErrorMessage } from "@/lib/errors"
import type { Schemas } from "@/lib/api/types"

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

  const assignedRoleIds = new Set(
    (rolesQuery.data ?? []).map((role) => role.roleId)
  )
  const availableRoles = (allRolesQuery.data ?? []).filter(
    (role) => role.id && !assignedRoleIds.has(role.id)
  )

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: ["users", userId, "roles"] })

  const assignMutation = useMutation({
    mutationFn: async (roleId: string) => {
      const { error } = await api.POST("/api/v1/Users/{id}/roles", {
        params: { path: { id: userId } },
        body: { roleId },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      void queryClient.invalidateQueries({ queryKey: ["users"] })
      setSelectedRole(undefined)
      toast.success(t("users.roleAssigned"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const removeMutation = useMutation({
    mutationFn: async (roleId: string) => {
      const { error } = await api.DELETE("/api/v1/Users/{id}/roles/{roleId}", {
        params: { path: { id: userId, roleId } },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      void queryClient.invalidateQueries({ queryKey: ["users"] })
      toast.success(t("users.roleRemoved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const roles = rolesQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("users.manageRoles")}</DialogTitle>
          <DialogDescription>{user.email}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <Select value={selectedRole} onValueChange={setSelectedRole}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder={t("users.assignRole")} />
                </SelectTrigger>
                <SelectContent>
                  {availableRoles.map((role) => (
                    <SelectItem key={role.id} value={role.id as string}>
                      {role.name}
                      {role.applicationId ? null : ` (${t("nav.platform")})`}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Button
              onClick={() =>
                selectedRole && assignMutation.mutate(selectedRole)
              }
              disabled={!selectedRole || assignMutation.isPending}
            >
              {assignMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : (
                <Plus />
              )}
              {t("users.assignRole")}
            </Button>
          </div>

          <div className="min-h-24 rounded-lg border p-3">
            {rolesQuery.isLoading ? (
              <div className="flex flex-wrap gap-2">
                {Array.from({ length: 3 }).map((_, i) => (
                  <Skeleton key={i} className="h-6 w-20" />
                ))}
              </div>
            ) : roles.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                {t("users.noRoles")}
              </p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {roles.map((role) => (
                  <Badge
                    key={role.id}
                    variant="secondary"
                    className="gap-1 pe-1"
                  >
                    {role.roleName}
                    <button
                      type="button"
                      className="rounded-full p-0.5 hover:bg-foreground/10 disabled:opacity-50"
                      aria-label={t("common.remove")}
                      disabled={removeMutation.isPending}
                      onClick={() =>
                        role.roleId && removeMutation.mutate(role.roleId)
                      }
                    >
                      <X className="size-3" />
                    </button>
                  </Badge>
                ))}
              </div>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  )
}
