import { afterEach, describe, expect, it } from "vitest"

import {
  getErrorCodes,
  getErrorFeedback,
  getErrorMessage,
  getFieldErrors,
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
  title: "User.AccountPendingDeletion",
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
      getErrorFeedback({ title: "User.DuplicateEmail", status: 409 }).kind
    ).toBe("duplicateEmail")
    expect(
      getErrorFeedback({
        status: 409,
        errors: [{ code: "Notification.PublishTargetChanged" }],
      }).kind
    ).toBe("staleData")
    expect(getErrorFeedback(PENDING_DELETION_403).kind).toBe("pendingDeletion")
    // Classification stays local, but the sentence comes from the backend's
    // DomainErrors catalog: it is written per code in all seven languages and
    // often carries a fact this client cannot reconstruct.
    expect(
      getErrorFeedback({
        title: "Secret.InvalidChallengeCode",
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
        title: "Secret.ConnectionStringUnreachable",
        status: 400,
        detail: "raw database credentials",
      })
    ).toMatchObject({
      kind: "connectionUnreachable",
      description: en.errors.feedback.connectionUnreachable,
    })
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
        title: "User.AccountPendingDeletion",
        detail,
      }).description
    ).toBe(detail)
  })

  it("stops a lock-out reading as an authorization failure", () => {
    const detail = "This account is locked until 12 May 2026 09:00."
    const feedback = getErrorFeedback({
      status: 403,
      title: "User.AccountLockedUntil",
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
      title: "Secret.ConnectionStringUnreachable",
      detail:
        "The connection string was not saved because no connection could be opened with it: A network-related or instance-specific error occurred while establishing a connection to SQL Server (server=db-prod-01; user id=sa).",
    })
    expect(feedback.description).toBe(en.errors.feedback.connectionUnreachable)
    expect(feedback.description).not.toContain("db-prod-01")
  })

  it("keeps local copy when there is no domain code to trust", () => {
    expect(
      getErrorFeedback({
        status: 400,
        title: "PhoneNumber",
        detail: "'Phone Number' must be 20 characters or fewer.",
      }).description
    ).toBe(en.errors.feedback.validation)
  })
})

describe("getErrorCodes", () => {
  it("preserves single and multi-error ErrorOr codes for control flow", () => {
    expect(getErrorCodes(PENDING_DELETION_403)).toEqual([
      "User.AccountPendingDeletion",
    ])
    expect(
      getErrorCodes({ errors: [{ code: "First" }, { code: "Second" }] })
    ).toEqual(["First", "Second"])
  })

  it("does not mistake a human title for a code", () => {
    expect(getErrorCodes({ title: "This account is deactivated" })).toEqual([])
  })
})

describe("getFieldErrors", () => {
  it("maps validation keys to camelCase local feedback", () => {
    expect(
      getFieldErrors({
        errors: { Email: ["raw required"], Password: ["raw too short"] },
      })
    ).toEqual({
      email: en.errors.feedback.fieldInvalid,
      password: en.errors.feedback.fieldInvalid,
    })
  })

  // These payloads are what ApiController.Problem() actually emits for a
  // handler validation failure: FluentValidation names the offending property
  // as the ErrorOr code, so the field arrives in `title` alone when one rule
  // failed, and in `errors[].code` when several did.
  it("reads the field from a single-error ProblemDetails title", () => {
    expect(
      getFieldErrors({
        status: 400,
        title: "PhoneNumber",
        detail: "'Phone Number' must be 20 characters or fewer.",
      })
    ).toEqual({ phoneNumber: en.errors.feedback.fieldInvalid })
  })

  it("reads every field from a multi-error ProblemDetails array", () => {
    expect(
      getFieldErrors({
        status: 400,
        title: "Email",
        errors: [
          { code: "Email", description: "raw required" },
          { code: "Password", description: "raw too short" },
        ],
      })
    ).toEqual({
      email: en.errors.feedback.fieldInvalid,
      password: en.errors.feedback.fieldInvalid,
    })
  })

  it("does not mistake a domain rule for a field", () => {
    // Nothing on any form is called "User.DuplicateEmail"; treating it as a
    // field would highlight no control while swallowing the page-level message.
    expect(
      getFieldErrors({ status: 409, title: "User.DuplicateEmail" })
    ).toEqual({})
  })

  it("ignores empty entries and malformed dictionaries", () => {
    expect(
      getFieldErrors({ errors: { Email: [], Password: "wrong shape" } })
    ).toEqual({})
    expect(getFieldErrors({ title: "This account is deactivated" })).toEqual({})
    expect(getFieldErrors(new TypeError("offline"))).toEqual({})
  })
})
