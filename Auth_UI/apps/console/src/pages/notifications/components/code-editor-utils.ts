import type * as React from "react"
import type { ReactCodeMirrorRef } from "@uiw/react-codemirror"

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
