import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import i18n from "@authsystem/i18n"

const get = vi.fn()
const put = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
    DELETE: vi.fn(),
  },
}))

/**
 * The confirmation flow reads the signed-in administrator's own address — it is
 * both where the code is sent and what has to be typed back on the last screen.
 * Standing up a real AuthProvider here would test the provider, not the page.
 */
vi.mock("@authsystem/auth/auth-context", () => ({
  useAuth: () => ({ user: { email: "admin@company.com" } }),
}))

import { SecretsPage } from "./secrets-page"

const CONNECTION_STRING = "Server=db;Database=AuthDb;User Id=app;Password=new"

/**
 * The real 400 body for this failure. ApiController.Problem puts the ErrorOr code
 * in `title` and only adds an `errors` array when there is more than one error —
 * and this handler returns exactly one, so `errors` is absent in production.
 * A fixture carrying the array would test a branch the console never takes here.
 */
function unreachableProblem() {
  return {
    status: 400,
    title: "Secret.ConnectionStringUnreachable",
    detail:
      "The connection string was not saved because no connection could be opened with it: " +
      "Login failed for user 'app'. If you are staging a password that is not active yet, " +
      "resubmit with confirmation to save it anyway.",
    instance: "/api/v1/admin/Secrets/connection-string",
  }
}

function renderPage() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <SecretsPage />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

/**
 * The status payload the console renders the badge list from. Carries all eight
 * first-class secrets because the row's action is chosen per key: a payload with
 * only three of them would leave five governance branches unexercised.
 */
function statusPayload() {
  return {
    data: {
      secretFileExists: true,
      secretFilePath: "C:/secrets/secrets.dpapi",
      machineName: "TESTHOST",
      schemaVersion: 1,
      secrets: {
        JwtPrivateKeyPem: "Configured",
        JwtPublicKeyPem: "Configured",
        RefreshTokenHmacKey: "Configured",
        GatewayToken: "Configured",
        AccountDeletionIdentifierHmacKey: "Configured",
        PasswordPepper: "Configured",
        SmtpPassword: "NotConfigured",
        "ConnectionStrings.AuthDb": "NotConfigured",
      },
    },
  }
}

/**
 * The whole row for a secret. The key name now sits in a two-line column beside
 * its description, so its parent is no longer the row — reach for the `li`.
 */
function rowFor(key: string) {
  return screen.getByText(key).closest("li")!
}

async function openDialogFor(rowLabel: string) {
  const user = userEvent.setup()
  renderPage()
  await screen.findByText(rowLabel)
  await user.click(
    within(rowFor(rowLabel)).getByRole("button", { name: "Edit" })
  )
  return user
}

/**
 * Every other test here mocks a successful status call, which is why the page
 * shipped reporting a 500 as "administration is disabled" — the opposite of the
 * truth, in front of a fault that names its own fix. These two pin the split.
 */
describe("SecretsPage — when the status call fails", () => {
  beforeEach(() => {
    put.mockReset().mockResolvedValue({})
  })

  it("reports a refusal as the setting it is", async () => {
    get.mockReset().mockResolvedValue({
      error: { status: 403, title: "Forbidden" },
    })

    renderPage()

    await screen.findByText("Secret administration is disabled")
    // Nothing to retry: a switched-off API answers the same way every time.
    expect(screen.queryByRole("button", { name: "Retry" })).not.toBeInTheDocument()
  })

  it("reports a fault as a fault, in the server's own words", async () => {
    // What an undecryptable secrets file actually returns: the handler catches
    // SecretDecryptionException and answers with a domain error naming the cause.
    get.mockReset().mockResolvedValue({
      error: {
        status: 500,
        title: "Secret.DecryptionFailed",
        detail:
          "Failed to decrypt the secret file. It may have been encrypted on a different machine or the DPAPI keys may have changed.",
      },
    })

    renderPage()

    await screen.findByText("Secret status could not be read")
    expect(
      screen.getByText(/may have been encrypted on a different machine/)
    ).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument()
    expect(
      screen.queryByText("Secret administration is disabled")
    ).not.toBeInTheDocument()
  })

  /**
   * The message on this screen is the server's, localized from Accept-Language
   * when the call was made. Cached under a language-blind key it outlives the
   * language that produced it — an English page explaining itself in Persian,
   * which is exactly what the browser showed before the key carried the
   * language.
   */
  it("refetches the status when the language changes", async () => {
    get.mockReset().mockResolvedValue({
      error: { status: 500, title: "Secret.DecryptionFailed", detail: "…" },
    })

    renderPage()
    await screen.findByText("Secret status could not be read")
    const callsInEnglish = get.mock.calls.length

    await act(async () => {
      await i18n.changeLanguage("tr")
    })

    await waitFor(() =>
      expect(get.mock.calls.length).toBeGreaterThan(callsInEnglish)
    )
    await i18n.changeLanguage("en")
  })
})

