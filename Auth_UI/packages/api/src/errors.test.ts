import { afterEach, describe, expect, it } from "vitest"

import {
  getErrorCodes,
  getErrorDetail,
  getErrorFeedback,
  getErrorMessage,
  getFieldErrors,
  readProblem,
  type ApiErrorKind,
} from "./errors"
import { applyLanguage } from "@authsystem/i18n"
import { ar } from "@authsystem/i18n/locales/ar"
import { en } from "@authsystem/i18n/locales/en"
import { fa } from "@authsystem/i18n/locales/fa"
import { fr } from "@authsystem/i18n/locales/fr"
import { tr } from "@authsystem/i18n/locales/tr"
import { ur } from "@authsystem/i18n/locales/ur"
import { zh } from "@authsystem/i18n/locales/zh"

const PENDING_DELETION_403 = {
  code: "User.AccountPendingDeletion",
  status: 403,
  detail:
    "This account is deactivated and scheduled for deletion on 2026-09-14 23:29:34Z.",
}

const LOCALES = [
  { code: "en", resource: en },
  { code: "ar", resource: ar },
  { code: "fa", resource: fa },
  { code: "fr", resource: fr },
  { code: "tr", resource: tr },
  { code: "ur", resource: ur },
  { code: "zh", resource: zh },
] as const

afterEach(async () => {
  await applyLanguage("en")
})

describe("getErrorFeedback", () => {
  it.each<{
    error: unknown
    kind: ApiErrorKind
  }>([
    { error: { status: 400, detail: "raw validation" }, kind: "validation" },
    {
      error: { status: 401, detail: "raw unauthorized" },
      kind: "authentication",
    },
    { error: { status: 403, detail: "raw forbidden" }, kind: "authorization" },
    { error: { status: 409, detail: "raw conflict" }, kind: "conflict" },
    { error: { status: 429, detail: "raw throttle" }, kind: "rateLimit" },
    { error: { status: 500, detail: "raw stack trace" }, kind: "server" },
    { error: new TypeError("Failed to fetch internal host"), kind: "network" },
    { error: new Error("SQL connection string leaked"), kind: "unknown" },
  ])("maps $kind without exposing backend text", ({ error, kind }) => {
    const feedback = getErrorFeedback(error)

    expect(feedback).toMatchObject({
      kind,
      title: en.errors.feedback.title,
      description: en.errors.feedback[kind],
      actionLabel: en.errors.feedback.retry,
    })
    expect(getErrorMessage(error)).toBe(en.errors.feedback[kind])
    expect(feedback.description).not.toMatch(/raw|stack|internal|SQL/i)
  })

  it("uses known codes before a generic HTTP status", () => {
    expect(
      getErrorFeedback({ code: "User.DuplicateEmail", status: 409 }).kind
    ).toBe("duplicateEmail")
    expect(
      getErrorFeedback({ status: 409, code: "Notification.PublishTargetChanged" })
        .kind
    ).toBe("staleData")
    expect(getErrorFeedback(PENDING_DELETION_403).kind).toBe("pendingDeletion")
    // Classification stays local, but the sentence comes from the backend's
    // DomainErrors catalog: it is written per code in all seven languages and
    // often carries a fact this client cannot reconstruct.
    expect(
      getErrorFeedback({
        code: "Secret.InvalidChallengeCode",
        status: 400,
        detail:
          "The confirmation code is incorrect or is no longer valid. Request a new code and try again.",
      })
    ).toMatchObject({
      kind: "invalidChallengeCode",
      description:
        "The confirmation code is incorrect or is no longer valid. Request a new code and try again.",
    })
    expect(
      getErrorFeedback({
        code: "Secret.ConnectionStringUnreachable",
        status: 400,
        detail: "raw database credentials",
      })
    ).toMatchObject({
      kind: "connectionUnreachable",
      description: en.errors.feedback.connectionUnreachable,
    })
  })

  it("never reads a code out of the title", () => {
    // The framework's title is a reason phrase; a code in it was the old
    // contract's defect, not something to keep honouring (ADR 0001).
    const feedback = getErrorFeedback({ status: 409, title: "User.DuplicateEmail" })

    expect(feedback.kind).toBe("conflict")
    expect(feedback.codes).toEqual([])
  })

  it("offers direct Retry only for replay-safe transient classes", () => {
    expect(getErrorFeedback({ status: 503 }).retryable).toBe(true)
    expect(getErrorFeedback(new TypeError("offline")).retryable).toBe(true)
    expect(getErrorFeedback({ status: 429 }).retryable).toBe(false)
    expect(getErrorFeedback({ status: 409 }).retryable).toBe(false)
  })

  it.each(LOCALES)(
    "loads safe feedback from the $code locale",
    async ({ code, resource }) => {
      await applyLanguage(code)

      expect(getErrorFeedback(new TypeError("offline"))).toMatchObject({
        title: resource.errors.feedback.title,
        description: resource.errors.feedback.network,
        actionLabel: resource.errors.feedback.retry,
      })
    }
  )
})

