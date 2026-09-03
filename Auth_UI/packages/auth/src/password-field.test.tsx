import { zodResolver } from "@hookform/resolvers/zod"
import { fireEvent, render, screen } from "@testing-library/react"
import { useForm } from "react-hook-form"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { z } from "zod"

import type { PasswordPolicy } from "@authsystem/api/password-policy"
import { Form } from "@authsystem/ui/form"

import { PasswordField } from "./password-field"
import {
  applyPasswordServerErrors,
  passwordIssue,
  passwordSchema,
} from "./password-rules"

const usePasswordPolicyMock = vi.fn()
vi.mock("@authsystem/api/password-policy", async (importOriginal) => {
  const actual =
    await importOriginal<typeof import("@authsystem/api/password-policy")>()
  return { ...actual, usePasswordPolicy: () => usePasswordPolicyMock() }
})

const STRICT: PasswordPolicy = {
  minimumLength: 8,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireSpecialCharacter: true,
}

const TWO_RULES_BROKEN = {
  status: 400,
  title: "Password.TooShort",
  detail: "Password must be at least 12 characters long.",
  errors: [
    {
      code: "Password.TooShort",
      description: "Password must be at least 12 characters long.",
    },
    {
      code: "Password.RequiresDigit",
      description: "Password must contain at least one digit.",
    },
    // Not about the password: must be left alone for the caller's own feedback.
    {
      code: "User.DuplicateEmail",
      description: "This email is already registered.",
    },
  ],
}

describe("passwordIssue", () => {
  it("asks for a value before judging it", () => {
    expect(passwordIssue("", STRICT)).toBe("This field is required.")
  })

  it("gives one sentence when any enabled rule is unmet, none when all are met", () => {
    expect(passwordIssue("abc", STRICT)).toBe(
      "The password does not meet all the requirements."
    )
    expect(passwordIssue("Abcdefg1!", STRICT)).toBeUndefined()
  })

  it("enforces only the registry floor while the policy is unknown", () => {
    expect(passwordIssue("abcde", undefined)).toBe(
      "The password does not meet all the requirements."
    )
    expect(passwordIssue("abcdef", undefined)).toBeUndefined()
  })
})

describe("passwordSchema", () => {
  it("is a string schema that applies passwordIssue", () => {
    const schema = passwordSchema(STRICT)

    expect(schema.safeParse("Abcdefg1!").success).toBe(true)

    const refused = schema.safeParse("abc")
    expect(refused.success).toBe(false)
    expect(refused.error?.issues.map((issue) => issue.message)).toEqual([
      "The password does not meet all the requirements.",
    ])
  })
})

describe("applyPasswordServerErrors", () => {
  it("places every password sentence on the field, focuses it, and reports true", () => {
    const form = { setError: vi.fn(), setFocus: vi.fn() }

    const handled = applyPasswordServerErrors<{ password: string }>(
      form,
      "password",
      TWO_RULES_BROKEN
    )

    expect(handled).toBe(true)
    expect(form.setError).toHaveBeenCalledWith("password", {
      type: "server",
      message: "Password must be at least 12 characters long.",
      types: {
        "server-0": "Password must be at least 12 characters long.",
        "server-1": "Password must contain at least one digit.",
      },
    })
    expect(form.setFocus).toHaveBeenCalledWith("password")
  })

  it("leaves a failure about anything else to the caller", () => {
    const form = { setError: vi.fn(), setFocus: vi.fn() }

    const handled = applyPasswordServerErrors<{ password: string }>(
      form,
      "password",
      {
        status: 409,
        title: "User.DuplicateEmail",
        detail: "This email is already registered.",
      }
    )

    expect(handled).toBe(false)
    expect(form.setError).not.toHaveBeenCalled()
    expect(form.setFocus).not.toHaveBeenCalled()
  })

  it("does not mistake a reset-token failure for a password rule", () => {
    const form = { setError: vi.fn(), setFocus: vi.fn() }

    const handled = applyPasswordServerErrors<{ newPassword: string }>(
      form,
      "newPassword",
      {
        status: 400,
        title: "PasswordReset.InvalidToken",
        detail: "This reset link is no longer valid.",
      }
    )

    expect(handled).toBe(false)
  })
})

function Harness({ policy }: { policy: PasswordPolicy | undefined }) {
  const form = useForm({
    resolver: zodResolver(z.object({ password: passwordSchema(policy) })),
    defaultValues: { password: "" },
  })
  return (
    <Form {...form}>
      <form>
        <PasswordField
          control={form.control}
          name="password"
          label="Password"
        />
      </form>
    </Form>
  )
}

describe("PasswordField", () => {
  beforeEach(() => usePasswordPolicyMock.mockReset())

  it("ticks the rules live as the value is typed", () => {
    usePasswordPolicyMock.mockReturnValue({ policy: STRICT, isPending: false })
    render(<Harness policy={STRICT} />)

    const input = screen.getByLabelText("Password")
    const met = () =>
      screen
        .getAllByRole("listitem")
        .map((item) => [item.dataset.rule, item.dataset.met])

    expect(met()).toEqual([
      ["minLength", "false"],
      ["uppercase", "false"],
      ["lowercase", "false"],
      ["digit", "false"],
      ["special", "false"],
    ])

    fireEvent.change(input, { target: { value: "Abc" } })
    expect(met()).toEqual([
      ["minLength", "false"],
      ["uppercase", "true"],
      ["lowercase", "true"],
      ["digit", "false"],
      ["special", "false"],
    ])

    fireEvent.change(input, { target: { value: "Abcdefg1!" } })
    expect(met().every(([, state]) => state === "true")).toBe(true)
  })

  it("draws no list while the policy is unknown, rather than one built on a guess", () => {
    usePasswordPolicyMock.mockReturnValue({
      policy: undefined,
      isPending: true,
    })
    render(<Harness policy={undefined} />)

    expect(screen.getByLabelText("Password")).toBeInTheDocument()
    expect(screen.queryByRole("list")).toBeNull()
  })
})
