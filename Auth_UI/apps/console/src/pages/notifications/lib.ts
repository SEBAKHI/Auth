import type { Schemas } from "@astoom/api/types"

export type NotificationTemplateDto = Schemas["NotificationTemplateDto"]
export type NotificationTemplateDetailDto = Schemas["NotificationTemplateDetailDto"]
export type NotificationTemplateVersionDto = Schemas["NotificationTemplateVersionDto"]
export type NotificationTranslationDto = Schemas["NotificationTranslationDto"]
export type NotificationTypeDto = Schemas["NotificationTypeDto"]
export type NotificationLayoutDto = Schemas["NotificationLayoutDto"]
export type NotificationPreviewDto = Schemas["NotificationPreviewDto"]

/** One entry of a notification type's variable catalog (VariablesJson). */
export interface TemplateVariable {
  name: string
  description?: string
  example?: string
  required?: boolean
  /**
   * Exact Liquid snippet inserted on click. Defaults to "{{ name }}"; layout
   * slots override it (e.g. "{{ content | raw }}").
   */
  insertText?: string
}

/**
 * Parses a type's VariablesJson catalog; malformed JSON degrades to an empty
 * palette rather than breaking the editor.
 */
export function parseVariables(variablesJson: string | null | undefined): TemplateVariable[] {
  if (!variablesJson) return []
  try {
    const parsed: unknown = JSON.parse(variablesJson)
    if (!Array.isArray(parsed)) return []
    return parsed.filter(
      (entry): entry is TemplateVariable =>
        typeof entry === "object" && entry !== null && typeof (entry as TemplateVariable).name === "string"
    )
  } catch {
    return []
  }
}

/** Editable state of one translation tab in the template editor. */
export interface TranslationDraft {
  subject: string
  bodyHtml: string
  bodyText: string
}

export function toTranslationDrafts(
  translations: NotificationTranslationDto[] | null | undefined
): Record<string, TranslationDraft> {
  const drafts: Record<string, TranslationDraft> = {}
  for (const translation of translations ?? []) {
    if (!translation.languageCode) continue
    drafts[translation.languageCode] = {
      subject: translation.subject ?? "",
      bodyHtml: translation.bodyHtml ?? "",
      bodyText: translation.bodyText ?? "",
    }
  }
  return drafts
}
