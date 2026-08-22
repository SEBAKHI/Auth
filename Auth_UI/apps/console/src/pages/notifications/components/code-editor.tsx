import { redo, undo } from "@codemirror/commands"
import { liquid } from "@codemirror/lang-liquid"
import CodeMirror, {
  EditorView,
  type ReactCodeMirrorRef,
} from "@uiw/react-codemirror"
import { Image, Redo2, Undo2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@authsystem/ui/button"
import { useTheme } from "@authsystem/ui/theme-provider"
import { Tooltip, TooltipContent, TooltipTrigger } from "@authsystem/ui/tooltip"
import { InsertImageDialog } from "./insert-image-dialog"
import { insertAtCursor } from "./code-editor-utils"

/**
 * Liquid-in-HTML source editor for template bodies and layouts, with an undo/redo
 * toolbar (also Ctrl+Z / Ctrl+Y) and an insert-image action. Validation stays
 * server-side through the same Fluid parser that renders real sends.
 */
export const CodeEditor = React.forwardRef<
  ReactCodeMirrorRef,
  {
    value: string
    onChange: (value: string) => void
    minHeight?: string
    ariaLabel?: string
    /** Show the insert-image action (bodies yes; layouts optional). */
    allowImages?: boolean
    /**
     * Direction of the locale this surface is authoring. It reaches the copy
     * fields of the insert-image dialog, whose alt text lands inside the body
     * being edited. Left undefined on a surface shared by every locale — a
     * layout's HTML — where that copy follows the console instead.
     */
    contentDir?: "ltr" | "rtl"
    /**
     * Read-only for someone who may look but not save. Without it the page
     * offers an edit that can never be kept, and the unsaved-changes guard then
     * asks them to discard work they were never allowed to do.
     */
    readOnly?: boolean
  }
>(function CodeEditor(
  {
    value,
    onChange,
    minHeight = "260px",
    ariaLabel,
    allowImages = false,
    contentDir,
    readOnly = false,
  },
  forwardedRef
) {
  const { t } = useTranslation()
  const { resolvedTheme } = useTheme()
  const innerRef = React.useRef<ReactCodeMirrorRef>(null)
  const [imageOpen, setImageOpen] = React.useState(false)

  // Expose the CodeMirror ref to the parent while keeping our own handle.
  React.useImperativeHandle(
    forwardedRef,
    () => innerRef.current as ReactCodeMirrorRef
  )

  const extensions = React.useMemo(
    () => [liquid(), EditorView.lineWrapping],
    []
  )

  const runHistory = (command: typeof undo) => {
    const view = innerRef.current?.view
    if (view) {
      command(view)
      view.focus()
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-1">
        {readOnly ? null : (
          <>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={t("notifications.undo")}
                  onClick={() => runHistory(undo)}
                >
                  <Undo2 />
                </Button>
              </TooltipTrigger>
              <TooltipContent>{t("notifications.undo")}</TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon-sm"
                  aria-label={t("notifications.redo")}
                  onClick={() => runHistory(redo)}
                >
                  <Redo2 />
                </Button>
              </TooltipTrigger>
              <TooltipContent>{t("notifications.redo")}</TooltipContent>
            </Tooltip>
            {allowImages ? (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setImageOpen(true)}
              >
                <Image data-icon="inline-start" />
                {t("notifications.insertImage")}
              </Button>
            ) : null}
          </>
        )}
      </div>

      <div
        className="overflow-hidden rounded-md border"
        // Source code is LTR whatever the UI language is, and that includes the
        // alignment this block `dir` carries: an RTL gutter would put the line
        // numbers on the wrong edge.
        // eslint-disable-next-line no-restricted-syntax
        dir="ltr"
      >
        <CodeMirror
          ref={innerRef}
          value={value}
          onChange={onChange}
          readOnly={readOnly}
          extensions={extensions}
          theme={resolvedTheme === "dark" ? "dark" : "light"}
          minHeight={minHeight}
          aria-label={ariaLabel}
          basicSetup={{
            foldGutter: false,
            highlightActiveLine: true,
            lineNumbers: true,
          }}
        />
      </div>

      {allowImages && !readOnly ? (
        <InsertImageDialog
          open={imageOpen}
          onOpenChange={setImageOpen}
          contentDir={contentDir}
          onInsert={(snippet) => insertAtCursor(innerRef, snippet)}
        />
      ) : null}
    </div>
  )
})
