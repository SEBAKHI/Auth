import { useMutation, useQueryClient } from "@tanstack/react-query"
import { TriangleAlert } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { useAuth } from "@authsystem/auth/auth-context"
import { Alert, AlertDescription } from "@authsystem/ui/alert"
import { Button } from "@authsystem/ui/button"
import { ConfirmDialog } from "@authsystem/ui/common/confirm-dialog"
import {
  OTP_CODE_LENGTH,
  OtpInput,
  RESEND_COOLDOWN_MS,
} from "@authsystem/ui/common/otp-input"
import { ResendCodeButton } from "@authsystem/ui/common/resend-code-button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { Field, FieldLabel } from "@authsystem/ui/field"
import { useCountdown } from "@authsystem/ui/hooks/use-countdown"
import { Input } from "@authsystem/ui/input"
import { Spinner } from "@authsystem/ui/spinner"

/**
 * The six destructive secret operations, by their server-side enum names.
 *
 * The generated schema types `SecretOperation` as `number` because the OpenAPI
 * document describes the CLR enum, but the API is configured with
 * `JsonStringEnumConverter`, so the value on the wire is the member name in both
 * directions. This union is the honest contract; the two casts below are where
 * that mismatch is absorbed, exactly as `secretStatusMeta` absorbs it for reads.
 */
export type SecretOperationName =
  | "GenerateRsaKey"
  | "GenerateHmacKey"
  | "GenerateGatewayToken"
  | "ImportRsaKey"
  | "ImportHmacKey"
  | "ImportGatewayToken"

/**
 * A change of the HMAC keyring (S11), by the server's enum names, sent the same
 * way as `SecretOperationName`. Each is confirmed with the HMAC step-up, bound
 * to the action and to the key it applies to.
 */
export type KeyringActionName =
  | "Stage"
  | "StageImport"
  | "Promote"
  | "PromoteEmergency"
  | "DiscardNext"
  | "Reinstate"
  | "Retire"

/** An operation the administrator has asked for but not yet confirmed. */
export interface PendingSecretOperation {
  operation: SecretOperationName
  /** Key material, for the import operations only. */
  value?: string
  /** For a change of the HMAC keyring: which one. */
  keyringAction?: KeyringActionName
  /** The key a keyring change applies to, when it names one (shown, and sent for reinstate and retire). */
  keyId?: number
}

/**
 * What the reveal dialog should show once the operation has run. Only the
 * regenerate/import paths that mint a value the administrator has to copy carry
 * one; an HMAC rotation produces nothing they can see.
 */
export interface SecretOperationResult {
  value?: string | null
  multiline: boolean
}

type FlowStep = "confirm" | "challenge" | "impact"

/** The keyring endpoint of each keyring change; every one answers with the new keyring. */
async function executeKeyringAction(
  action: KeyringActionName,
  { value, keyId }: PendingSecretOperation,
  challengeId: string
): Promise<SecretOperationResult> {
  switch (action) {
    case "Stage":
    case "StageImport":
      await unwrap(
        api.POST("/api/v1/admin/Secrets/keyring/hmac/stage", {
          body: { challengeId, value: action === "StageImport" ? value : null },
        })
      )
      break
    case "Promote":
    case "PromoteEmergency":
      await unwrap(
        api.POST("/api/v1/admin/Secrets/keyring/hmac/promote", {
          body: { challengeId, emergency: action === "PromoteEmergency" },
        })
      )
      break
    case "DiscardNext":
      await unwrap(
        api.POST("/api/v1/admin/Secrets/keyring/hmac/discard-next", {
          body: { challengeId },
        })
      )
      break
    case "Reinstate":
      await unwrap(
        api.POST("/api/v1/admin/Secrets/keyring/hmac/reinstate/{id}", {
          params: { path: { id: keyId ?? 0 } },
          body: { challengeId },
        })
      )
      break
    case "Retire":
      await unwrap(
        api.POST("/api/v1/admin/Secrets/keyring/hmac/retire/{id}", {
          params: { path: { id: keyId ?? 0 } },
          body: { challengeId },
        })
      )
      break
  }
  return { multiline: false }
}

