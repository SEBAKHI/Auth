import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { act, render, screen } from "@testing-library/react"
import * as React from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import "@authsystem/i18n"

const { post, get } = vi.hoisted(() => ({ post: vi.fn(), get: vi.fn() }))

vi.mock("@authsystem/api/client", () => ({
  SESSION_EXPIRED_EVENT: "auth:session-expired",
  api: {
    POST: (...args: unknown[]) => post(...args),
    GET: (...args: unknown[]) => get(...args),
  },
}))

import {
  clearTokens,
  getAccessToken,
  getRefreshToken,
} from "@authsystem/api/token-store"

import { AuthProvider, useAuth, type LoginResult } from "./auth-context"

const USER = {
  id: "11111111-1111-1111-1111-111111111111",
  email: "jane@one.example",
  firstName: "Jane",
  lastName: "Doe",
  preferredLanguage: "en",
  timeZone: "UTC",
  roles: [],
  permissions: [],
}

function token() {
  const enc = (v: object) =>
    Buffer.from(JSON.stringify(v)).toString("base64url")
  return `${enc({ alg: "none" })}.${enc({
    exp: Math.floor(Date.now() / 1000) + 3600,
  })}.sig`
}

let result: LoginResult | null = null

function Probe() {
  const { status, completeRegistration } = useAuth()
  return (
    <>
      <span data-testid="status">{status}</span>
      <button
        onClick={() => {
          void completeRegistration({
            pendingId: "handle-1",
            otp: "123456",
            password: "NewPass1!",
            firstName: "Jane",
            lastName: "Doe",
            timeZone: "Europe/Istanbul",
          }).then((value) => {
            result = value
          })
        }}
      >
        complete
      </button>
    </>
  )
}

/**
 * The page test mocks the context, so the request body is built one layer
 * below anything it can see. This exercises the real provider: what goes on
 * the wire, and that the answer is adopted exactly as a sign-in is.
 */
describe("AuthProvider.completeRegistration", () => {
  beforeEach(() => {
    post.mockReset()
    get.mockReset()
    // The provider re-reads the profile on bootstrap when a refresh token is
    // present; a previous case leaves one behind, so answer rather than throw.
    get.mockResolvedValue({ data: USER })
    result = null
    clearTokens()
    window.localStorage.clear()
  })

  it("posts the handle, the code, the password, the name and the zone - never an address - and adopts the session", async () => {
    post.mockResolvedValue({
      data: {
        token: { accessToken: token(), refreshToken: "R1" },
        user: USER,
        requiresPasswordChange: false,
      },
    })
    render(
      <QueryClientProvider client={new QueryClient()}>
        <AuthProvider>
          <Probe />
        </AuthProvider>
      </QueryClientProvider>
    )
    expect(screen.getByTestId("status")).toHaveTextContent("unauthenticated")

    await act(async () => {
      screen.getByRole("button", { name: "complete" }).click()
      await Promise.resolve()
    })

    expect(post).toHaveBeenCalledTimes(1)
    const [path, options] = post.mock.calls[0] as [string, { body: object }]
    expect(path).toBe("/api/v1/Auth/registration/complete")
    expect(Object.keys(options.body).sort()).toEqual(
      ["firstName", "lastName", "otp", "password", "pendingId", "timeZone"].sort()
    )
    expect(options.body).toEqual({
      pendingId: "handle-1",
      otp: "123456",
      password: "NewPass1!",
      firstName: "Jane",
      lastName: "Doe",
      timeZone: "Europe/Istanbul",
    })

    expect(result).toEqual({ status: "authenticated", requiresPasswordChange: false })
    expect(screen.getByTestId("status")).toHaveTextContent("authenticated")
    expect(getRefreshToken()).toBe("R1")
    expect(getAccessToken()).toBeTruthy()
  })

  it("sends null for an unknown zone and throws the server's refusal untouched", async () => {
    const refusal = { status: 409, title: "User.DuplicateEmail" }
    post.mockResolvedValue({ error: refusal })
    let thrown: unknown = null
    function Refused() {
      const { completeRegistration } = useAuth()
      React.useEffect(() => {
        completeRegistration({
          pendingId: "handle-1",
          otp: "123456",
          password: "NewPass1!",
          firstName: "Jane",
          lastName: "Doe",
        }).catch((error: unknown) => {
          thrown = error
        })
      }, [completeRegistration])
      return null
    }
    render(
      <QueryClientProvider client={new QueryClient()}>
        <AuthProvider>
          <Refused />
        </AuthProvider>
      </QueryClientProvider>
    )

    await act(async () => {
      await Promise.resolve()
    })

    expect(thrown).toBe(refusal)
    const [, options] = post.mock.calls[0] as [string, { body: { timeZone: unknown } }]
    expect(options.body.timeZone).toBeNull()
    expect(getRefreshToken()).toBeNull()
  })
})
