import { existsSync, readdirSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { LOGO_HEIGHT, LOGO_MAX_ASPECT, LogoMark, PlatformMark, resolveLogoHeight } from "./logo-mark"

/** The repository root: the first folder above the working directory that holds `Auth_UI`. */
function repoRoot(): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    if (existsSync(join(dir, "Auth_UI")) && existsSync(join(dir, "Auth"))) return dir
    dir = dirname(dir)
  }
  throw new Error(`no repository root above ${process.cwd()}`)
}

function repoFile(relative: string): string {
  let dir = process.cwd()
  for (let i = 0; i < 8; i++) {
    const candidate = join(dir, relative)
    if (existsSync(candidate)) {
      // The C# file may start with a byte-order mark and use CRLF line endings.
      const text = readFileSync(candidate, "utf8").split("\r\n").join("\n")
      return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text
    }
    dir = dirname(dir)
  }
  throw new Error(`${relative} not found above ${process.cwd()}`)
}

/**
 * The logo heights of the sign-in pages (UI batch P3). The API validates the range and owns the
 * defaults; this app draws them. Breaks: change a number on either side; let a stored value
 * outside the range through; size the width cap apart from the height.
 */
describe("the logo heights", () => {
  it("are the API's own numbers", () => {
    const source = repoFile("Auth/Auth.Domain/Constants/LogoHeights.cs")
    const constant = (name: string) => {
      const value = new RegExp(`const int ${name} = (\\d+);`).exec(source)?.[1]
      expect(value, `${name} in LogoHeights.cs`).toBeDefined()
      return Number(value)
    }

    expect(constant("MinPx")).toBe(LOGO_HEIGHT.min)
    expect(constant("MaxPx")).toBe(LOGO_HEIGHT.max)
    expect(constant("PlatformDefaultPx")).toBe(LOGO_HEIGHT.platformDefault)
    expect(constant("ApplicationDefaultPx")).toBe(LOGO_HEIGHT.applicationDefault)
  })

  it("are the owner's decisions: 60px for the platform (1.5 × 40px), 48px for an application, 24–120px", () => {
    expect(LOGO_HEIGHT).toEqual({ min: 24, max: 120, platformDefault: 60, applicationDefault: 48 })
    // The platform's width cap scales by the same 1.5: 160px at 40px became 240px at 60px.
    expect(LOGO_HEIGHT.platformDefault * LOGO_MAX_ASPECT.platform).toBe(240)
    // An application's keeps the 240px its 48px logo always had.
    expect(LOGO_HEIGHT.applicationDefault * LOGO_MAX_ASPECT.application).toBe(240)
  })

  it.each([
    [null, 60],
    [undefined, 60],
    [24, 24],
    [120, 120],
    ["96", 96],
    [23, 60],
    [121, 60],
    [60.5, 60],
    ["wide", 60],
  ] as const)("reads a stored %s as %s", (stored, expected) => {
    expect(resolveLogoHeight(stored, LOGO_HEIGHT.platformDefault)).toBe(expected)
  })
})

/**
 * Every sign-in and flow page draws its logos through `AuthLayout`, so one primitive sizes them
 * all (D12). A page that draws an image of its own beside the layout would escape the heights;
 * the browser suite measures the routes a cold visit reaches, and this holds every page.
 */
describe("the pages of the sign-in layout", () => {
  function sources(dir: string): string[] {
    return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
      const path = join(dir, entry.name)
      if (entry.isDirectory()) return entry.name === "node_modules" || entry.name === "dist" ? [] : sources(path)
      return /\.tsx$/.test(entry.name) && !/\.test\.tsx$/.test(entry.name) ? [path] : []
    })
  }
  const root = repoRoot()
  const pages = ["apps", "packages"]
    .flatMap((dir) => sources(join(root, "Auth_UI", dir)))
    .filter((file) => readFileSync(file, "utf8").includes("<AuthLayout"))
    .map((file) => ({ file, source: readFileSync(file, "utf8") }))

  it("are found at all", () => {
    // A floor, so an emptied scan cannot pass the case below.
    expect(pages.length).toBeGreaterThanOrEqual(15)
  })

  it("draw no logo of their own", () => {
    const own = pages
      .filter(({ source }) => /<img\b|<LogoMark\b|<PlatformMark\b|<BrandingLogo\b/.test(source))
      .map(({ file }) => file)
    expect(own).toEqual([])
  })
})

describe("LogoMark", () => {
  it("sizes the box by its height, caps the width at a multiple of it, and never crops", () => {
    render(<LogoMark src="wide.webp" alt="Acme" heightPx={120} maxAspect={LOGO_MAX_ASPECT.application} />)

    const logo = screen.getByRole("img", { name: "Acme" })
    expect(logo.style.getPropertyValue("--logo-h")).toBe("120px")
    expect(logo.style.getPropertyValue("--logo-w-max")).toBe("600px")
    // Auto width keeps the image's proportions; the column is a cap too, so a 120px wordmark
    // at 375px shrinks inside its box instead of overflowing the page.
    expect(logo).toHaveClass("h-(--logo-h)", "w-auto", "max-w-[min(var(--logo-w-max),100%)]", "object-contain")
    expect(logo.className).not.toMatch(/object-cover|rounded-full/)
  })
})

describe("PlatformMark", () => {
  it("holds the place at the same height while the branding is unknown, and asserts nothing", () => {
    const { container } = render(<PlatformMark src={null} name="Acme" heightPx={60} pending />)

    const box = container.querySelector<HTMLElement>('[data-slot="logo-mark"]')!
    expect(box).toHaveAttribute("aria-hidden", "true")
    expect(box.style.getPropertyValue("--logo-h")).toBe("60px")
    expect(screen.queryByText("Acme")).toBeNull()
    expect(container.querySelector("svg")).toBeNull()
  })

  it("draws the uploaded logo, named by the platform", () => {
    render(<PlatformMark src="logo.webp" name="Acme" heightPx={72} />)

    expect(screen.getByRole("img", { name: "Acme" }).style.getPropertyValue("--logo-h")).toBe("72px")
  })

  it("draws the default tile at the chosen height, with the name beside it, when no logo is uploaded", () => {
    const { container } = render(<PlatformMark src={null} name="Acme" heightPx={90} />)

    const tile = container.querySelector<HTMLElement>('[data-slot="logo-mark"]')!
    expect(tile.style.getPropertyValue("--logo-h")).toBe("90px")
    expect(tile).toHaveClass("size-(--logo-h)")
    expect(screen.getByText("Acme")).toBeInTheDocument()
  })
})
