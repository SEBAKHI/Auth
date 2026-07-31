import { useMutation, useQueryClient } from "@tanstack/react-query"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useNavigate } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@authsystem/api/client"
import { getErrorMessage } from "@authsystem/api/errors"
import { unwrap } from "@authsystem/api/helpers"
import type { Schemas } from "@authsystem/api/types"
import { Button } from "@authsystem/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@authsystem/ui/dialog"
import { DatePicker, monthsFromNow } from "@authsystem/ui/common/date-picker"
import { PolicyVersionField } from "./policy-version-field"
import { Field, FieldGroup, FieldLabel } from "@authsystem/ui/field"
import { Textarea } from "@authsystem/ui/textarea"
import { Spinner } from "@authsystem/ui/spinner"

type PolicyVersionDto = Schemas["PrivacyPolicyVersionDto"]

const VERSION_RE = /^\d{4}\.\d{2}$/

/**
 * Clones a policy revision: records the new version, then copies every
 * language document from the source into it.
 *
 * This is what makes a new revision practical — drafting the next policy from
 * a blank document in seven languages is not something anyone would do. The
 * copy is a composition of the existing endpoints (create + per-language
 * save), so there is no bespoke server-side clone to keep in sync.
 */
export function ClonePolicyDialog({
  source,
  onOpenChange,
}: {
  /** Revision to copy from; null closes the dialog. */
  source: PolicyVersionDto | null
  onOpenChange: (open: boolean) => void
}) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [version, setVersion] = React.useState("")
  const [effectiveDate, setEffectiveDate] = React.useState("")
  const [changeNote, setChangeNote] = React.useState("")
  const [copied, setCopied] = React.useState(0)

  // Reset per open so a previous attempt never leaks into the next one.
  React.useEffect(() => {
    if (!source) return
    setVersion("")
    setEffectiveDate("")
    setChangeNote("")
    setCopied(0)
  }, [source])

  const languages = source?.languages ?? []

  const cloneMutation = useMutation({
    mutationFn: async () => {
      if (!source?.version) throw new Error("No source version")

      const created = await unwrap(
        api.POST("/api/v1/privacy-policy/versions", {
          body: {
            version: version.trim(),
            effectiveDateUtc: effectiveDate + "T00:00:00Z",
            changeNote: changeNote.trim() || null,
          },
        })
      )

      // Copy sequentially: a partial clone is recoverable (the languages that
      // landed are saved), and it keeps the progress counter honest.
      let done = 0
      const createdId = created?.id
      for (const language of languages) {
        const content = await unwrap(
          api.GET("/api/v1/privacy-policy/versions/content", {
            params: { query: { version: source.version, language } },
          })
        )
        if (!content?.contentJson) continue

        await unwrap(
          api.PUT("/api/v1/privacy-policy/versions/content", {
            body: {
              version: version.trim(),
              languageCode: language,
              contentJson: content.contentJson,
            },
          })
        )
        done += 1
        setCopied(done)
      }
      return { done, createdId }
    },
    onSuccess: ({ done, createdId }) => {
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      onOpenChange(false)
      toast.success(t("notifications.policyClonedToast", { count: done }))
      if (createdId) navigate(`/notifications/policy/${createdId}`)
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const valid = VERSION_RE.test(version.trim()) && effectiveDate.length > 0

  return (
    <Dialog
      open={source !== null}
      onOpenChange={(open) => {
        if (!open && !cloneMutation.isPending) onOpenChange(false)
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("notifications.policyCloneTitle")}</DialogTitle>
          <DialogDescription>
            {t("notifications.policyCloneDescription", {
              source: source?.version ?? "",
              count: languages.length,
            })}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel htmlFor="clone-version">
              {t("notifications.policyVersion")}
            </FieldLabel>
            <PolicyVersionField
              id="clone-version"
              value={version}
              disabled={cloneMutation.isPending}
              onChange={setVersion}
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="clone-effective">
              {t("notifications.policyEffective")}
            </FieldLabel>
            <DatePicker
              id="clone-effective"
              value={effectiveDate}
              disabled={cloneMutation.isPending}
              onChange={(value) => setEffectiveDate(value ?? "")}
              minDate={monthsFromNow(-5)}
              maxDate={monthsFromNow(5)}
            />
          </Field>
          <Field>
            <FieldLabel htmlFor="clone-note">
              {t("notifications.policyChangeNote")}
            </FieldLabel>
            {/* No `dir="auto"`: Chrome resolves `auto` from the value alone, so an
                empty control computes `ltr` and this note opened left-aligned with
                the caret on the wrong edge in every RTL locale. The note is the
                admin's own prose, so it follows the console's direction. */}
            <Textarea
              id="clone-note"
              rows={2}
              placeholder={t("notifications.policyChangeNoteHint")}
              value={changeNote}
              disabled={cloneMutation.isPending}
              onChange={(event) => setChangeNote(event.target.value)}
            />
          </Field>
          {cloneMutation.isPending ? (
            <p className="text-sm text-muted-foreground">
              {t("notifications.policyCloneProgress", {
                done: copied,
                total: languages.length,
              })}
            </p>
          ) : null}
        </FieldGroup>
        <DialogFooter>
          <Button
            disabled={!valid || cloneMutation.isPending}
            onClick={() => cloneMutation.mutate()}
          >
            {cloneMutation.isPending ? <Spinner /> : null}
            {t("notifications.policyClone")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
