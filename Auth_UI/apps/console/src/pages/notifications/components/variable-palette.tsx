import { useTranslation } from "react-i18next"

import { Badge } from "@authsystem/ui/badge"
import { Button } from "@authsystem/ui/button"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@authsystem/ui/tooltip"
import type { TemplateVariable } from "../lib"

/**
 * A clickable placeholder palette: one click inserts the Liquid snippet at the
 * editor cursor. Used for the type's variable catalog (the contract with the
 * sending code — variables outside it fail the publish gate) and, with a custom
 * title and insertText, for the fixed renderer-injected layout slots.
 */
export function VariablePalette({
  variables,
  onInsert,
  title,
}: {
  variables: TemplateVariable[]
  /**
   * Omitted for someone who may read the template but not save it. The catalog
   * still reads as documentation - the names, their meaning and an example -
   * it just stops offering an edit that could never be kept.
   */
  onInsert?: (placeholder: string) => void
  /** Heading override; defaults to the "Variables" label. */
  title?: string
}) {
  const { t } = useTranslation()

  if (variables.length === 0) return null

  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm font-medium">{title ?? t("notifications.variables")}</p>
      <div className="flex flex-wrap gap-2">
        {variables.map((variable) => (
          <Tooltip key={variable.name}>
            <TooltipTrigger asChild>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={!onInsert}
                onClick={() =>
                  onInsert?.(variable.insertText ?? `{{ ${variable.name} }}`)
                }
              >
                {variable.name}
                {variable.required ? (
                  <Badge variant="secondary">{t("notifications.required")}</Badge>
                ) : null}
              </Button>
            </TooltipTrigger>
            <TooltipContent>
              <p>{variable.description || variable.name}</p>
              {variable.example ? (
                <p className="text-muted-foreground">
                  {t("notifications.example")}:{" "}
                  {/* The label is localized and the example is not, so the run
                      needs its own isolate — and it has to be an inline `bdi`,
                      since a `dir` on the `p` would re-resolve its inherited
                      `text-align: start`. */}
                  <bdi dir="auto">{variable.example}</bdi>
                </p>
              ) : null}
            </TooltipContent>
          </Tooltip>
        ))}
      </div>
    </div>
  )
}
