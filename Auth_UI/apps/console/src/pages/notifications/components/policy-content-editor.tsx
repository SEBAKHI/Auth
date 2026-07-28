import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Loader2, Save } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { Alert, AlertDescription } from "@astoom/ui/alert"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@astoom/ui/dialog"
import { Skeleton } from "@astoom/ui/skeleton"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { Textarea } from "@astoom/ui/textarea"

/** Placeholders the renderer substitutes from the running configuration. */
const TOKENS = [
  "{{graceDays}}",
  "{{otpValidityMinutes}}",
  "{{loginAttemptRetentionDays}}",
  "{{outboxRetentionDays}}",
]

/**
 * Per-language editor for one policy revision's document. Content is stored in
 * the database, so wording changes ship without a deployment.
 *
 * Numeric disclosures are written as {{tokens}}, never as literals: the
 * accounts app substitutes the live values from AccountDeletionSettings at
 * render time, which is what stops the published policy from drifting out of
 * agreement with the running system when appsettings change.
 */
export function PolicyContentEditor({
  version,
  open,
  onOpenChange,
  canManage,
}: {
  version: string
  open: boolean
  onOpenChange: (open: boolean) => void
  canManage: boolean
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()

  const [language, setLanguage] = React.useState("en")
  const [draft, setDraft] = React.useState("")
  const [dirty, setDirty] = React.useState(false)

  const contentQuery = useQuery({
    queryKey: ["privacy-policy-content", version, language],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/privacy-policy/versions/content", {
          params: { query: { version, language } },
        })
      ),
    enabled: open,
  })

  // Adopt the fetched document whenever the language (or the fetch) changes,
  // unless the editor holds unsaved edits.
  React.useEffect(() => {
    if (!contentQuery.data || dirty) return
    const raw = contentQuery.data.contentJson ?? ""
    setDraft(raw ? formatJson(raw) : "")
  }, [contentQuery.data, dirty])

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/privacy-policy/versions/content", {
          body: { version, languageCode: language, contentJson: draft },
        })
      ),
    onSuccess: () => {
      setDirty(false)
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-content"] })
      void queryClient.invalidateQueries({ queryKey: ["privacy-policy-versions"] })
      toast.success(t("notifications.policyContentSaved"))
    },
    onError: (error) => toast.error(getErrorMessage(error)),
  })

  const jsonError = React.useMemo(() => {
    if (!draft.trim()) return null
    try {
      JSON.parse(draft)
      return null
    } catch (error) {
      return (error as Error).message
    }
  }, [draft])

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        onOpenChange(next)
        if (!next) {
          setDirty(false)
          setLanguage("en")
        }
      }}
    >
      <DialogContent className="max-w-4xl">
        <DialogHeader>
          <DialogTitle>
            {t("notifications.policyContentTitle", { version })}
          </DialogTitle>
          <DialogDescription>
            {t("notifications.policyContentDescription")}
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-4">
          <Tabs
            value={language}
            onValueChange={(next: string) => {
              setDirty(false)
              setLanguage(next)
            }}
          >
            <TabsList>
              {SUPPORTED_LANGUAGES.map((lang) => (
                <TabsTrigger key={lang.code} value={lang.code}>
                  {lang.code.toUpperCase()}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>

          <div className="flex flex-wrap items-center gap-1.5">
            <span className="text-xs text-muted-foreground">
              {t("notifications.policyTokens")}
            </span>
            {TOKENS.map((token) => (
              <Badge key={token} variant="secondary" className="font-mono">
                {token}
              </Badge>
            ))}
          </div>

          {contentQuery.isLoading ? (
            <Skeleton className="h-96 w-full" />
          ) : (
            <Textarea
              className="h-96 font-mono text-xs"
              dir="ltr"
              spellCheck={false}
              value={draft}
              readOnly={!canManage}
              aria-label={t("notifications.policyContentTitle", { version })}
              onChange={(event) => {
                setDraft(event.target.value)
                setDirty(true)
              }}
            />
          )}

          {jsonError ? (
            <Alert variant="destructive">
              <AlertDescription className="font-mono text-xs">
                {jsonError}
              </AlertDescription>
            </Alert>
          ) : null}
        </div>

        <DialogFooter>
          <Button
            disabled={
              !canManage ||
              !dirty ||
              jsonError !== null ||
              saveMutation.isPending
            }
            onClick={() => saveMutation.mutate()}
          >
            {saveMutation.isPending ? (
              <Loader2 className="animate-spin" />
            ) : (
              <Save data-icon="inline-start" />
            )}
            {t("common.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

/** Pretty-prints stored JSON so the document is editable by a human. */
function formatJson(raw: string): string {
  try {
    return JSON.stringify(JSON.parse(raw), null, 2)
  } catch {
    return raw
  }
}
