import { render, screen, within } from "@testing-library/react"
import * as React from "react"
import { useForm, type ErrorOption } from "react-hook-form"
import { describe, expect, it } from "vitest"

import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "./form"
import { Input } from "./input"

function Harness({ error }: { error: ErrorOption }) {
  const form = useForm<{ password: string }>({
    defaultValues: { password: "" },
  })
  React.useEffect(() => {
    form.setError("password", error)
  }, [form, error])

  return (
    <Form {...form}>
      <FormField
        control={form.control}
        name="password"
        render={({ field }) => (
          <FormItem>
            <FormLabel>Password</FormLabel>
            <FormControl>
              <Input {...field} />
            </FormControl>
            <FormMessage />
          </FormItem>
        )}
      />
    </Form>
  )
}

describe("FormMessage", () => {
  it("renders a single message as plain text", async () => {
    render(<Harness error={{ type: "server", message: "Too short." }} />)

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent("Too short.")
    expect(within(alert).queryByRole("list")).toBeNull()
  })

  it("renders every sentence handed over through `types` as one list", async () => {
    render(
      <Harness
        error={{
          type: "server",
          message: "Password must be at least 12 characters long.",
          types: {
            "server-0": "Password must be at least 12 characters long.",
            "server-1": "Password must contain at least one digit.",
          },
        }}
      />
    )

    const alert = await screen.findByRole("alert")
    const items = within(alert).getAllByRole("listitem")
    expect(items.map((item) => item.textContent)).toEqual([
      "Password must be at least 12 characters long.",
      "Password must contain at least one digit.",
    ])
  })

  it("keeps the plain rendering when `types` holds only one sentence", async () => {
    render(
      <Harness
        error={{
          type: "server",
          message: "Only this.",
          types: { "server-0": "Only this." },
        }}
      />
    )

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent("Only this.")
    expect(within(alert).queryByRole("list")).toBeNull()
  })
})
