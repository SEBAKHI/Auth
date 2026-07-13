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
} from "@astoom/ui/dialog"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { Input } from "@astoom/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"
import { Skeleton } from "@astoom/ui/skeleton"
import { api } from "@astoom/api/client"
import { unwrap } from "@astoom/api/helpers"
import { getErrorMessage } from "@astoom/api/errors"
import type { Schemas } from "@astoom/api/types"

/**
 * Manages a member's app-scoped role assignments within an organization:
 * assign a role of an org-enabled application, or remove an existing one.
 */
export function MemberAppRolesDialog({
  open,
  onOpenChange,
  orgId,
  member,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  orgId: string
  member: Schemas["OrganizationMemberDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const userId = member.userId as string
  const [selectedApp, setSelectedApp] = React.useState<string>()
  const [selectedRole, setSelectedRole] = React.useState<string>()
  const [expiresAt, setExpiresAt] = React.useState("")

  const assignmentsQuery = useQuery({
    queryKey: ["org-member-app-roles", orgId, userId],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{orgId}/members/{userId}/roles", {
          params: { path: { orgId, userId } },
        })
      ),
  })

  const appsQuery = useQuery({
    queryKey: ["org-apps", orgId],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Organizations/{id}/applications", {
          params: { path: { id: orgId } },
        })
      ),
  })

  const rolesQuery = useQuery({
    queryKey: ["applications", selectedApp, "roles"],
    enabled: open && Boolean(selectedApp),
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Applications/{id}/roles", {
          params: { path: { id: selectedApp as string } },
        })
      ),
  })

  const assignments = assignmentsQuery.data ?? []
  const enabledApps = (appsQuery.data ?? []).filter(
    (app) => app.isActive && app.applicationId
  )
  const assignedRoleIds = new Set(assignments.map((role) => role.roleId))
  const availableRoles = (rolesQuery.data ?? []).filter(
    (role) => role.id && !assignedRoleIds.has(role.id)
  )

  const invalidate = () => {
    void queryClient.invalidateQueries({
      queryKey: ["org-member-app-roles", orgId, userId],
    })
    // Assignment changes move the applications tab's assigned-user counts.
    void queryClient.invalidateQueries({ queryKey: ["org-apps", orgId] })
  }

  const assignMutation = useMutation({
    mutationFn: async ({
      applicationId,
      roleId,
    }: {
      applicationId: string
      roleId: string
    }) => {
      const { error } = await api.POST(
        "/api/v1/Organizations/{orgId}/members/{userId}/roles",
        {
          params: { path: { orgId, userId } },
          body: {
            applicationId,
            roleId,
            expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
          },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      setSelectedRole(undefined)
      setExpiresAt("")
      toast.success(t("organizations.appRoleAssigned"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const removeMutation = useMutation({
    mutationFn: async (roleId: string) => {
      const { error } = await api.DELETE(
        "/api/v1/Organizations/{orgId}/members/{userId}/roles/{roleId}",
        { params: { path: { orgId, userId, roleId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      invalidate()
      toast.success(t("organizations.appRoleRemoved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("organizations.manageAppRoles")}</DialogTitle>
          <DialogDescription>{member.email}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <Select
                value={selectedApp}
                onValueChange={(value) => {
                  setSelectedApp(value)
                  setSelectedRole(undefined)
                }}
              >
                <SelectTrigger className="w-full">
                  <SelectValue
                    placeholder={t("organizations.selectApplication")}
                  />
                </SelectTrigger>
                <SelectContent>
                  {enabledApps.map((app) => (
                    <SelectItem
                      key={app.applicationId}
                      value={app.applicationId as string}
                    >
                      {app.applicationName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex-1">
              <Select
                value={selectedRole}
                onValueChange={setSelectedRole}
                disabled={!selectedApp}
              >
                <SelectTrigger className="w-full">
                  <SelectValue placeholder={t("organizations.selectRole")} />
                </SelectTrigger>
                <SelectContent>
                  {availableRoles.map((role) => (
                    <SelectItem key={role.id} value={role.id as string}>
                      {role.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Input
              type="date"
              className="w-40"
              value={expiresAt}
              onChange={(e) => setExpiresAt(e.target.value)}
              aria-label={t("common.expiresAt")}
            />
            <Button
              onClick={() =>
                selectedApp &&
                selectedRole &&
                assignMutation.mutate({
                  applicationId: selectedApp,
                  roleId: selectedRole,
                })
              }
              disabled={!selectedApp || !selectedRole || assignMutation.isPending}
            >
              {assignMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : (
                <Plus />
              )}
              {t("organizations.assignAppRole")}
            </Button>
          </div>

          <div className="min-h-24 rounded-lg border p-3">
            {assignmentsQuery.isLoading ? (
              <div className="flex flex-wrap gap-2">
                {Array.from({ length: 3 }).map((_, i) => (
                  <Skeleton key={i} className="h-6 w-24" />
                ))}
              </div>
            ) : assignments.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                {t("organizations.noAppRoles")}
              </p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {assignments.map((assignment) => (
                  <Badge
                    key={assignment.id}
                    variant="secondary"
                    className="gap-1 pe-1"
                  >
                    {assignment.applicationName} · {assignment.roleName}
                    <button
                      type="button"
                      className="rounded-full p-0.5 hover:bg-foreground/10 disabled:opacity-50"
                      aria-label={t("common.remove")}
                      disabled={removeMutation.isPending}
                      onClick={() =>
                        assignment.roleId &&
                        removeMutation.mutate(assignment.roleId)
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
