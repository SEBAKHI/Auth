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

  const grantedIds = new Set(
    (grantedQuery.data ?? []).map((perm) => perm.permissionId)
  )
  const available = (allQuery.data ?? []).filter(
    (perm) => perm.id && !grantedIds.has(perm.id)
  )

  const invalidate = () =>
    queryClient.invalidateQueries({
      queryKey: ["users", userId, "permissions"],
    })

  const grantMutation = useMutation({
    mutationFn: async (permissionId: string) => {
      const { error } = await api.POST("/api/v1/Users/{id}/permissions", {
        params: { path: { id: userId } },
        body: { permissionId },
      })
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      setSelected(undefined)
      toast.success(t("users.permissionGranted"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const revokeMutation = useMutation({
    mutationFn: async (permissionId: string) => {
      const { error } = await api.DELETE(
        "/api/v1/Users/{id}/permissions/{permissionId}",
        { params: { path: { id: userId, permissionId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("users.permissionRevoked"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const granted = grantedQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("users.managePermissions")}</DialogTitle>
          <DialogDescription>{user.email}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <Select value={selected} onValueChange={setSelected}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder={t("users.grantPermission")} />
                </SelectTrigger>
                <SelectContent>
                  {available.map((perm) => (
                    <SelectItem key={perm.id} value={perm.id as string}>
                      {perm.code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Button
              onClick={() => selected && grantMutation.mutate(selected)}
              disabled={!selected || grantMutation.isPending}
            >
              {grantMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : (
                <Plus />
              )}
              {t("users.grantPermission")}
            </Button>
          </div>

          <div className="min-h-24 rounded-lg border p-3">
            {grantedQuery.isLoading ? (
              <div className="flex flex-wrap gap-2">
                {Array.from({ length: 3 }).map((_, i) => (
                  <Skeleton key={i} className="h-6 w-24" />
                ))}
              </div>
            ) : granted.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                {t("users.noPermissions")}
              </p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {granted.map((perm) => (
                  <Badge
                    key={perm.id}
                    variant="secondary"
                    className="gap-1 pe-1"
                  >
                    {perm.permissionCode}
                    <button
                      type="button"
                      className="rounded-full p-0.5 hover:bg-foreground/10 disabled:opacity-50"
                      aria-label={t("common.remove")}
                      disabled={revokeMutation.isPending}
                      onClick={() =>
                        perm.permissionId &&
                        revokeMutation.mutate(perm.permissionId)
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
