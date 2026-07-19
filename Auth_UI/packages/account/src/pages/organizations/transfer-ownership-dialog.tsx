import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Loader2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { collectAllPages, unwrap } from "@astoom/api/helpers"
import { getErrorMessage } from "@astoom/api/errors"
import type { Schemas } from "@astoom/api/types"
import { Button } from "@astoom/ui/button"
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { fullName } from "@astoom/ui/format"
import { useCountdown } from "@astoom/ui/hooks/use-countdown"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
  REGEXP_ONLY_DIGITS,
} from "@astoom/ui/input-otp"
import { Label } from "@astoom/ui/label"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@astoom/ui/select"

const CODE_LENGTH = 6

/**
 * Two-step, owner-only ownership transfer. Step one selects an eligible member
 * and shows the (irreversible) consequences; confirming emails a code to that
 * member. Step two takes the code the new owner received — entering it proves
 * both parties consent and completes the atomic swap.
 */
export function TransferOwnershipDialog({
  orgId,
  ownerId,
  open,
  onOpenChange,
  onTransferred,
}: {
  orgId: string
  /** Current owner's user id, excluded from the candidate list. */
  ownerId?: string
  open: boolean
  onOpenChange: (open: boolean) => void
  onTransferred: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [step, setStep] = React.useState<"select" | "verify">("select")
  const [memberId, setMemberId] = React.useState<string>()
  const [code, setCode] = React.useState("")
  const [expiresAt, setExpiresAt] = React.useState<Date | null>(null)
  const [targetEmail, setTargetEmail] = React.useState<string>()

  // Reset everything whenever the dialog is (re)opened.
  React.useEffect(() => {
    if (open) {
      setStep("select")
      setMemberId(undefined)
      setCode("")
      setExpiresAt(null)
      setTargetEmail(undefined)
    }
  }, [open])

  const membersQuery = useQuery({
    queryKey: ["org-members", orgId, "all"],
    enabled: open,
    queryFn: () =>
      collectAllPages<Schemas["OrganizationMemberDto"]>(
        async (pageNumber, size) => {
          const result = await unwrap(
            api.GET("/api/v1/Organizations/{id}/members", {
              params: {
                path: { id: orgId },
                query: { pageNumber, pageSize: size },
              },
            })
          )
          return {
            items: result.members ?? [],
            totalCount: Number(result.totalCount ?? 0),
          }
        }
      ),
  })

  // Only active members other than the current owner can receive ownership.
  const candidates = (membersQuery.data ?? []).filter(
    (m) => m.userId && m.userId !== ownerId && m.isActive !== false
  )

  const countdown = useCountdown(expiresAt)

  const initiateMutation = useMutation({
    mutationFn: async (newOwnerId: string) =>
      unwrap(
        api.POST("/api/v1/Organizations/{orgId}/ownership/initiate", {
          params: { path: { orgId } },
          body: { newOwnerId },
        })
      ),
    onSuccess: (response) => {
      setExpiresAt(response.expiresAt ? new Date(response.expiresAt) : null)
      setTargetEmail(response.targetEmailMasked ?? undefined)
      setCode("")
      setStep("verify")
      toast.success(t("organizations.transferInitiated"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const transferMutation = useMutation({
    mutationFn: async (submittedCode: string) => {
      const { error } = await api.POST(
        "/api/v1/Organizations/{orgId}/ownership",
        {
          params: { path: { orgId } },
          body: { newOwnerId: memberId ?? "", code: submittedCode },
        }
      )
      if (error) throw error
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["org-members", orgId] })
      void queryClient.invalidateQueries({ queryKey: ["organizations", orgId] })
      toast.success(t("organizations.transferSuccess"))
      onTransferred()
      onOpenChange(false)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const codeInputDisabled =
    !expiresAt || countdown.expired || transferMutation.isPending

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("organizations.transferOwnershipTitle")}</DialogTitle>
        </DialogHeader>

        {step === "select" ? (
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>{t("organizations.transferSelectMember")}</Label>
              <Select value={memberId} onValueChange={setMemberId}>
                <SelectTrigger className="w-full">
                  <SelectValue
                    placeholder={t(
                      "organizations.transferSelectMemberPlaceholder"
                    )}
                  />
                </SelectTrigger>
                <SelectContent>
                  {candidates.map((member) => (
                    <SelectItem
                      key={member.userId}
                      value={member.userId as string}
                    >
                      {member.fullName ||
                        fullName(
                          member.firstName,
                          member.lastName,
                          member.email ?? ""
                        )}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="rounded-md border p-3">
              <p className="text-sm font-medium">
                {t("organizations.transferConsequencesTitle")}
              </p>
              <ul className="mt-2 list-disc space-y-1 ps-5 text-sm text-muted-foreground">
                <li>{t("organizations.transferConsequence1")}</li>
                <li>{t("organizations.transferConsequence2")}</li>
                <li>{t("organizations.transferConsequence3")}</li>
              </ul>
            </div>

            <DialogFooter>
              <Button variant="outline" onClick={() => onOpenChange(false)}>
                {t("common.cancel")}
              </Button>
              <Button
                onClick={() => memberId && initiateMutation.mutate(memberId)}
                disabled={!memberId || initiateMutation.isPending}
              >
                {initiateMutation.isPending ? (
                  <Loader2 className="animate-spin" />
                ) : null}
                {t("organizations.transferSendCode")}
              </Button>
            </DialogFooter>
          </div>
        ) : (
          <div className="space-y-4">
            <p className="text-sm text-muted-foreground">
              {t("organizations.transferCodeSent", { email: targetEmail })}
            </p>

            <div className="flex flex-col items-center gap-3">
              <InputOTP
                dir="ltr"
                maxLength={CODE_LENGTH}
                pattern={REGEXP_ONLY_DIGITS}
                value={code}
                onChange={setCode}
                onComplete={(value: string) => transferMutation.mutate(value)}
                disabled={codeInputDisabled}
                autoFocus
                aria-label={t("organizations.transferCodeLabel")}
              >
                <InputOTPGroup>
                  {Array.from({ length: CODE_LENGTH }).map((_, index) => (
                    <InputOTPSlot key={index} index={index} />
                  ))}
                </InputOTPGroup>
              </InputOTP>

              {expiresAt && !countdown.expired ? (
                <p className="text-sm tabular-nums text-muted-foreground">
                  {t("organizations.transferCodeExpiresIn", {
                    time: countdown.label,
                  })}
                </p>
              ) : (
                <p className="text-sm text-destructive">
                  {t("organizations.transferCodeExpired")}
                </p>
              )}

              <Button
                variant="link"
                size="sm"
                disabled={!memberId || initiateMutation.isPending}
                onClick={() => memberId && initiateMutation.mutate(memberId)}
              >
                {initiateMutation.isPending ? (
                  <Loader2 className="animate-spin" />
                ) : null}
                {t("organizations.transferResendCode")}
              </Button>
            </div>

            <DialogFooter>
              <Button variant="outline" onClick={() => onOpenChange(false)}>
                {t("common.cancel")}
              </Button>
              <Button
                onClick={() => transferMutation.mutate(code)}
                disabled={code.length < CODE_LENGTH || codeInputDisabled}
              >
                {transferMutation.isPending ? (
                  <Loader2 className="animate-spin" />
                ) : null}
                {t("organizations.transferComplete")}
              </Button>
            </DialogFooter>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
