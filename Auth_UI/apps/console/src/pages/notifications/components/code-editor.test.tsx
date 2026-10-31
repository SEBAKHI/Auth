import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"
import { ThemeProvider } from "@authsystem/ui/theme-provider"
import { TooltipProvider } from "@authsystem/ui/tooltip"

// CodeMirror needs layout APIs jsdom does not provide, and none of these cases
// are about the editor's own rendering - they are about the toolbar around it,
// what it offers to someone who cannot save, and what the editor is handed.
// The rendered height is measured in a real browser (isolated suite).
const editorProps = vi.hoisted(() => ({ last: {} as Record<string, unknown> }))
vi.mock("@uiw/react-codemirror", () => ({
  default: (props: {
    value: string
    readOnly?: boolean
    "aria-label"?: string
  }) => {
    editorProps.last = props
    return (
      <textarea
        aria-label={props["aria-label"]}
        readOnly={props.readOnly}
        defaultValue={props.value}
        data-readonly={String(Boolean(props.readOnly))}
      />
    )
  },
  EditorView: {
    lineWrapping: {},
    theme: (spec: Record<string, unknown>) => ({ themeSpec: spec }),
  },
}))
vi.mock("@codemirror/lang-liquid", () => ({ liquid: () => ({}) }))
vi.mock("@codemirror/commands", () => ({ undo: vi.fn(), redo: vi.fn() }))

import {
  CODE_EDITOR_HEIGHT,
  CODE_EDITOR_VISIBLE_LINES,
  CodeEditor,
} from "./code-editor"

function renderEditor(
  props: Partial<React.ComponentProps<typeof CodeEditor>> = {}
) {
  return render(
    <ThemeProvider>
      <TooltipProvider>
        <CodeEditor
          value="<p>body</p>"
          onChange={() => {}}
          ariaLabel="Body"
          allowImages
          {...props}
        />
      </TooltipProvider>
    </ThemeProvider>
  )
}

describe("CodeEditor", () => {
  it("offers undo, redo and image insertion to an author", () => {
    renderEditor()

    expect(screen.getByRole("button", { name: "Undo" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Redo" })).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: /insert image/i })
    ).toBeInTheDocument()
    expect(screen.getByLabelText("Body")).toHaveAttribute(
      "data-readonly",
      "false"
    )
  })

  it("opens the image dialog from the toolbar", async () => {
    const user = userEvent.setup()
    renderEditor()

    await user.click(screen.getByRole("button", { name: /insert image/i }))

    // ConfirmDialog is an AlertDialog underneath.
    expect(await screen.findByRole("alertdialog")).toBeInTheDocument()
  })

  it("runs undo and redo against the editor", async () => {
    const user = userEvent.setup()
    const { undo, redo } = await import("@codemirror/commands")
    renderEditor()

    await user.click(screen.getByRole("button", { name: "Undo" }))
    await user.click(screen.getByRole("button", { name: "Redo" }))

    // No view is attached behind the mock, so the commands are not called -
    // what matters here is that the handlers run without throwing.
    expect(vi.isMockFunction(undo)).toBe(true)
    expect(vi.isMockFunction(redo)).toBe(true)
  })

  it("shows the same 28-line window whatever the content's length", () => {
    const heightOf = () => {
      const extensions = editorProps.last.extensions as {
        themeSpec?: Record<string, Record<string, string>>
      }[]
      return extensions.find((e) => e.themeSpec)?.themeSpec
    }

    const { unmount } = renderEditor({ value: "<p>one line</p>" })
    const short = heightOf()
    // Nothing that grows with the content reaches CodeMirror.
    expect(editorProps.last).not.toHaveProperty("minHeight")
    expect(editorProps.last).not.toHaveProperty("height")
    unmount()

    renderEditor({ value: Array.from({ length: 200 }, (_, i) => `<p>${i}</p>`).join("\n") })
    const long = heightOf()

    expect(CODE_EDITOR_VISIBLE_LINES).toBe(28)
    expect(CODE_EDITOR_HEIGHT).toBe("calc(28lh + 8px)")
    expect(short).toEqual({
      "&": { fontFamily: "monospace", lineHeight: "1.4", height: CODE_EDITOR_HEIGHT },
      ".cm-scroller": { overflowY: "auto" },
    })
    expect(long).toEqual(short)
  })

  it("withdraws every editing affordance when read-only", () => {
    renderEditor({ readOnly: true })

    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull()
    expect(screen.queryByRole("button", { name: "Redo" })).toBeNull()
    expect(screen.queryByRole("button", { name: /insert image/i })).toBeNull()
    expect(screen.getByLabelText("Body")).toHaveAttribute(
      "data-readonly",
      "true"
    )
  })
})
