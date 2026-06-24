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

export function PermissionImplicationsDialog({
  open,
  onOpenChange,
  permission,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  permission: Schemas["PermissionDto"]
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const permissionId = permission.id as string
  const [selected, setSelected] = React.useState<string>()

  const impliedQuery = useQuery({
    queryKey: ["permissions", permissionId, "implications"],
    enabled: open,
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/Permissions/{id}/implications", {
          params: { path: { id: permissionId } },
        })
      ),
  })

  const allQuery = useQuery({
    queryKey: ["permissions", "all"],
    enabled: open,
    queryFn: () =>
      unwrap(api.GET("/api/v1/Permissions", { params: { query: {} } })),
  })

  const impliedIds = new Set((impliedQuery.data ?? []).map((p) => p.id))
  const available = (allQuery.data ?? []).filter(
    (p) => p.id && p.id !== permissionId && !impliedIds.has(p.id)
  )

  const invalidate = () =>
    queryClient.invalidateQueries({
      queryKey: ["permissions", permissionId, "implications"],
    })

  const addMutation = useMutation({
    mutationFn: async (impliedPermissionId: string) => {
      const { error } = await api.POST(
        "/api/v1/Permissions/{id}/implications",
        {
          params: { path: { id: permissionId } },
          body: { impliedPermissionId },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      setSelected(undefined)
      toast.success(t("permissions.implicationAdded"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const removeMutation = useMutation({
    mutationFn: async (impliedId: string) => {
      const { error } = await api.DELETE(
        "/api/v1/Permissions/{id}/implications/{impliedId}",
        { params: { path: { id: permissionId, impliedId } } }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void invalidate()
      toast.success(t("permissions.implicationRemoved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const implied = impliedQuery.data ?? []

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("permissions.implications")}</DialogTitle>
          <DialogDescription>{permission.code}</DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <Select value={selected} onValueChange={setSelected}>
                <SelectTrigger className="w-full">
                  <SelectValue
                    placeholder={t("permissions.impliedPermission")}
                  />
                </SelectTrigger>
                <SelectContent>
                  {available.map((p) => (
                    <SelectItem key={p.id} value={p.id as string}>
                      {p.code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <Button
              onClick={() => selected && addMutation.mutate(selected)}
              disabled={!selected || addMutation.isPending}
            >
              {addMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : (
                <Plus />
              )}
              {t("permissions.addImplication")}
            </Button>
          </div>

          <div className="min-h-24 rounded-lg border p-3">
            {impliedQuery.isLoading ? (
              <div className="flex flex-wrap gap-2">
                {Array.from({ length: 3 }).map((_, i) => (
                  <Skeleton key={i} className="h-6 w-24" />
                ))}
              </div>
            ) : implied.length === 0 ? (
              <p className="py-4 text-center text-sm text-muted-foreground">
                {t("common.empty")}
              </p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {implied.map((p) => (
                  <Badge key={p.id} variant="secondary" className="gap-1 pe-1">
                    {p.code}
                    <button
                      type="button"
                      className="rounded-full p-0.5 hover:bg-foreground/10 disabled:opacity-50"
                      aria-label={t("common.remove")}
                      disabled={removeMutation.isPending}
                      onClick={() => p.id && removeMutation.mutate(p.id)}
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
