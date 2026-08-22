import * as React from "react"

type EditableField = HTMLInputElement | HTMLTextAreaElement

/** Remembers the last focused text field and inserts through React's setter. */
export function useFocusedField() {
  const fieldRef = React.useRef<EditableField | null>(null)

  const onFocusCapture = React.useCallback((event: React.FocusEvent) => {
    const target = event.target as HTMLElement
    if (
      target instanceof HTMLInputElement ||
      target instanceof HTMLTextAreaElement
    ) {
      fieldRef.current = target
    }
  }, [])

  const insert = React.useCallback((text: string) => {
    const field = fieldRef.current
    if (!field || field.disabled || field.readOnly) return false

    const start = field.selectionStart ?? field.value.length
    const end = field.selectionEnd ?? start
    const next = field.value.slice(0, start) + text + field.value.slice(end)
    const prototype =
      field instanceof HTMLTextAreaElement
        ? HTMLTextAreaElement.prototype
        : HTMLInputElement.prototype
    const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set
    setter?.call(field, next)
    field.dispatchEvent(new Event("input", { bubbles: true }))

    const caret = start + text.length
    field.focus()
    field.setSelectionRange(caret, caret)
    return true
  }, [])

  return { onFocusCapture, insert }
}