describe("the backend catalog versus local copy", () => {
  // The catalog is written per code in all seven languages and carries facts
  // this client cannot reconstruct. Collapsing it into a per-status sentence
  // told a locked-out user they lacked permission, and dropped the deletion
  // deadline the recovery screen renders.
  it("shows the deadline the catalog carries, not a generic sentence", () => {
    const detail =
      "This account is deactivated and scheduled for deletion on 12 May 2026. It can be restored until then."
    expect(
      getErrorFeedback({
        status: 403,
        code: "User.AccountPendingDeletion",
        detail,
      }).description
    ).toBe(detail)
  })

  it("stops a lock-out reading as an authorization failure", () => {
    const detail = "This account is locked until 12 May 2026 09:00."
    const feedback = getErrorFeedback({
      status: 403,
      code: "User.AccountLockedUntil",
      detail,
    })
    expect(feedback.kind).toBe("authorization")
    expect(feedback.description).toBe(detail)
    expect(feedback.description).not.toBe(en.errors.feedback.authorization)
  })

  it("withholds a catalog sentence that carries the driver’s own words", () => {
    // The wrapper is localized; what it interpolates is a raw SqlException.
    const feedback = getErrorFeedback({
      status: 400,
      code: "Secret.ConnectionStringUnreachable",
      detail:
        "The connection string was not saved because no connection could be opened with it: A network-related or instance-specific error occurred while establishing a connection to SQL Server (server=db-prod-01; user id=sa).",
    })
    expect(feedback.description).toBe(en.errors.feedback.connectionUnreachable)
    expect(feedback.description).not.toContain("db-prod-01")
  })

  it("keeps local copy for a pipeline code, whose kind says what to do next", () => {
    expect(
      getErrorFeedback({
        status: 404,
        code: "Http.NotFound",
        detail: "The requested resource was not found.",
      }).description
    ).toBe(en.errors.feedback.notFound)
    expect(
      getErrorFeedback({
        status: 429,
        code: "Http.RateLimited",
        detail: "Too many requests. Please try again later.",
      }).description
    ).toBe(en.errors.feedback.rateLimit)
  })

  it("keeps local copy when the code is not one the API publishes", () => {
    expect(
      getErrorFeedback({
        status: 400,
        code: "PhoneNumber",
        detail: "'Phone Number' must be 20 characters or fewer.",
      }).description
    ).toBe(en.errors.feedback.validation)
    expect(
      getErrorFeedback({
        status: 500,
        code: "System.DatabaseUnavailableException",
        detail: "private host and stack trace",
      }).description
    ).toBe(en.errors.feedback.server)
  })
})

describe("getErrorCodes", () => {
  it("lists the problem's code first, then the other failures, once each", () => {
    expect(getErrorCodes(PENDING_DELETION_403)).toEqual([
      "User.AccountPendingDeletion",
    ])
    expect(
      getErrorCodes({
        status: 400,
        code: "Password.TooShort",
        errors: [
          { code: "Password.TooShort", pointer: "#/password" },
          { code: "Password.RequiresDigit", pointer: "#/password" },
        ],
      })
    ).toEqual(["Password.TooShort", "Password.RequiresDigit"])
  })

  it("leaves out a code the API does not publish", () => {
    expect(
      getErrorCodes({
        status: 400,
        code: "First",
        errors: [{ code: "First" }, { code: "User.FirstNameRequired" }],
      })
    ).toEqual(["User.FirstNameRequired"])
    expect(getErrorCodes({ title: "This account is deactivated" })).toEqual([])
  })
})

