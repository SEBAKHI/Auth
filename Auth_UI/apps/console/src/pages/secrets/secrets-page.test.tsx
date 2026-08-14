import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const get = vi.fn()
const put = vi.fn()

vi.mock("@authsystem/api/client", () => ({
  api: {
    GET: (...args: unknown[]) => get(...args),
    PUT: (...args: unknown[]) => put(...args),
    DELETE: vi.fn(),
  },
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

/** The status payload the console renders the badge list from. */
function statusPayload() {
  return {
    data: {
      secretFileExists: true,
      secretFilePath: "C:/secrets/secrets.dpapi",
      machineName: "TESTHOST",
      schemaVersion: 1,
      secrets: {
        JwtPrivateKeyPem: "Configured",
        SmtpPassword: "NotConfigured",
        "ConnectionStrings.AuthDb": "NotConfigured",
      },
    },
  }
}

async function openDialogFor(rowLabel: string) {
  const user = userEvent.setup()
  renderPage()
  const row = await screen.findByText(rowLabel)
  const editButton = row.parentElement!.querySelector("button")!
  await user.click(editButton)
  return user
}

describe("SecretsPage — storing the credential secrets", () => {
  beforeEach(() => {
    get.mockReset().mockResolvedValue(statusPayload())
    put.mockReset().mockResolvedValue({})
  })

  /**
   * Only these two rows are settable. The signing keys are rotated through the
   * confirmation flow, and a stray Edit button beside them would offer a path
   * that silently bypasses it.
   */
  it("offers an edit control on the two credential rows only", async () => {
    renderPage()

    await screen.findByText("ConnectionStrings.AuthDb")

    for (const key of ["SmtpPassword", "ConnectionStrings.AuthDb"]) {
      const row = screen.getByText(key).parentElement!
      expect(row.querySelector("button")).not.toBeNull()
    }
    expect(
      screen.getByText("JwtPrivateKeyPem").parentElement!.querySelector("button")
    ).toBeNull()
  })

  /**
   * Only two of the rows carry an action. If the badge came first, those two
   * badges would sit one button-width inboard of all the others and the column
   * the eye scans down would break. Asserting the badge is the LAST child keeps
   * every badge on the same edge, in both text directions.
   */
  it("puts the badge last in the row so every badge stays aligned", async () => {
    renderPage()

    await screen.findByText("ConnectionStrings.AuthDb")

    for (const key of ["JwtPrivateKeyPem", "SmtpPassword", "ConnectionStrings.AuthDb"]) {
      const trailing = screen.getByText(key).parentElement!.lastElementChild!
      expect(trailing.lastElementChild).toHaveAttribute("data-slot", "badge")
    }
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