async function executeOperation(
  pending: PendingSecretOperation,
  challengeId: string
): Promise<SecretOperationResult> {
  const { operation, value, keyringAction } = pending
  // A keyring change never goes to generate/hmac or import/hmac: those are the
  // emergency aliases kept for consoles cached before the keyring (S11).
  if (keyringAction) return executeKeyringAction(keyringAction, pending, challengeId)

  switch (operation) {
    case "GenerateRsaKey": {
      const data = await unwrap(
        api.POST("/api/v1/admin/Secrets/generate/rsa", { body: { challengeId } })
      )
      return { value: data?.publicKeyPem, multiline: true }
    }
    case "GenerateHmacKey": {
      await unwrap(
        api.POST("/api/v1/admin/Secrets/generate/hmac", { body: { challengeId } })
      )
      return { multiline: false }
    }
    case "GenerateGatewayToken": {
      const data = await unwrap(
        api.POST("/api/v1/admin/Secrets/generate/gateway-token", {
          body: { challengeId },
        })
      )
      return { value: data?.token, multiline: false }
    }
    case "ImportRsaKey": {
      const data = await unwrap(
        api.POST("/api/v1/admin/Secrets/import/rsa", {
          body: { value, challengeId },
        })
      )
      return { value: data?.publicKeyPem, multiline: true }
    }
    case "ImportHmacKey": {
      await unwrap(
        api.POST("/api/v1/admin/Secrets/import/hmac", {
          body: { value, challengeId },
        })
      )
      return { multiline: false }
    }
    default: {
      await unwrap(
        api.POST("/api/v1/admin/Secrets/import/gateway-token", {
          body: { value, challengeId },
        })
      )
      return { multiline: false }
    }
  }
}

function needsKeyId(pending: PendingSecretOperation): boolean {
  return (
    pending.keyringAction === "Reinstate" || pending.keyringAction === "Retire"
  )
}

/**
 * Three gates in front of every key rotation and key import: a warning, a code
 * emailed to the administrator's own mailbox, and — only once that code is
 * accepted — the live count of who this breaks, behind a type-to-confirm.
 *
 * Mounted only while an operation is pending, so every piece of flow state
 * (code, challenge, impact, typed confirmation) is discarded when it closes and
 * cannot leak into the next attempt.
 */
