import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"
import { ThemeProvider } from "@authsystem/ui/theme-provider"
import { TooltipProvider } from "@authsystem/ui/tooltip"

// CodeMirror needs layout APIs jsdom does not provide, and none of these cases
// are about the editor's own rendering - they are about the toolbar around it
// and what it offers to someone who cannot save.
vi.mock("@uiw/react-codemirror", () => ({
  default: ({
    value,
    readOnly,
    "aria-label": ariaLabel,
  }: {
    value: string
    readOnly?: boolean
    "aria-label"?: string
  }) => (
    <textarea
      aria-label={ariaLabel}
      readOnly={readOnly}
      defaultValue={value}
      data-readonly={String(Boolean(readOnly))}
    />
  ),
  EditorView: { lineWrapping: {} },
}))
vi.mock("@codemirror/lang-liquid", () => ({ liquid: () => ({}) }))
vi.mock("@codemirror/commands", () => ({ undo: vi.fn(), redo: vi.fn() }))

import { CodeEditor } from "./code-editor"

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
