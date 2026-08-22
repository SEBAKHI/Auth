import { describe, expect, it, vi } from "vitest"

import { insertAtCursor } from "./code-editor-utils"

describe("insertAtCursor", () => {
  it("returns false before CodeMirror has mounted", () => {
    expect(insertAtCursor({ current: null }, "token")).toBe(false)
  })

  it("replaces the active selection, moves the caret, and restores focus", () => {
    const dispatch = vi.fn()
    const focus = vi.fn()
    const editorRef = {
      current: {
        view: {
          state: { selection: { main: { from: 4, to: 9 } } },
          dispatch,
          focus,
        },
      },
    }

    expect(insertAtCursor(editorRef as never, "{{name}}")).toBe(true)
    expect(dispatch).toHaveBeenCalledWith({
      changes: { from: 4, to: 9, insert: "{{name}}" },
      selection: { anchor: 12 },
    })
    expect(focus).toHaveBeenCalledOnce()
  })
})
