import { describe, expect, it } from "vitest"

import { getErrorDescriptions } from "./errors"

describe("getErrorDescriptions", () => {
  it("returns every catalog sentence of a multi-rule refusal, in server order", () => {
    const refusal = {
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
        {
          code: "Password.CommonPattern",
          description:
            "Password contains a common pattern that is easy to guess.",
        },
      ],
    }

    expect(getErrorDescriptions(refusal)).toEqual([
      {
        code: "Password.TooShort",
        description: "Password must be at least 12 characters long.",
      },
      {
        code: "Password.RequiresDigit",
        description: "Password must contain at least one digit.",
      },
      {
        code: "Password.CommonPattern",
        description:
          "Password contains a common pattern that is easy to guess.",
      },
    ])
  })

  it("reads a single-rule refusal from title and detail", () => {
    expect(
      getErrorDescriptions({
        status: 400,
        title: "Password.RequiresUppercase",
        detail: "Password must contain at least one uppercase letter.",
      })
    ).toEqual([
      {
        code: "Password.RequiresUppercase",
        description: "Password must contain at least one uppercase letter.",
      },
    ])
  })

  it("keeps the trust boundary: no field names, no echoed codes, no opaque sentences", () => {
    expect(
      getErrorDescriptions({
        status: 400,
        title: "FirstName",
        detail: "raw backend validation text",
        errors: [
          { code: "FirstName", description: "raw backend validation text" },
          { code: "Password.TooShort", description: "Password.TooShort" },
          { code: "Password.RequiresDigit", description: "   " },
          {
            code: "Secret.ConnectionStringUnreachable",
            description: "Login failed for user 'sa' on host db-prod-01",
          },
        ],
      })
    ).toEqual([])

    expect(
      getErrorDescriptions({
        status: 500,
        title: "System.DatabaseUnavailableException",
        detail: "private host and stack trace",
      })
    ).toEqual([])
  })

  it("is empty for anything that is not a ProblemDetails", () => {
    expect(getErrorDescriptions(undefined)).toEqual([])
    expect(getErrorDescriptions(new TypeError("Failed to fetch"))).toEqual([])
    expect(getErrorDescriptions("nope")).toEqual([])
  })
})