describe("SecretsPage — storing the credential secrets", () => {
  beforeEach(() => {
    get.mockReset().mockResolvedValue(statusPayload())
    put.mockReset().mockResolvedValue({})
  })

  /**
   * Each row gets exactly the action its governance allows, and the three
   * classes must not leak into one another. An Edit box beside a signing key
   * would be an import that silently bypasses the confirmation flow; a
   * confirmation flow in front of the SMTP password would deliver its code
   * through the very mail path being repaired.
   */
  it("gives every row exactly the action its governance allows", async () => {
    renderPage()

    await screen.findByText("ConnectionStrings.AuthDb")

    // An external party owns the value, so it can only be transcribed.
    for (const key of ["SmtpPassword", "ConnectionStrings.AuthDb"]) {
      expect(
        within(rowFor(key)).getByRole("button", { name: "Edit" })
      ).toBeInTheDocument()
    }

    // The system owns the value, so replacing it runs the confirmation flow.
    for (const key of [
      "JwtPrivateKeyPem",
      "RefreshTokenHmacKey",
      "GatewayToken",
    ]) {
      expect(
        within(rowFor(key)).getByRole("button", { name: "Replace" })
      ).toBeInTheDocument()
    }

    // Derived, permanent, or not a single value: nothing to offer.
    for (const key of [
      "JwtPublicKeyPem",
      "AccountDeletionIdentifierHmacKey",
      "PasswordPepper",
    ]) {
      expect(within(rowFor(key)).queryByRole("button")).toBeNull()
    }
  })

  /**
   * A row with no button and no sentence reads as an unfinished feature rather
   * than a deliberate refusal — which is exactly how the page was misread. The
   * three action-less rows are the ones that must carry their reason.
   */
  it("states why the action-less rows offer nothing", async () => {
    renderPage()

    await screen.findByText("ConnectionStrings.AuthDb")

    expect(rowFor("JwtPublicKeyPem")).toHaveTextContent(
      /Derived from the private key/
    )
    expect(rowFor("AccountDeletionIdentifierHmacKey")).toHaveTextContent(
      /Permanent/
    )
    expect(rowFor("PasswordPepper")).toHaveTextContent(/not one value/)
  })

  /**
   * Rows differ in whether they carry an action, and in how many. If the badge
   * came first it would sit one or two button-widths inboard on some rows and
   * the column the eye scans down would break. Asserting the badge is the LAST
   * child keeps every badge on the same edge, in both text directions.
   */
  it("puts the badge last in the row so every badge stays aligned", async () => {
    renderPage()

    await screen.findByText("ConnectionStrings.AuthDb")

    for (const key of [
      "JwtPrivateKeyPem",
      "JwtPublicKeyPem",
      "SmtpPassword",
      "ConnectionStrings.AuthDb",
    ]) {
      const trailing = rowFor(key).lastElementChild!
      expect(trailing.lastElementChild).toHaveAttribute("data-slot", "badge")
    }
  })

  /**
   * The import path has no "which secret" parameter — each endpoint writes to a
   * fixed name — so the shape is decided by the row, not by the operator. The
   * dialog used to ask, which is what made importing look like a general editor
   * for any secret. The field names the encoding the server will check against.
   */
  it("locks the import dialog to the shape of the row it was opened from", async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText("RefreshTokenHmacKey")
    await user.click(
      within(rowFor("RefreshTokenHmacKey")).getByRole("button", {
        name: "Replace",
      })
    )
    await user.click(
      await screen.findByRole("menuitem", { name: "Import HMAC key" })
    )

    expect(await screen.findByLabelText("HMAC key (base64)")).toBeInTheDocument()
    expect(screen.queryByRole("combobox")).toBeNull()
  })

  /**
   * Generating is the other half of the same governance: it must reach the
   * confirmation flow, not the storage endpoint. Nothing is written until the
   * emailed code and the impact screen have both been answered.
   */
  it("routes a row's generate through the confirmation flow, not a write", async () => {
    const user = userEvent.setup()
    renderPage()

    await screen.findByText("GatewayToken")
    await user.click(
      within(rowFor("GatewayToken")).getByRole("button", { name: "Replace" })
    )
    await user.click(
      await screen.findByRole("menuitem", { name: "Generate gateway token" })
    )

    await screen.findByText("This changes a key the whole platform runs on")
    expect(put).not.toHaveBeenCalled()
  })

  it("posts the SMTP password to its own endpoint, masked while typing", async () => {
    const user = await openDialogFor("SmtpPassword")

    const input = await screen.findByLabelText("Value")
    expect(input).toHaveAttribute("type", "password")

    await user.type(input, "smtp-secret")
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() =>
      expect(put).toHaveBeenCalledWith("/api/v1/admin/Secrets/smtp-password", {
        body: { value: "smtp-secret" },
      })
    )
  })

  /**
   * The password-rotation path. The first save is refused because the new
   * credential is not live at the database yet; the operator must be told, and
   * then be able to store it anyway — otherwise rotating a database password has
   * no valid order at all.
   */
  it("re-sends the connection string with forceSave after an unreachable warning", async () => {
    put.mockResolvedValueOnce({ error: unreachableProblem() })

    const user = await openDialogFor("ConnectionStrings.AuthDb")
    await user.type(await screen.findByLabelText("Value"), CONNECTION_STRING)
    await user.click(screen.getByRole("button", { name: "Save" }))

    // The warning names the failure and the button changes what it promises.
    await screen.findByText(/Could not connect with this connection string/)
    expect(screen.getByText(/Login failed for user/)).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Save anyway" }))

    await waitFor(() =>
      expect(put).toHaveBeenLastCalledWith(
        "/api/v1/admin/Secrets/connection-string",
        { body: { value: CONNECTION_STRING, forceSave: true } }
      )
    )
  })

  /**
   * ApiController.Problem only emits the `errors` array when there is more than
   * one error, and this handler always returns exactly one — so the array form
   * is the shape the console will NOT see here. Both are covered because
   * getErrorCodes recognises the code through two different branches, and the
   * one production actually uses is the `title` fallback.
   */
  it("recognises the unreachable code from a multi-error body too", async () => {
    put.mockResolvedValueOnce({
      error: {
        status: 400,
        title: "Secret.ConnectionStringUnreachable",
        errors: [
          {
            code: "Secret.ConnectionStringUnreachable",
            description: "Login failed for user 'app'.",
          },
          { code: "Secret.Other", description: "second error" },
        ],
      },
    })

    const user = await openDialogFor("ConnectionStrings.AuthDb")
    await user.type(await screen.findByLabelText("Value"), CONNECTION_STRING)
    await user.click(screen.getByRole("button", { name: "Save" }))

    await screen.findByRole("button", { name: "Save anyway" })
  })

  /**
   * The warning described the value that failed. Editing produces a different
   * value, so the confirmation it armed must not carry over to it.
   */
  it("retracts the force-save confirmation when the value is edited", async () => {
    put.mockResolvedValueOnce({ error: unreachableProblem() })

    const user = await openDialogFor("ConnectionStrings.AuthDb")
    const input = await screen.findByLabelText("Value")
    await user.type(input, CONNECTION_STRING)
    await user.click(screen.getByRole("button", { name: "Save" }))
    await screen.findByRole("button", { name: "Save anyway" })

    await user.type(input, "X")

    expect(
      screen.queryByRole("button", { name: "Save anyway" })
    ).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() =>
      expect(put).toHaveBeenLastCalledWith(
        "/api/v1/admin/Secrets/connection-string",
        { body: { value: `${CONNECTION_STRING}X`, forceSave: false } }
      )
    )
  })

  /**
   * The field stays editable while the probe runs, so the failure can arrive
   * describing a string the box no longer holds. Arming "Save anyway" then would
   * store an unprobed value under a confirmation the operator was never shown —
   * force-save skips the server-side reachability check entirely.
   */
  it("does not arm force-save when the value changed while the probe was in flight", async () => {
    let settle: (result: unknown) => void = () => {}
    put.mockReturnValueOnce(
      new Promise((resolve) => {
        settle = resolve
      })
    )

    const user = await openDialogFor("ConnectionStrings.AuthDb")
    const input = await screen.findByLabelText("Value")
    await user.type(input, CONNECTION_STRING)
    await user.click(screen.getByRole("button", { name: "Save" }))

    // Operator spots a typo and fixes it while the probe is still running.
    await user.type(input, "EDITED")
    settle({ error: unreachableProblem() })

    // The stale failure must neither arm the button nor show its warning.
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Save" })).toBeEnabled()
    )
    expect(
      screen.queryByRole("button", { name: "Save anyway" })
    ).not.toBeInTheDocument()
    expect(
      screen.queryByText(/Could not connect with this connection string/)
    ).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Save" }))

    await waitFor(() =>
      expect(put).toHaveBeenLastCalledWith(
        "/api/v1/admin/Secrets/connection-string",
        { body: { value: `${CONNECTION_STRING}EDITED`, forceSave: false } }
      )
    )
  })
})
