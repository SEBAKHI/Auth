import type { Column } from "@tanstack/react-table"
import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "./dialog"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuShortcut,
} from "./dropdown-menu"
import {
  Popover,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
} from "./popover"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from "./select"
import { DataTableFacetedFilter } from "./data-table/data-table-faceted-filter"

describe("overlay primitive contracts", () => {
  it("renders controlled dialog, menu, popover, and select surfaces", () => {
    render(
      <>
        <Dialog open>
          <DialogContent>
            <DialogHeader><DialogTitle>Dialog title</DialogTitle><DialogDescription>Dialog body</DialogDescription></DialogHeader>
            <DialogFooter><DialogClose>Close</DialogClose></DialogFooter>
          </DialogContent>
        </Dialog>
        <DropdownMenu open>
          <DropdownMenuContent>
            <DropdownMenuLabel>Menu</DropdownMenuLabel>
            <DropdownMenuGroup><DropdownMenuItem>Item<DropdownMenuShortcut>⌘I</DropdownMenuShortcut></DropdownMenuItem></DropdownMenuGroup>
            <DropdownMenuCheckboxItem checked>Checked</DropdownMenuCheckboxItem>
            <DropdownMenuRadioGroup value="a"><DropdownMenuRadioItem value="a">Radio</DropdownMenuRadioItem></DropdownMenuRadioGroup>
            <DropdownMenuSeparator />
          </DropdownMenuContent>
        </DropdownMenu>
        <Popover open>
          <PopoverContent>
            <PopoverHeader><PopoverTitle>Popover</PopoverTitle><PopoverDescription>Details</PopoverDescription></PopoverHeader>
          </PopoverContent>
        </Popover>
        <Select open value="a">
          <SelectTrigger aria-label="select"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectGroup><SelectLabel>Choices</SelectLabel><SelectItem value="a">Alpha</SelectItem><SelectSeparator /><SelectItem value="b">Beta</SelectItem></SelectGroup>
          </SelectContent>
        </Select>
      </>
    )

    expect(screen.getByText("Dialog title")).toBeInTheDocument()
    expect(screen.getByText("Menu")).toBeInTheDocument()
    expect(screen.getByText("Popover")).toBeInTheDocument()
    expect(screen.getAllByText("Alpha").length).toBeGreaterThan(0)
  })

  it("derives, toggles, and clears faceted values", async () => {
    const setFilterValue = vi.fn()
    const column = {
      getFacetedUniqueValues: () => new Map<unknown, number>([["active", 2], ["disabled", 1]]),
      getFilterValue: () => ["active"],
      setFilterValue,
    } as unknown as Column<unknown, unknown>
    render(<DataTableFacetedFilter column={column} title="Status" />)

    await userEvent.click(screen.getByRole("button", { name: /Status/ }))
    await userEvent.click(screen.getByRole("option", { name: /active/i }))
    expect(setFilterValue).toHaveBeenCalledWith(undefined)
    await userEvent.click(screen.getByRole("option", { name: /Clear/i }))
    expect(setFilterValue).toHaveBeenLastCalledWith(undefined)
  })
})
