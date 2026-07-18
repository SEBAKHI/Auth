import { redo, undo } from "@codemirror/commands"
import { liquid } from "@codemirror/lang-liquid"
import CodeMirror, { EditorView, type ReactCodeMirrorRef } from "@uiw/react-codemirror"
import { Image, Redo2, Undo2 } from "lucide-react"
import * as React from "react"
import { useTranslation } from "react-i18next"

import { Button } from "@astoom/ui/button"
import { useTheme } from "@astoom/ui/theme-provider"
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@astoom/ui/tooltip"
import { InsertImageDialog } from "./insert-image-dialog"

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
  }
>(function CodeEditor(
  { value, onChange, minHeight = "260px", ariaLabel, allowImages = false },
  forwardedRef
) {
  const { t } = useTranslation()
  const { resolvedTheme } = useTheme()
  const innerRef = React.useRef<ReactCodeMirrorRef>(null)
  const [imageOpen, setImageOpen] = React.useState(false)

  // Expose the CodeMirror ref to the parent while keeping our own handle.
  React.useImperativeHandle(forwardedRef, () => innerRef.current as ReactCodeMirrorRef)

  const extensions = React.useMemo(() => [liquid(), EditorView.lineWrapping], [])

  const runHistory = (command: typeof undo) => {
    const view = innerRef.current?.view
    if (view) {
      command(view)
      view.focus()
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-1">
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
            <Image />
            {t("notifications.insertImage")}
          </Button>
        ) : null}
      </div>

      <div className="overflow-hidden rounded-md border" dir="ltr">
        <CodeMirror
          ref={innerRef}
          value={value}
          onChange={onChange}
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

      {allowImages ? (
        <InsertImageDialog
          open={imageOpen}
          onOpenChange={setImageOpen}
          onInsert={(snippet) => insertAtCursor(innerRef, snippet)}
        />
      ) : null}
    </div>
  )
})

/** Inserts text at the current cursor position of a CodeMirror instance. */
export function insertAtCursor(
  editorRef: React.RefObject<ReactCodeMirrorRef | null>,
  text: string
): boolean {
  const view = editorRef.current?.view
  if (!view) return false

  const { from, to } = view.state.selection.main
  view.dispatch({
    changes: { from, to, insert: text },
    selection: { anchor: from + text.length },
  })
  view.focus()
  return true
}
