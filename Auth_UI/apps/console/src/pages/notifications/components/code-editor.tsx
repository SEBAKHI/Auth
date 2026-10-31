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
 * Every source editor shows the same window, whatever it holds: this many
 * lines. A longer body scrolls inside the editor; a shorter one leaves the rest
 * blank. Before, the editor grew with its content, so the page under it moved
 * on every added line and a long body pushed the save bar out of reach.
 */
export const CODE_EDITOR_VISIBLE_LINES = 28

/**
 * The window is measured in editor lines (`lh`), not in pixels, so it stays 28
 * lines at any font size. The 8px is CodeMirror's own padding around the
 * content (4px above, 4px below).
 */
export const CODE_EDITOR_HEIGHT = `calc(${CODE_EDITOR_VISIBLE_LINES}lh + 8px)`

/**
 * The height goes on the editor box: the React wrapper pins the scroller to
 * `height: 100% !important` of it. CodeMirror's base theme draws the code in
 * `monospace` at a line height of 1.4; the box declares the same font and line
 * height, so `lh` on the box is one line of code. The font matters, not only
 * the line height: where no ancestor sets a font size, browsers draw a bare
 * `monospace` smaller than the page's font, and a box in the page's font would
 * then hold about 34 code lines instead of 28.
 */
const fixedHeight = EditorView.theme({
  "&": { fontFamily: "monospace", lineHeight: "1.4", height: CODE_EDITOR_HEIGHT },
  ".cm-scroller": { overflowY: "auto" },
})

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
    /** What inserted images are for: `Panel` for a sign-in page design (larger images). */
    imagePurpose?: "Panel"
  }
>(function CodeEditor(
  {
    value,
    onChange,
    ariaLabel,
    allowImages = false,
    contentDir,
    readOnly = false,
    imagePurpose,
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
    () => [liquid(), EditorView.lineWrapping, fixedHeight],
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
        // `clip`, not `hidden`: it rounds the corners without making this box
        // a scroll container, which a flex column could then shrink below the
        // editor's height and cut its last lines off.
        className="overflow-clip rounded-md border"
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
          purpose={imagePurpose}
          onInsert={(snippet) => insertAtCursor(innerRef, snippet)}
        />
      ) : null}
    </div>
  )
})
