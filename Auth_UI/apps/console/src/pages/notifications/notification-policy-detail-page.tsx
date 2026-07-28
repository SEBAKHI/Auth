import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import { Loader2, Plus, Save, Trash2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"
import { useParams } from "react-router-dom"
import { toast } from "sonner"

import { api } from "@astoom/api/client"
import { getErrorMessage } from "@astoom/api/errors"
import { unwrap } from "@astoom/api/helpers"
import { useAuth } from "@astoom/auth/auth-context"
import { directionForLanguage, SUPPORTED_LANGUAGES } from "@astoom/i18n"
import { Alert, AlertDescription } from "@astoom/ui/alert"
import { Badge } from "@astoom/ui/badge"
import { Button } from "@astoom/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@astoom/ui/card"
import { ConfirmDialog } from "@astoom/ui/common/confirm-dialog"
import { PageHeader } from "@astoom/ui/common/page-header"
import {
  PolicyDocument,
  POLICY_TOKENS,
  type PolicyDisclosure,
  type PrivacyPolicyContent,
} from "@astoom/ui/common/policy-document"
import { Field, FieldGroup, FieldLabel } from "@astoom/ui/field"
import { Input } from "@astoom/ui/input"
import { Skeleton } from "@astoom/ui/skeleton"
import { useUnsavedChangesPrompt } from "@astoom/ui/hooks/use-unsaved-changes"
import { Tabs, TabsList, TabsTrigger } from "@astoom/ui/tabs"
import { Textarea } from "@astoom/ui/textarea"
import { PERMISSIONS } from "@/lib/constants"
import {
  SectionListEditor,
  StringListEditor,
} from "./components/policy-field-editors"

/** Shape used when starting a language that has no document yet. */
const EMPTY_DOCUMENT: PrivacyPolicyContent = {
  title: "",
  effectiveDate: "",
  versionLabel: "Version",
  intro: [""],
  sections: [],
  retention: {
    heading: "",
    intro: "",
    columns: ["", "", ""],
    rows: [],
  },
  deletion: {
    heading: "",
    paragraphs: [""],
    bullets: [""],
    button: "",
    signedInHint: "",
  },
  rights: [],
  closing: [],
  contactDpoLabel: "",
  contactVerbisLabel: "",
  contactKepLabel: "",
  unfilledWarning: "",
}

/**
 * Structured editor for one policy revision, with a live preview rendered by
 * the SAME component the public page uses — so what an editor sees is what a
 * user will get.
 *
 * Numbers are never typed in: the {{token}} palette is substituted from the
 * running AccountDeletionSettings at render time, which is what keeps the
 * published text from drifting when configuration changes.
 */
export function NotificationPolicyDetailPage() {
  const { t } = useTranslation()
  const { hasPermission } = useAuth()
  const queryClient = useQueryClient()
  const { version = "" } = useParams()

  const canManage = hasPermission(PERMISSIONS.privacyPolicy.manage)

  const [language, setLanguage] = React.useState("en")
  const [doc, setDoc] = React.useState<PrivacyPolicyContent | null>(null)
  const [dirty, setDirty] = React.useState(false)
  const [parseError, setParseError] = React.useState<string | null>(null)
  const [pendingLanguage, setPendingLanguage] = React.useState<string | null>(null)

  const contentQuery = useQuery({
    queryKey: ["privacy-policy-content", version, language],
    queryFn: () =>
      unwrap(
        api.GET("/api/v1/privacy-policy/versions/content", {
          params: { query: { version, language } },
        })
      ),
  })

  // The live disclosure drives the preview's numbers, exactly as on the
  // public page — so the preview shows real values, never raw tokens.
  const publishedQuery = useQuery({
    queryKey: ["privacy-policy-disclosure"],
    queryFn: () =>
      unwrap(api.GET("/api/v1/privacy-policy/published", { params: { query: {} } })),
    staleTime: 5 * 60 * 1000,
  })

  React.useEffect(() => {
    if (!contentQuery.data || dirty) return
    const raw = contentQuery.data.contentJson
    if (!raw) {
      setDoc(structuredClone(EMPTY_DOCUMENT))
      setParseError(null)
      return
    }
    try {
      setDoc(JSON.parse(raw) as PrivacyPolicyContent)
      setParseError(null)
    } catch (error) {
      setDoc(null)
      setParseError((error as Error).message)
    }
  }, [contentQuery.data, dirty])

  const saveMutation = useMutation({
    mutationFn: () =>
      unwrap(
        api.PUT("/api/v1/privacy-policy/versions/content", {
          body: {
            version,
            languageCode: language,
            contentJson: JSON.stringify(doc),
          },
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

  // Leaving with unsaved legal text must never be silent.
  const unsavedPrompt = useUnsavedChangesPrompt(dirty)

  const patch = React.useCallback((next: Partial<PrivacyPolicyContent>) => {
    setDoc((current) => (current ? { ...current, ...next } : current))
    setDirty(true)
  }, [])

  const disclosure: PolicyDisclosure = {
    graceDays: Number(publishedQuery.data?.disclosure?.graceDays ?? 30),
    otpValidityMinutes: Number(publishedQuery.data?.disclosure?.otpValidityMinutes ?? 15),
    loginAttemptRetentionDays: Number(
      publishedQuery.data?.disclosure?.loginAttemptRetentionDays ?? 365
    ),
    outboxRetentionDays: Number(
      publishedQuery.data?.disclosure?.outboxRetentionDays ?? 180
    ),
    policyVersion: publishedQuery.data?.disclosure?.policyVersion ?? version,
  }

  const dir = directionForLanguage(language)

  return (
    <div className="space-y-6">
      {unsavedPrompt}
      <PageHeader
        title={t("notifications.policyContentTitle", { version })}
        description={t("notifications.policyContentDescription")}
        actions={
          canManage ? (
            <Button
              disabled={!dirty || !doc || saveMutation.isPending}
              onClick={() => saveMutation.mutate()}
            >
              {saveMutation.isPending ? (
                <Loader2 className="animate-spin" />
              ) : (
                <Save data-icon="inline-start" />
              )}
              {t("common.save")}
            </Button>
          ) : undefined
        }
      />

      <Tabs
        value={language}
        onValueChange={(next: string) => {
          // Switching language refetches the document, so unsaved edits in
          // the current one would be lost without asking.
          if (dirty) {
            setPendingLanguage(next)
            return
          }
          setLanguage(next)
        }}
      >
        <TabsList className="h-auto! flex-wrap">
          {SUPPORTED_LANGUAGES.map((lang) => (
            <TabsTrigger key={lang.code} value={lang.code}>
              {lang.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      <div className="flex flex-wrap items-center gap-1.5">
        <span className="text-xs text-muted-foreground">
          {t("notifications.policyTokens")}
        </span>
        {POLICY_TOKENS.map((token) => (
          <Badge key={token} variant="secondary" className="font-mono">
            {token}
          </Badge>
        ))}
      </div>

      <ConfirmDialog
        open={pendingLanguage !== null}
        onOpenChange={(open) => {
          if (!open) setPendingLanguage(null)
        }}
        title={t("common.discardTitle")}
        description={t("common.discardBody")}
        confirmLabel={t("common.discard")}
        destructive
        onConfirm={() => {
          if (pendingLanguage) {
            setDirty(false)
            setLanguage(pendingLanguage)
          }
          setPendingLanguage(null)
        }}
      />

      {parseError ? (
        <Alert variant="destructive">
          <AlertDescription className="font-mono text-xs">
            {parseError}
          </AlertDescription>
        </Alert>
      ) : null}

      {contentQuery.isLoading || !doc ? (
        <Skeleton className="h-96 w-full" />
      ) : (
        <div className="grid gap-6 xl:grid-cols-2">
          {/* Editor */}
          <div className="flex flex-col gap-6">
            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyHeader")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="doc-title">
                      {t("notifications.policyDocTitle")}
                    </FieldLabel>
                    <Input
                      id="doc-title"
                      dir="auto"
                      value={doc.title}
                      disabled={!canManage}
                      onChange={(e) => patch({ title: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-effective">
                      {t("notifications.policyEffectiveLabel")}
                    </FieldLabel>
                    <Input
                      id="doc-effective"
                      dir="auto"
                      value={doc.effectiveDate}
                      disabled={!canManage}
                      onChange={(e) => patch({ effectiveDate: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-versionlabel">
                      {t("notifications.policyVersionLabel")}
                    </FieldLabel>
                    <Input
                      id="doc-versionlabel"
                      dir="auto"
                      value={doc.versionLabel}
                      disabled={!canManage}
                      onChange={(e) => patch({ versionLabel: e.target.value })}
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="doc-warning">
                      {t("notifications.policyDraftWarning")}
                    </FieldLabel>
                    <Textarea
                      id="doc-warning"
                      dir="auto"
                      rows={2}
                      value={doc.unfilledWarning}
                      disabled={!canManage}
                      onChange={(e) => patch({ unfilledWarning: e.target.value })}
                    />
                  </Field>
                  <StringListEditor
                    label={t("notifications.policyIntro")}
                    values={doc.intro}
                    disabled={!canManage}
                    removeLabel={t("notifications.policyRemoveParagraph")}
                    onChange={(intro) => patch({ intro })}
                  />
                </FieldGroup>
              </CardContent>
            </Card>

            <SectionListEditor
              label={t("notifications.policySections")}
              sections={doc.sections}
              disabled={!canManage}
              onChange={(sections) => patch({ sections })}
            />

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyRetention")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="ret-heading">
                      {t("notifications.policyHeading")}
                    </FieldLabel>
                    <Input
                      id="ret-heading"
                      dir="auto"
                      value={doc.retention.heading}
                      disabled={!canManage}
                      onChange={(e) =>
                        patch({
                          retention: { ...doc.retention, heading: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="ret-intro">
                      {t("notifications.policyParagraphs")}
                    </FieldLabel>
                    <Textarea
                      id="ret-intro"
                      dir="auto"
                      rows={3}
                      value={doc.retention.intro}
                      disabled={!canManage}
                      onChange={(e) =>
                        patch({
                          retention: { ...doc.retention, intro: e.target.value },
                        })
                      }
                    />
                  </Field>

                  <Field>
                    <FieldLabel>{t("notifications.policyColumns")}</FieldLabel>
                    <div className="grid gap-2 sm:grid-cols-3">
                      {doc.retention.columns.map((column, index) => (
                        <Input
                          key={index}
                          dir="auto"
                          value={column}
                          disabled={!canManage}
                          onChange={(e) => {
                            const columns = [...doc.retention.columns] as [
                              string,
                              string,
                              string,
                            ]
                            columns[index] = e.target.value
                            patch({ retention: { ...doc.retention, columns } })
                          }}
                        />
                      ))}
                    </div>
                  </Field>

                  <Field>
                    <FieldLabel>{t("notifications.policyRows")}</FieldLabel>
                    <div className="flex flex-col gap-3">
                      {doc.retention.rows.map((row, index) => (
                        <div
                          key={index}
                          className="grid items-start gap-2 rounded-md border p-3 sm:grid-cols-[1fr_1fr_1.5fr_auto]"
                        >
                          {(["category", "retention", "detail"] as const).map(
                            (key) => (
                              <Textarea
                                key={key}
                                dir="auto"
                                rows={2}
                                value={row[key]}
                                disabled={!canManage}
                                onChange={(e) => {
                                  const rows = [...doc.retention.rows]
                                  rows[index] = { ...rows[index], [key]: e.target.value }
                                  patch({ retention: { ...doc.retention, rows } })
                                }}
                              />
                            )
                          )}
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            disabled={!canManage}
                            aria-label={t("notifications.policyRemoveRow")}
                            title={t("notifications.policyRemoveRow")}
                            onClick={() =>
                              patch({
                                retention: {
                                  ...doc.retention,
                                  rows: doc.retention.rows.filter(
                                    (_, i) => i !== index
                                  ),
                                },
                              })
                            }
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      ))}
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="w-fit"
                        disabled={!canManage}
                        onClick={() =>
                          patch({
                            retention: {
                              ...doc.retention,
                              rows: [
                                ...doc.retention.rows,
                                { category: "", retention: "", detail: "" },
                              ],
                            },
                          })
                        }
                      >
                        <Plus data-icon="inline-start" />
                        {t("notifications.policyAddRow")}
                      </Button>
                    </div>
                  </Field>
                </FieldGroup>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyDeletion")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  <Field>
                    <FieldLabel htmlFor="del-heading">
                      {t("notifications.policyHeading")}
                    </FieldLabel>
                    <Input
                      id="del-heading"
                      dir="auto"
                      value={doc.deletion.heading}
                      disabled={!canManage}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, heading: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <StringListEditor
                    label={t("notifications.policyParagraphs")}
                    values={doc.deletion.paragraphs}
                    disabled={!canManage}
                    removeLabel={t("notifications.policyRemoveParagraph")}
                    onChange={(paragraphs) =>
                      patch({ deletion: { ...doc.deletion, paragraphs } })
                    }
                  />
                  <StringListEditor
                    label={t("notifications.policyBullets")}
                    values={doc.deletion.bullets}
                    disabled={!canManage}
                    rows={2}
                    removeLabel={t("notifications.policyRemoveBullet")}
                    onChange={(bullets) =>
                      patch({ deletion: { ...doc.deletion, bullets } })
                    }
                  />
                  <Field>
                    <FieldLabel htmlFor="del-button">
                      {t("notifications.policyButtonLabel")}
                    </FieldLabel>
                    <Input
                      id="del-button"
                      dir="auto"
                      value={doc.deletion.button}
                      disabled={!canManage}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, button: e.target.value },
                        })
                      }
                    />
                  </Field>
                  <Field>
                    <FieldLabel htmlFor="del-hint">
                      {t("notifications.policySignedInHint")}
                    </FieldLabel>
                    <Input
                      id="del-hint"
                      dir="auto"
                      value={doc.deletion.signedInHint}
                      disabled={!canManage}
                      onChange={(e) =>
                        patch({
                          deletion: { ...doc.deletion, signedInHint: e.target.value },
                        })
                      }
                    />
                  </Field>
                </FieldGroup>
              </CardContent>
            </Card>

            <SectionListEditor
              label={t("notifications.policyRights")}
              sections={doc.rights}
              disabled={!canManage}
              onChange={(rights) => patch({ rights })}
            />

            <SectionListEditor
              label={t("notifications.policyClosing")}
              sections={doc.closing}
              disabled={!canManage}
              onChange={(closing) => patch({ closing })}
            />

            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyContactLabels")}</CardTitle>
              </CardHeader>
              <CardContent>
                <FieldGroup>
                  {(
                    [
                      ["contactDpoLabel", t("notifications.policyDpoLabel")],
                      ["contactVerbisLabel", t("notifications.policyVerbisLabel")],
                      ["contactKepLabel", t("notifications.policyKepLabel")],
                    ] as const
                  ).map(([key, label]) => (
                    <Field key={key}>
                      <FieldLabel htmlFor={`doc-${key}`}>{label}</FieldLabel>
                      <Input
                        id={`doc-${key}`}
                        dir="auto"
                        value={doc[key]}
                        disabled={!canManage}
                        onChange={(e) => patch({ [key]: e.target.value })}
                      />
                    </Field>
                  ))}
                </FieldGroup>
              </CardContent>
            </Card>
          </div>

          {/* Live preview — same renderer as the public page */}
          <div className="xl:sticky xl:top-6 xl:self-start">
            <Card>
              <CardHeader>
                <CardTitle>{t("notifications.policyPreview")}</CardTitle>
              </CardHeader>
              <CardContent>
                <div
                  dir={dir}
                  lang={language}
                  className="flex max-h-[75vh] flex-col gap-6 overflow-y-auto"
                >
                  <div className="flex flex-col items-center gap-2 text-center">
                    <h1 className="text-2xl font-semibold tracking-tight">
                      {doc.title}
                    </h1>
                    <div className="flex flex-wrap items-center justify-center gap-2">
                      <Badge variant="secondary">
                        {doc.versionLabel} {version}
                      </Badge>
                      <span className="text-sm text-muted-foreground">
                        {doc.effectiveDate}
                      </span>
                    </div>
                  </div>
                  <PolicyDocument
                    content={doc}
                    disclosure={disclosure}
                    dir={dir}
                  />
                </div>
              </CardContent>
            </Card>
          </div>
        </div>
      )}
    </div>
  )
}