export function SecretOperationFlow({
  pending,
  onClose,
  onExecuted,
}: {
  pending: PendingSecretOperation
  onClose: () => void
  onExecuted: (result: SecretOperationResult) => void
}) {
  const { t } = useTranslation()
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const [step, setStep] = React.useState<FlowStep>("confirm")
  const [challenge, setChallenge] =
    React.useState<Schemas["SecretOperationChallengeDto"]>()
  const [impact, setImpact] = React.useState<Schemas["SecretRotationImpactDto"]>()
  const [code, setCode] = React.useState("")
  const [codeError, setCodeError] = React.useState<string | null>(null)
  const [cooldownUntil, setCooldownUntil] = React.useState<Date | null>(null)
  const [confirmEmail, setConfirmEmail] = React.useState("")

  const adminEmail = user?.email ?? ""

  // Both windows are server-owned; the client only reflects them so the
  // administrator is never staring at a button that has already stopped working.
  const codeCountdown = useCountdown(
    challenge ? new Date(challenge.expiresAt) : null
  )
  const approvalCountdown = useCountdown(
    impact ? new Date(impact.approvalExpiresAt) : null
  )

  const operationLabels: Record<SecretOperationName, string> = {
    GenerateRsaKey: t("secrets.operation.generateRsaKey"),
    GenerateHmacKey: t("secrets.operation.generateHmacKey"),
    GenerateGatewayToken: t("secrets.operation.generateGatewayToken"),
    ImportRsaKey: t("secrets.operation.importRsaKey"),
    ImportHmacKey: t("secrets.operation.importHmacKey"),
    ImportGatewayToken: t("secrets.operation.importGatewayToken"),
  }

  // Literal keys so the i18n parity test can see them; unknown codes fall back
  // to the raw code rather than rendering an empty row.
  const impactLabels: Record<string, string> = {
    usersWithLiveAccessTokens: t("secrets.impact.usersWithLiveAccessTokens"),
    usersWithActiveSessions: t("secrets.impact.usersWithActiveSessions"),
    usersSignedOut: t("secrets.impact.usersSignedOut"),
    usersWithSsoSessions: t("secrets.impact.usersWithSsoSessions"),
    pendingPasswordResets: t("secrets.impact.pendingPasswordResets"),
    pendingTwoFactorChallenges: t("secrets.impact.pendingTwoFactorChallenges"),
    activeWebhookKeys: t("secrets.impact.activeWebhookKeys"),
    webhookKeysInGrace: t("secrets.impact.webhookKeysInGrace"),
    pendingInvitations: t("secrets.impact.pendingInvitations"),
    pendingEmailCodes: t("secrets.impact.pendingEmailCodes"),
    pendingRegistrations: t("secrets.impact.pendingRegistrations"),
    pendingAuthorizations: t("secrets.impact.pendingAuthorizations"),
    usersBeyondOverlap: t("secrets.impact.usersBeyondOverlap"),
    ssoSessionsBeyondOverlap: t("secrets.impact.ssoSessionsBeyondOverlap"),
  }

  // Literal keys, as above. A keyring change names its action and its key.
  const keyringLabels: Record<KeyringActionName, string> = {
    Stage: t("secrets.keyring.action.Stage"),
    StageImport: t("secrets.keyring.action.StageImport"),
    Promote: t("secrets.keyring.action.Promote", { id: pending.keyId }),
    PromoteEmergency: t("secrets.keyring.action.PromoteEmergency"),
    DiscardNext: t("secrets.keyring.action.DiscardNext", { id: pending.keyId }),
    Reinstate: t("secrets.keyring.action.Reinstate", { id: pending.keyId }),
    Retire: t("secrets.keyring.action.Retire", { id: pending.keyId }),
  }
  const keyringWarnings: Record<KeyringActionName, string> = {
    Stage: t("secrets.keyring.warning.Stage"),
    StageImport: t("secrets.keyring.warning.StageImport"),
    Promote: t("secrets.keyring.warning.Promote"),
    PromoteEmergency: t("secrets.keyring.warning.PromoteEmergency"),
    DiscardNext: t("secrets.keyring.warning.DiscardNext"),
    Reinstate: t("secrets.keyring.warning.Reinstate"),
    Retire: t("secrets.keyring.warning.Retire"),
  }

  const operationLabel = pending.keyringAction
    ? keyringLabels[pending.keyringAction]
    : operationLabels[pending.operation]
  const warning = pending.keyringAction
    ? keyringWarnings[pending.keyringAction]
    : t("secrets.rotateWarning")

  const requestMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.POST("/api/v1/admin/Secrets/challenges", {
          body: {
            // See the SecretOperationName note above: the wire value is the
            // enum member name, which the generated numeric type disallows.
            operation:
              pending.operation as unknown as Schemas["SecretOperation"],
            value: pending.value,
            // The server binds the approval to the action and to the key the
            // secrets file names for it; only reinstate and retire send their key.
            keyringAction: pending.keyringAction as unknown as
              | Schemas["KeyringAction"]
              | undefined,
            keyId: needsKeyId(pending) ? pending.keyId : undefined,
          },
        })
      ),
    onSuccess: (data) => {
      setChallenge(data)
      setCode("")
      setCodeError(null)
      setCooldownUntil(new Date(Date.now() + RESEND_COOLDOWN_MS))
      setStep("challenge")
      toast.success(t("secrets.challengeSent"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const verifyMutation = useMutation({
    mutationFn: (submitted: string) =>
      unwrap(
        api.POST("/api/v1/admin/Secrets/challenges/{challengeId}/verify", {
          params: { path: { challengeId: challenge?.challengeId ?? "" } },
          // A keyring change is costed for its own action; the server checks the
          // claim against the confirmation.
          body: {
            code: submitted,
            keyringAction: pending.keyringAction as unknown as
              | Schemas["KeyringAction"]
              | undefined,
            keyId: needsKeyId(pending) ? pending.keyId : undefined,
            value: pending.keyringAction === "StageImport" ? pending.value : undefined,
          },
        })
      ),
    onSuccess: (data) => {
      setImpact(data)
      setConfirmEmail("")
      setStep("impact")
    },
    onError: (error) => {
      // A rejected code must not strand the administrator: clear the field and
      // let them retype or request a fresh one.
      setCode("")
      setCodeError(getErrorMessage(error))
    },
  })

  const executeMutation = useMutation({
    mutationFn: () => executeOperation(pending, challenge?.challengeId ?? ""),
    onSuccess: (result) => {
      toast.success(
        pending.keyringAction
          ? t("secrets.keyring.done")
          : pending.value
            ? t("secrets.imported")
            : t("secrets.generated")
      )
      onExecuted(result)
      onClose()
    },
    onError: (error) => {
      // Whether or not the refusal spent the approval, the flow starts again
      // with a new code: there is nothing to retry from here.
      toast.error(getErrorMessage(error))
      onClose()
    },
    // Every secrets query, the status and the keyring, is read again however the
    // change ended: one that failed after the file was saved, or another
    // administrator's, must not leave the panel showing a ring that moved.
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ["secrets"] })
    },
  })

  const canVerify =
    code.length === OTP_CODE_LENGTH &&
    !codeCountdown.expired &&
    !verifyMutation.isPending

  // The generator widens int32 to `number | string` for every count in the
  // schema; coerce once here rather than at each read.
  const affectedUsers = Number(impact?.affectedUsers ?? 0)

  return (
    <>
      <ConfirmDialog
        open={step === "confirm"}
        onOpenChange={(open) => !open && onClose()}
        title={t("secrets.confirmTitle")}
        description={t("secrets.confirmBody")}
        confirmLabel={t("secrets.challengeContinue")}
        destructive
        loading={requestMutation.isPending}
        onConfirm={() => requestMutation.mutate()}
      >
        <Alert variant="destructive">
          <TriangleAlert />
          {/* Both strings live in the description: AlertTitle is a one-line
              slot (line-clamp-1), and these translate to full sentences and
              long operation labels in fr/tr/fa. */}
          <AlertDescription>
            <p>{operationLabel}</p>
            <p>{warning}</p>
          </AlertDescription>
        </Alert>
      </ConfirmDialog>

      <Dialog
        open={step === "challenge"}
        onOpenChange={(open) => !open && onClose()}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("secrets.challengeTitle")}</DialogTitle>
            <DialogDescription>
              {t("secrets.challengeBody", {
                email: challenge?.maskedEmail ?? "",
              })}
            </DialogDescription>
          </DialogHeader>

          <div className="flex flex-col items-center gap-3">
            <OtpInput
              value={code}
              onChange={(value) => {
                setCode(value)
                setCodeError(null)
              }}
              onComplete={(submitted) => {
                if (!codeCountdown.expired) verifyMutation.mutate(submitted)
              }}
              label={t("secrets.challengeCodeLabel")}
              disabled={codeCountdown.expired || verifyMutation.isPending}
              autoFocus
            />

            {codeCountdown.expired ? (
              <p className="text-sm text-destructive">{t("auth.codeExpired")}</p>
            ) : (
              <p className="text-sm tabular-nums text-muted-foreground">
                {t("auth.codeExpiresIn", { time: codeCountdown.label })}
              </p>
            )}

            {/* Inline, not a toast: a toast behind a modal is easy to miss. */}
            {codeError ? (
              <p className="text-center text-sm text-destructive">{codeError}</p>
            ) : null}

            <ResendCodeButton
              availableAt={cooldownUntil}
              pending={requestMutation.isPending}
              onResend={() => requestMutation.mutate()}
            />
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={onClose}>
              {t("common.cancel")}
            </Button>
            <Button
              variant="destructive"
              disabled={!canVerify}
              onClick={() => verifyMutation.mutate(code)}
            >
              {verifyMutation.isPending ? <Spinner /> : null}
              {t("secrets.challengeContinue")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={step === "impact"}
        onOpenChange={(open) => !open && onClose()}
        title={t("secrets.impactTitle")}
        description={operationLabel}
        confirmLabel={t("secrets.impactExecute")}
        destructive
        loading={executeMutation.isPending}
        confirmDisabled={
          approvalCountdown.expired ||
          confirmEmail.trim().toLowerCase() !== adminEmail.trim().toLowerCase()
        }
        onConfirm={() => executeMutation.mutate()}
      >
        <Alert variant="destructive">
          <TriangleAlert />
          {/* The dialog above already carries the title and the operation
              label; these two are sentences, so they belong in the
              description rather than the one-line AlertTitle slot. */}
          <AlertDescription>
            <p>
              {affectedUsers > 0
                ? t("secrets.impactAffected", { count: affectedUsers })
                : t("secrets.impactNobody")}
            </p>
            <p>
              {pending.keyringAction
                ? t("secrets.keyring.impactNote")
                : t("secrets.impactIrreversible")}
            </p>
          </AlertDescription>
        </Alert>

        {impact && impact.details.length > 0 ? (
          // Kept as a block list so the markers survive — a flex/grid `ul`
          // drops `list-item` display and the bullets with it.
          <ul className="list-disc ps-5 text-sm text-muted-foreground [&>li+li]:mt-1">
            {impact.details.map((detail) => (
              <li key={detail.code}>
                {impactLabels[detail.code] ?? detail.code}:{" "}
                <span className="font-medium tabular-nums text-foreground">
                  {Number(detail.count)}
                </span>
              </li>
            ))}
          </ul>
        ) : null}

        {impact?.requiresApiRestart ? (
          <p className="text-sm text-muted-foreground">
            {t("secrets.impactRestart")}
          </p>
        ) : null}

        {impact?.requiresGatewayReconfiguration ? (
          <p className="text-sm text-muted-foreground">
            {t("secrets.impactGateway")}
          </p>
        ) : null}

        <p className="text-sm tabular-nums text-muted-foreground">
          {approvalCountdown.expired
            ? t("secrets.impactExpired")
            : t("secrets.impactExpiresIn", { time: approvalCountdown.label })}
        </p>

        <Field>
          <FieldLabel htmlFor="secret-operation-confirm">
            {t("secrets.impactConfirmHint", { email: adminEmail })}
          </FieldLabel>
          <Input
            id="secret-operation-confirm"
            dir="ltr"
            type="email"
            autoComplete="off"
            spellCheck={false}
            value={confirmEmail}
            onChange={(event) => setConfirmEmail(event.target.value)}
          />
        </Field>
      </ConfirmDialog>
    </>
  )
}
