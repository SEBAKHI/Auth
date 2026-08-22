import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"
import {
  Breadcrumb,
  BreadcrumbEllipsis,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "./breadcrumb"
import { Checkbox } from "./checkbox"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSeparator,
  FieldSet,
  FieldTitle,
} from "./field"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
  InputGroupText,
  InputGroupTextarea,
} from "./input-group"
import { Kbd, KbdGroup } from "./kbd"
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from "./table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "./tabs"
import { Toggle } from "./toggle"
import { CopyButton } from "./common/copy-button"
import { QrCode } from "./common/qr-code"
import { SearchInput } from "./common/search-input"

describe("UI primitive contracts", () => {
  beforeEach(() => {
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  it("renders semantic layout primitives and their optional branches", () => {
    render(
      <>
        <Breadcrumb>
          <BreadcrumbList>
            <BreadcrumbItem>
              <BreadcrumbLink href="/">Home</BreadcrumbLink>
            </BreadcrumbItem>
            <BreadcrumbSeparator />
            <BreadcrumbItem>
              <BreadcrumbPage>Current</BreadcrumbPage>
            </BreadcrumbItem>
            <BreadcrumbEllipsis />
          </BreadcrumbList>
        </Breadcrumb>
        <FieldSet>
          <FieldLegend variant="label">Legend</FieldLegend>
          <FieldGroup>
            <Field orientation="horizontal">
              <FieldLabel htmlFor="name">Name</FieldLabel>
              <FieldContent>
                <FieldTitle>Title</FieldTitle>
                <FieldDescription>Description</FieldDescription>
              </FieldContent>
            </Field>
            <FieldSeparator>or</FieldSeparator>
            <FieldError errors={[{ message: "Required" }, { message: "Short" }]} />
          </FieldGroup>
        </FieldSet>
        <InputGroup>
          <InputGroupAddon>start</InputGroupAddon>
          <InputGroupInput aria-label="group input" />
          <InputGroupText>text</InputGroupText>
          <InputGroupButton>action</InputGroupButton>
          <InputGroupTextarea aria-label="group textarea" />
        </InputGroup>
        <KbdGroup>
          <Kbd>Ctrl</Kbd><Kbd>K</Kbd>
        </KbdGroup>
        <Table>
          <TableCaption>Caption</TableCaption>
          <TableHeader><TableRow><TableHead>Head</TableHead></TableRow></TableHeader>
          <TableBody><TableRow><TableCell>Cell</TableCell></TableRow></TableBody>
          <TableFooter><TableRow><TableCell>Foot</TableCell></TableRow></TableFooter>
        </Table>
        <Tabs defaultValue="one">
          <TabsList variant="line"><TabsTrigger value="one">One</TabsTrigger></TabsList>
          <TabsContent value="one">Panel</TabsContent>
        </Tabs>
        <Toggle aria-label="toggle">Toggle</Toggle>
        <Checkbox aria-label="check" />
        <QrCode value="otpauth://example" size={64} />
      </>
    )

    expect(screen.getByRole("navigation", { name: "breadcrumb" })).toBeVisible()
    expect(screen.getByText("Required")).toBeVisible()
    expect(screen.getByRole("table")).toBeVisible()
    expect(screen.getByText("Panel")).toBeVisible()
    expect(document.querySelector("svg")).not.toBeNull()
  })

  it("covers search clear and clipboard success/error feedback", async () => {
    const onChange = vi.fn()
    const { rerender } = render(
      <SearchInput value="alice" onChange={onChange} />
    )
    await userEvent.click(screen.getByRole("button", { name: "Clear" }))
    expect(onChange).toHaveBeenCalledWith("")

    rerender(<CopyButton value="secret" label="copy secret" />)
    await userEvent.click(screen.getByRole("button", { name: "copy secret" }))
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith("secret")

    vi.mocked(navigator.clipboard.writeText).mockRejectedValueOnce(
      new Error("denied")
    )
    await userEvent.click(screen.getByRole("button", { name: "copy secret" }))
    expect(navigator.clipboard.writeText).toHaveBeenCalledTimes(2)
  })
})