describe("getFieldErrors", () => {
  it("reads every field from the pointers of a multi-failure result", () => {
    expect(
      getFieldErrors({
        status: 400,
        code: "Email.Required",
        errors: [
          { code: "Email.Required", pointer: "#/email" },
          { code: "Notification.TranslationLanguageRequired", pointer: "#/translations/1/languageCode" },
          { code: "Paging.PageSizeOutOfRange" },
        ],
      })
    ).toEqual({
      email: en.errors.feedback.fieldInvalid,
      "translations.1.languageCode": en.errors.feedback.fieldInvalid,
    })
  })

  it("reads a single failure's field from its code's published pointer", () => {
    expect(
      getFieldErrors({ status: 400, code: "PhoneNumber.TooLong" })
    ).toEqual({ phoneNumber: en.errors.feedback.fieldInvalid })
  })

  it("places no field for a code that names none", () => {
    // Nothing on any form is called "User.DuplicateEmail"; highlighting a guess
    // would swallow the page-level message.
    expect(getFieldErrors({ status: 409, code: "User.DuplicateEmail" })).toEqual({})
    expect(getFieldErrors({ status: 400, code: "Http.BadRequest" })).toEqual({})
    expect(getFieldErrors({ status: 400, code: "Paging.PageSizeOutOfRange" })).toEqual({})
  })

  it("ignores what is not a pointer into the body", () => {
    expect(
      getFieldErrors({
        status: 400,
        code: "Email.Required",
        errors: [{ code: "Email.Required", pointer: "email" }, { code: "X", pointer: "#/" }],
      })
    ).toEqual({})
    expect(getFieldErrors({ title: "PhoneNumber" })).toEqual({})
    expect(getFieldErrors(new TypeError("offline"))).toEqual({})
  })
})

describe("getErrorDetail", () => {
  it("returns the catalog sentence with the code it describes", () => {
    expect(
      getErrorDetail({
        status: 400,
        code: "Password.TooShort",
        detail: " Password must be at least 12 characters long. ",
        errors: [{ code: "Password.TooShort" }, { code: "Password.RequiresDigit" }],
      })
    ).toEqual({
      code: "Password.TooShort",
      description: "Password must be at least 12 characters long.",
    })
  })

  it("keeps the trust boundary: no pipeline, opaque, unpublished or empty sentences", () => {
    expect(getErrorDetail({ status: 500, code: "Http.Unexpected", detail: "An unexpected error occurred." })).toBeUndefined()
    expect(
      getErrorDetail({
        status: 400,
        code: "Secret.ConnectionStringUnreachable",
        detail: "Login failed for user 'sa' on host db-prod-01",
      })
    ).toBeUndefined()
    expect(getErrorDetail({ status: 400, code: "FirstName", detail: "raw backend text" })).toBeUndefined()
    expect(getErrorDetail({ status: 400, code: "Password.RequiresDigit", detail: "   " })).toBeUndefined()
    expect(getErrorDetail(new TypeError("Failed to fetch"))).toBeUndefined()
    expect(getErrorDetail("nope")).toBeUndefined()
  })
})

describe("readProblem", () => {
  it("keeps the body and takes the status from the transport", async () => {
    const response = new Response(
      JSON.stringify({ status: 200, code: "User.NotFound", detail: "No such user." }),
      { status: 404, headers: { "Content-Type": "application/problem+json" } }
    )

    expect(await readProblem(response)).toEqual({
      status: 404,
      code: "User.NotFound",
      detail: "No such user.",
    })
  })

  it.each([
    ["an empty body", ""],
    ["an HTML page", "<html><body>Bad gateway</body></html>"],
    ["a JSON array", "[1, 2]"],
  ])("answers with the status alone for %s", async (_, body) => {
    expect(await readProblem(new Response(body, { status: 502 }))).toEqual({ status: 502 })
  })
})
