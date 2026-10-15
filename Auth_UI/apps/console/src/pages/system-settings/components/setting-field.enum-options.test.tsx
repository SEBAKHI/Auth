import { render, screen } from "@testing-library/react"
import { useForm, type Control, type FieldValues } from "react-hook-form"
import { MemoryRouter } from "react-router-dom"
import { describe, expect, it, vi } from "vitest"

import { en } from "@authsystem/i18n/locales/en"
import { Form } from "@authsystem/ui/form"

import type { SystemSettingsField } from "../lib/sections"

/** Resolves keys against the English catalog, with i18next's defaultValue fallback. */
function translate(key: string, options?: { defaultValue?: string }): string {
  const found = key
    .split(".")
    .reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], en)
  return typeof found === "string" ? found : (options?.defaultValue ?? key)
}

vi.mock("react-i18next", () => ({
  useTranslation: () => ({ t: translate }),
  initReactI18next: { type: "3rdParty", init: () => {} },
}))

const { SettingField } = await import("./setting-field")

function renderField(sectionI18n: string, field: SystemSettingsField) {
  function Host() {
    const form = useForm<FieldValues>({ defaultValues: {} })
    return (
      <Form {...form}>
        <SettingField
          control={form.control as Control<FieldValues>}
          sectionI18n={sectionI18n}
          field={field}
        />
      </Form>
    )
  }
  return render(
    <MemoryRouter>
      <Host />
    </MemoryRouter>
  )
}

/**
 * OI-165: an enum value shows its label when the section names it under
 * `{field}Options`, and its raw value otherwise, so every existing enum field
 * renders as it always did.
 */
describe("SettingField: enum options", () => {
  it("shows the three labels of StaySignedInMode", () => {
    renderField("identityProvider", {
      path: "StaySignedInMode",
      kind: "enum",
      allowedValues: ["PersonChooses", "AlwaysBrowserSession", "AlwaysStaySignedIn"],
      effectiveValue: "PersonChooses",
      defaultValue: "PersonChooses",
      source: "default",
    })

    const options = screen.getAllByRole("radio").map((option) => option.textContent)
    expect(options).toEqual([
      "The person (shows “Stay signed in”)",
      "Always a browser session",
      "Always stay signed in",
    ])
    // The default line names the same option, not the raw value.
    expect(document.body.textContent).toContain("The person (shows “Stay signed in”)")
    expect(document.body.textContent).not.toContain("PersonChooses")
  })

  it("keeps the raw values of an enum field with no option labels", () => {
    renderField("password", {
      path: "ConsecutiveFailureMode",
      kind: "enum",
      allowedValues: ["Enforce", "Legacy"],
      effectiveValue: "Enforce",
      defaultValue: "Enforce",
      source: "default",
    })

    expect(screen.getAllByRole("radio").map((option) => option.textContent)).toEqual(["Enforce", "Legacy"])
    expect(document.body.textContent).toContain("Enforce")
  })
})
