import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it } from "vitest"

import "@authsystem/i18n"
import { ThemeProvider } from "@authsystem/ui/theme-provider"
import { TooltipProvider } from "@authsystem/ui/tooltip"

import { PreviewPane } from "./preview-pane"
import { PREVIEW_SCHEME_STORAGE_KEY } from "./use-preview-scheme"

const PREVIEW = {
  html: "<p>hello</p>",
  text: "hello",
  subject: "Hello",
  languageCode: "en",
}

function renderPane(theme: "light" | "dark") {
  localStorage.setItem("theme", theme)
  return render(
    <ThemeProvider>
      <TooltipProvider>
        <PreviewPane preview={PREVIEW} />
      </TooltipProvider>
    </ThemeProvider>
  )
}

/** What the iframe tells the embedded email about the client it is simulating. */
function frameScheme() {
  return screen.getByTitle("Preview").style.colorScheme
}

describe("PreviewPane colour scheme", () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => localStorage.clear())

  it.each(["light", "dark"] as const)(
    "opens a %s console against a matching client",
    (theme) => {
      renderPane(theme)
      expect(frameScheme()).toBe(theme)
    }
  )

  it("keeps a manual override without touching the console theme", async () => {
    const user = userEvent.setup()
    renderPane("dark")

    await user.click(screen.getByRole("radio", { name: "Light mode" }))

    expect(frameScheme()).toBe("light")
    // The site theme is a separate setting and must not move with the preview.
    expect(localStorage.getItem("theme")).toBe("dark")
    expect(document.documentElement.classList.contains("dark")).toBe(true)
    expect(localStorage.getItem(PREVIEW_SCHEME_STORAGE_KEY)).toBe("light")
  })
})
