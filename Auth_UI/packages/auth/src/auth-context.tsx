/* eslint-disable react-refresh/only-export-components */
import { useQueryClient } from "@tanstack/react-query"
import * as React from "react"

import {
  api,
  completePendingLogout,
  endCookieSession,
  SESSION_EXPIRED_EVENT,
} from "@authsystem/api/client"
import { claimToArray, decodeJwt } from "@authsystem/api/jwt"
import {
  MFA_REQUIRED_EVENT,
  readMfaRequirement,
  type MfaRequirement,
} from "@authsystem/api/mfa-requirement"
import { resetUserScopedCache } from "@authsystem/api/query"
import {
  REFRESH_SENTINEL,
  clearCookieCheck,
  clearLogoutPending,
  clearTokens,
  getAccessToken,
  hasSession,
  markCookieUnconfirmed,
  markLogoutPending,
  setTokens,
} from "@authsystem/api/token-store"
import i18n, {
  applyLanguage,
  persistLanguage,
  SUPPORTED_LANGUAGES,
  type LanguageCode,
} from "@authsystem/i18n"
import { readFlowHints } from "@authsystem/i18n/flow-hints"
import { setActiveTimeZone } from "@authsystem/i18n/timezone"
import { setDataTableScope } from "@authsystem/ui/data-table/storage"
import type { UserInfo } from "@authsystem/api/types"

import {
  clearPendingTwoFactorChallenge,
  getPendingStaySignedIn,
  setPendingTwoFactorChallenge,
  type TwoFactorMethod,
} from "./pending-challenge"
import { toEmailCodeNextStep, type EmailCodeNextStep } from "./email-code"
import { permissionMatches } from "./permission-matching"
import { markRecoveryCodeSignIn } from "./recovery-code-notice"
import { challengeEnded } from "./sign-in-method"

type AuthStatus = "loading" | "authenticated" | "unauthenticated"

/**
 * Provider-specific sign-in extras: Apple sends the one-time authorization
 * code (exchanged server-side for the revocable refresh token) and, on the
 * FIRST authorization only, the user's name.
 */
export interface ExternalLoginExtras {
  authorizationCode?: string
  givenName?: string
  familyName?: string
  /** The sign-in page's "Stay signed in" box (OI-154); absent is false. */
  staySignedIn?: boolean
}

/**
 * The last step of a verify-first sign-up. The address is not a member: the
 * server knows it from the handle, and a body that named one would be a body
 * a caller could name differently.
 */
export interface CompleteRegistrationInput {
  pendingId: string
  otp: string
  /**
   * Absent when the deployment signs new accounts up without one (OI-215): the
   * account then signs in with a code emailed to it.
   */
  password?: string
  /** Absent when the deployment asks for no name (OI-215). */
  firstName?: string
  lastName?: string
  timeZone?: string
}

/** The code of a sign-in by emailed code, presented to finish it (OI-215). */
export interface EmailCodeSignInInput {
  pendingId: string
  otp: string
  /**
   * Add-only: an account with a password proves it too, as the "sign in again"
   * dialog asks before a security change.
   */
  requireFullProof: boolean
  staySignedIn: boolean
}

/** The code of a sign-in by emailed code, presented to recover an account waiting for deletion. */
export interface EmailCodeRecoveryInput {
  pendingId: string
  otp: string
  /** The authenticator app's code, or a recovery code, once the server asked for it. */
  twoFactorCode?: string
  requireFullProof: boolean
}

/**
 * A sign-in by emailed code ends like any other — a session or a challenge — or,
 * with no session issued, names what comes next: the password, the recovery of
 * an account waiting for deletion, or a new account (OI-215).
 */
export type EmailCodeSignInResult =
  | LoginResult
  | { status: "nextStep"; nextStep: EmailCodeNextStep }

const KNOWN_TWO_FACTOR_METHODS: readonly TwoFactorMethod[] = [
  "totp",
  "recovery",
  "email",
]

/**
 * The server's list as this build knows it: names it does not know are dropped,
 * and a list left empty — or none at all — reads as "not said", the screen every
 * account always saw.
 */
export function toTwoFactorMethods(
  names: readonly string[] | null | undefined
): readonly TwoFactorMethod[] | null {
  const known = (names ?? []).filter((name): name is TwoFactorMethod =>
    KNOWN_TWO_FACTOR_METHODS.includes(name as TwoFactorMethod)
  )
  return known.length > 0 ? known : null
}

export type LoginResult =
  | { status: "authenticated"; requiresPasswordChange: boolean }
  | {
      status: "twoFactorRequired"
      challengeToken: string
      /**
       * How the second step may be completed (S24); null from a server that
       * predates the list, which means the screen every account always saw.
       */
      methods: readonly TwoFactorMethod[] | null
    }

interface AuthContextValue {
  status: AuthStatus
  user: UserInfo | null
  roles: string[]
  permissions: string[]
  /**
   * What this session must still prove before its token carries the account's
   * platform authority; "none" for everyone else (S08). The server enforces it;
   * this decides which page to show.
   */
  mfaRequirement: MfaRequirement
  hasPermission: (permission: string | undefined) => boolean
  hasAnyPermission: (permissions: string[]) => boolean
  /**
   * Signs in with the password. `staySignedIn` is the sign-in page's "Stay
   * signed in" box (OI-154): absent or false, the sign-in ends with the browser.
   */
  login: (
    email: string,
    password: string,
    staySignedIn?: boolean
  ) => Promise<LoginResult>
  loginExternal: (
    provider: string,
    idToken: string,
    nonce?: string,
    extras?: ExternalLoginExtras
  ) => Promise<LoginResult>
  recoverAccount: (
    email: string,
    password: string,
    twoFactorCode?: string
  ) => Promise<LoginResult>
  recoverAccountExternal: (
    provider: string,
    idToken: string,
    nonce?: string,
    twoFactorCode?: string
  ) => Promise<LoginResult>
  /**
   * Completes a challenged sign-in with a code: from the authenticator app
   * ("totp"), a recovery code ("recovery"), or the emailed code ("email", S24).
   */
  completeTwoFactor: (
    challengeToken: string,
    code: string,
    method: TwoFactorMethod
  ) => Promise<{ requiresPasswordChange: boolean }>
  completeEmailVerification: (email: string, otp: string) => Promise<LoginResult>
  completeRegistration: (input: CompleteRegistrationInput) => Promise<LoginResult>
  /** Spends a matched emailed code to sign in (OI-215). */
  signInWithEmailCode: (input: EmailCodeSignInInput) => Promise<EmailCodeSignInResult>
  /** Spends a matched emailed code to recover an account waiting for deletion. */
  recoverWithEmailCode: (input: EmailCodeRecoveryInput) => Promise<EmailCodeSignInResult>
  logout: () => Promise<void>
  refreshUser: () => Promise<void>
  /**
   * Reads the account again without ever ending the session: true when the
   * read succeeded and the account was updated; false when it failed (a 5xx, a
   * dropped connection), and then nothing changed. A session that is really over
   * still ends through the client's 401 path. For a page that must say "try
   * again" rather than sign the user out over a transient error.
   */
  rereadUser: () => Promise<boolean>
}

const AuthContext = React.createContext<AuthContextValue | undefined>(undefined)

/**
 * Permissions/roles come from the access-token claims, falling back to /me. The
 * two-factor requirement the other way round, like the profile: /me (or the
 * sign-in's user info) first, then the token's mfa_req.
 */
function derive(user: UserInfo | null): {
  roles: string[]
  permissions: string[]
  mfaRequirement: MfaRequirement
} {
  const token = getAccessToken()
  const claims = token ? decodeJwt(token) : null

  const permissions =
    user?.permissions && user.permissions.length > 0
      ? user.permissions
      : claimToArray(claims?.permissions)
  const roles =
    user?.roles && user.roles.length > 0
      ? user.roles
      : claimToArray(claims?.roles)
  const mfaRequirement = readMfaRequirement(user?.mfaRequirement ?? claims?.mfa_req)

  return { roles, permissions, mfaRequirement }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  // The session key says "there is a session to resume", whether it holds the
  // cookie sentinel or a legacy token (token-store.ts).
  const [status, setStatus] = React.useState<AuthStatus>(() =>
    hasSession() ? "loading" : "unauthenticated"
  )
  const [user, setUser] = React.useState<UserInfo | null>(null)
  // Read from context rather than importing the singleton: the provider is
  // mounted inside QueryClientProvider in both apps, and a test that renders
  // AuthProvider with its own client must be able to observe the reset.
  const queryClient = useQueryClient()

  const { roles, permissions, mfaRequirement } = React.useMemo(() => derive(user), [user])

  // Adopt the profile's preferred language once per session, so a login on a
  // fresh browser follows the profile without fighting a mid-session toggle.
  const languageAdoptedRef = React.useRef(false)
  const applyProfilePreferences = React.useCallback(
    (profile: UserInfo | null | undefined) => {
      setActiveTimeZone(profile?.timeZone)
      // Binds stored table layouts to this account and pulls the server copy.
      // Without it, two accounts on one browser share one set of layouts.
      setDataTableScope(profile?.id ?? null)

      if (languageAdoptedRef.current) return
      languageAdoptedRef.current = true
      // An application's language is in force for this flow: its pages stay in
      // it, and nothing is stored over the user's own choice.
      if (readFlowHints().language) return
      const code = profile?.preferredLanguage
      const supported = SUPPORTED_LANGUAGES.some((l) => l.code === code)
      if (code && supported && i18n.language !== code) {
        persistLanguage(code as LanguageCode)
        void applyLanguage(code as LanguageCode)
      }
    },
    []
  )

  const loadCurrentUser = React.useCallback(async () => {
    const { data, error } = await api.GET("/api/v1/Auth/me")
    if (error || !data) {
      clearTokens()
      setUser(null)
      setStatus("unauthenticated")
      // The session is gone but the tab is not: whatever the previous account
      // left in the cache would otherwise be waiting for whoever signs in next.
      void resetUserScopedCache(queryClient)
      return
    }
    setUser(data)
    setStatus("authenticated")
    applyProfilePreferences(data)
  }, [applyProfilePreferences, queryClient])

  // Bootstrap an existing session on first load (silent refresh via middleware).
  // A sign-out that never reached the server left the refresh cookie valid, and
  // may even have left the session key (a tab closed mid sign-out). It is
  // finished FIRST (client.ts, completePendingLogout): nothing signed in is
  // shown before it settles, and a session it ended is not resumed.
  React.useEffect(() => {
    let active = true
    // Cross an async boundary before the request so bootstrap cannot create a
    // cascading render from the effect that installed it.
    const timer = window.setTimeout(() => {
      void (async () => {
        await completePendingLogout()
        if (!active) return
        if (!hasSession()) {
          setStatus("unauthenticated")
          return
        }
        await loadCurrentUser()
      })()
    }, 0)
    return () => {
      active = false
      window.clearTimeout(timer)
    }
  }, [loadCurrentUser])

  // React to a non-recoverable session loss raised by the API client.
  React.useEffect(() => {
    const handler = () => {
      setUser(null)
      setStatus("unauthenticated")
      setActiveTimeZone(null)
      setDataTableScope(null)
      // The most dangerous of the four resets. An expiry ends the session
      // without anyone choosing to leave, so the app is mid-screen with its
      // queries populated, and the next sign-in on this tab is the likeliest of
      // all to be a different person. Note that scoping a key by user id could
      // not have covered this case: there is no id to scope by here.
      void resetUserScopedCache(queryClient)
    }
    window.addEventListener(SESSION_EXPIRED_EVENT, handler)
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, handler)
  }, [queryClient])

  // A background refresh withheld the platform authority while a page was open
  // (S08): read the account again, so RequireMfaSatisfied sees the requirement
  // and leaves the page that is failing. Only while this tab still believes
  // nothing is owed — once it knows, more refusals have nothing to add.
  //
  // Not through loadCurrentUser, which ends the session on any failed read: a
  // 5xx or a dropped connection here must not sign an administrator out. A
  // failed read changes nothing, and the next refused request raises the event
  // again. A session that is really over still ends through the client's 401
  // path, as everywhere else.
  const rereadUser = React.useCallback(async (): Promise<boolean> => {
    try {
      const { data, error } = await api.GET("/api/v1/Auth/me")
      if (error || !data) return false
      setUser(data)
      return true
    } catch {
      // A transport failure: as a failed read, nothing changes.
      return false
    }
  }, [])

  const mfaRequirementRef = React.useRef(mfaRequirement)
  const rereadingRef = React.useRef(false)
  React.useEffect(() => {
    mfaRequirementRef.current = mfaRequirement
  }, [mfaRequirement])
  React.useEffect(() => {
    const handler = () => {
      // A page's parallel queries all fail at once: one read answers them all.
      if (mfaRequirementRef.current !== "none" || rereadingRef.current) return
      rereadingRef.current = true
      void rereadUser().finally(() => {
        rereadingRef.current = false
      })
    }
    window.addEventListener(MFA_REQUIRED_EVENT, handler)
    return () => window.removeEventListener(MFA_REQUIRED_EVENT, handler)
  }, [rereadUser])

  // Shared tail of every login variant: either a 2FA challenge (no tokens
  // yet, the verify step completes the session) or a full token response.
  // `staySignedIn` rides with a challenge to the second factor, in memory only;
  // only the password, provider and emailed-code sign-ins have one to give
  // (OI-154, OI-215).
  const adoptLoginResponse = React.useCallback(
    (
      data: {
        token?: { accessToken: string; refreshToken: string } | null
        user?: UserInfo | null
        requiresPasswordChange?: boolean
        twoFactorChallengeToken?: string | null
        twoFactorMethods?: string[] | null
        twoFactorEmailCode?: {
          expiresAt?: string | null
          maskedEmail?: string | null
          sendsRemaining?: number | string | null
        } | null
      },
      staySignedIn = false
    ): LoginResult => {
      if (data.twoFactorChallengeToken) {
        // The methods ride with the token they were offered for, in memory and
        // in the navigation state, and are dropped with it. So does the code the
        // server emailed with the challenge (D-243), in memory only.
        const methods = toTwoFactorMethods(data.twoFactorMethods)
        const sent = data.twoFactorEmailCode
        setPendingTwoFactorChallenge(
          data.twoFactorChallengeToken,
          staySignedIn,
          methods,
          sent && methods?.includes("email")
            ? {
                expiresAt: sent.expiresAt ?? null,
                maskedEmail: sent.maskedEmail ?? "",
                sendsRemaining: Number(sent.sendsRemaining ?? 0),
                receivedAt: Date.now(),
              }
            : null
        )
        return {
          status: "twoFactorRequired",
          challengeToken: data.twoFactorChallengeToken,
          methods,
        }
      }
      if (!data.token || !data.user) {
        throw new Error("Login failed")
      }

      // The challenge is settled, so its mirrored destination has served its
      // purpose too. Leaving it set would let a later sign-in in the same tab
      // pick up a stale pending request and resume someone else's flow.
      clearPendingTwoFactorChallenge()
      // Before the tokens, not after: from this line on every query in the
      // cache would be read as belonging to the incoming account. The other
      // three resets should have left nothing behind, which is exactly why this
      // one is worth keeping — it is the only reset that runs on the path where
      // a mistake becomes someone else's data.
      void resetUserScopedCache(queryClient)
      // The one place a sign-in stores its tokens. The refresh value is stored
      // as the API delivered it: the cookie sentinel, or a real token while the
      // cookie delivery is off. A cookie sign-in is not proven until its first
      // refresh finds the cookie (client.ts, performRefresh).
      setTokens(data.token.accessToken, data.token.refreshToken)
      if (data.token.refreshToken === REFRESH_SENTINEL) markCookieUnconfirmed()
      else clearCookieCheck()
      setUser(data.user)
      setStatus("authenticated")
      applyProfilePreferences(data.user)

      return {
        status: "authenticated",
        requiresPasswordChange: data.requiresPasswordChange ?? false,
      }
    },
    [applyProfilePreferences, queryClient]
  )

  const login = React.useCallback(
    async (
      email: string,
      password: string,
      staySignedIn = false
    ): Promise<LoginResult> => {
      // The browser identifier is no longer passed here. It rides on every
      // request as a header set by the API client, so the three flows that used
      // to remember it and the two that forgot are now identical.
      const { data, error } = await api.POST("/api/v1/Auth/login", {
        body: { email, password, staySignedIn },
      })
      if (error || !data) {
        throw error ?? new Error("Login failed")
      }

      return adoptLoginResponse(data, staySignedIn)
    },
    [adoptLoginResponse]
  )

  const loginExternal = React.useCallback(
    async (
      provider: string,
      idToken: string,
      nonce?: string,
      extras?: ExternalLoginExtras
    ): Promise<LoginResult> => {
      const staySignedIn = extras?.staySignedIn ?? false
      const { data, error } = await api.POST("/api/v1/Auth/external-login", {
        body: {
          provider,
          idToken,
          nonce,
          authorizationCode: extras?.authorizationCode,
          givenName: extras?.givenName,
          familyName: extras?.familyName,
          staySignedIn,
        },
      })
      if (error || !data) {
        throw error ?? new Error("External login failed")
      }

      return adoptLoginResponse(data, staySignedIn)
    },
    [adoptLoginResponse]
  )

  // Grace-period recovery: cancels a pending account deletion, restores the
  // account and signs the user in — the response is a full login body, so it
  // rides the same shared tail.
  const recoverAccount = React.useCallback(
    async (
      email: string,
      password: string,
      twoFactorCode?: string
    ): Promise<LoginResult> => {
      const { data, error } = await api.POST("/api/v1/Auth/deletion/recover", {
        body: { email, password, twoFactorCode },
      })
      if (error || !data) {
        throw error ?? new Error("Account recovery failed")
      }
      // The 202 that says where an emailed code went (S24) answers only a request
      // that asked for one, which this never does: without a sign-in it failed.
      if ("maskedEmail" in data) {
        throw new Error("Account recovery answered with a code sent")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  const recoverAccountExternal = React.useCallback(
    async (
      provider: string,
      idToken: string,
      nonce?: string,
      twoFactorCode?: string
    ): Promise<LoginResult> => {
      const { data, error } = await api.POST(
        "/api/v1/Auth/deletion/recover-external",
        {
          body: { provider, idToken, nonce, twoFactorCode },
        }
      )
      if (error || !data) {
        throw error ?? new Error("Account recovery failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  const completeTwoFactor = React.useCallback(
    async (
      challengeToken: string,
      code: string,
      method: TwoFactorMethod
    ): Promise<{ requiresPasswordChange: boolean }> => {
      // The sign-in page's choice when it opened this very challenge in this
      // document; false after a reload or on a direct load of the page (OI-154).
      const staySignedIn = getPendingStaySignedIn(challengeToken)
      const useRecoveryCode = method === "recovery"
      // `method` names the factor (S24); `useRecoveryCode` says the same thing to
      // a server that predates it, which reads the boolean alone.
      const { data, error } = await api.POST("/api/v1/auth/2fa/verify", {
        body: { challengeToken, code, useRecoveryCode, method, staySignedIn },
      })
      if (error || !data) {
        throw error ?? new Error("Two-factor verification failed")
      }

      const result = adoptLoginResponse(data)
      if (result.status !== "authenticated") {
        throw new Error("Two-factor verification failed")
      }
      // The sign-in page's method, remembered now that it ended in a session
      // (D-215-4); nothing when the challenge did not start there.
      challengeEnded(challengeToken)
      // A recovery code is single-use: the security page says so once, with the
      // way to a new set (AM-S08-1).
      if (useRecoveryCode) markRecoveryCodeSignIn(data.user?.id)
      return { requiresPasswordChange: result.requiresPasswordChange }
    },
    [adoptLoginResponse]
  )

  const completeEmailVerification = React.useCallback(
    async (email: string, otp: string): Promise<LoginResult> => {
      // The anonymous verify-email path confirms the address and signs the user
      // in, returning the same body as login. Feed it through the shared tail so
      // a 2FA challenge (defensive) or a full session is handled identically.
      const { data, error } = await api.POST("/api/v1/Auth/verify-email", {
        body: { email, otp },
      })
      if (error || !data) {
        throw error ?? new Error("Email verification failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  const completeRegistration = React.useCallback(
    async (input: CompleteRegistrationInput): Promise<LoginResult> => {
      // The server answers with the same body a sign-in does, so the shared
      // tail adopts the new account's session exactly as it would any other.
      const { data, error } = await api.POST(
        "/api/v1/Auth/registration/complete",
        {
          body: {
            pendingId: input.pendingId,
            otp: input.otp,
            password: input.password ?? null,
            firstName: input.firstName ?? null,
            lastName: input.lastName ?? null,
            timeZone: input.timeZone ?? null,
          },
        }
      )
      if (error || !data) {
        throw error ?? new Error("Registration failed")
      }

      return adoptLoginResponse(data)
    },
    [adoptLoginResponse]
  )

  // Both answer a sign-in body. With no session and no challenge in it, the body
  // names the next step instead, and nothing is adopted.
  const adoptEmailCodeResponse = React.useCallback(
    (
      data: Parameters<typeof adoptLoginResponse>[0] & {
        nextStep?: {
          step?: string | null
          passwordRequired?: boolean | null
          nameRequired?: boolean | null
        } | null
      },
      staySignedIn = false
    ): EmailCodeSignInResult => {
      if (!data.token && !data.twoFactorChallengeToken) {
        const nextStep = toEmailCodeNextStep(data.nextStep)
        if (nextStep) return { status: "nextStep", nextStep }
      }
      return adoptLoginResponse(data, staySignedIn)
    },
    [adoptLoginResponse]
  )

  const signInWithEmailCode = React.useCallback(
    async (input: EmailCodeSignInInput): Promise<EmailCodeSignInResult> => {
      const { data, error } = await api.POST("/api/v1/Auth/email-code/sign-in", {
        body: {
          pendingId: input.pendingId,
          otp: input.otp,
          requireFullProof: input.requireFullProof,
          staySignedIn: input.staySignedIn,
        },
      })
      if (error || !data) {
        throw error ?? new Error("Sign-in failed")
      }

      return adoptEmailCodeResponse(data, input.staySignedIn)
    },
    [adoptEmailCodeResponse]
  )

  const recoverWithEmailCode = React.useCallback(
    async (input: EmailCodeRecoveryInput): Promise<EmailCodeSignInResult> => {
      const { data, error } = await api.POST("/api/v1/Auth/email-code/recover", {
        body: {
          pendingId: input.pendingId,
          otp: input.otp,
          twoFactorCode: input.twoFactorCode ?? null,
          requireFullProof: input.requireFullProof,
        },
      })
      if (error || !data) {
        throw error ?? new Error("Account recovery failed")
      }

      return adoptEmailCodeResponse(data)
    },
    [adoptEmailCodeResponse]
  )

  const logout = React.useCallback(async () => {
    // Written before the request: clearing local state below no longer ends a
    // cookie session by itself, so a sign-out that never reaches the server is
    // retried on the next load (completePendingLogout), for this session only.
    const claims = decodeJwt(getAccessToken() ?? "")
    const sessionId = typeof claims?.sid === "string" ? claims.sid : null
    markLogoutPending(sessionId)
    try {
      const { response } = await api.POST("/api/v1/Auth/logout", {
        body: { logoutAllDevices: false },
      })
      if (response.ok) {
        clearLogoutPending()
      } else if (response.status === 401) {
        // The bearer was expired or refused, so the server ran nothing and the
        // HttpOnly refresh cookie is still valid. End it with the cookie itself;
        // the marker stays until that is settled.
        if ((await endCookieSession(sessionId)) !== "unknown") clearLogoutPending()
      }
    } catch {
      /* network failure: the marker stays; clear local state regardless */
    }
    clearTokens()
    setUser(null)
    setStatus("unauthenticated")
    setActiveTimeZone(null)
    // Stops further persistence; the stored layouts stay put so signing back
    // in restores them. Only the *next* account is prevented from inheriting.
    setDataTableScope(null)
    languageAdoptedRef.current = false
    // Awaited here, unlike the other three call sites: this is the one path
    // with somewhere to await from, and the logout request above may well have
    // raced a page's refetch that would otherwise land after the removal.
    await resetUserScopedCache(queryClient)
  }, [queryClient])

  const hasPermission = React.useCallback(
    (permission: string | undefined) => {
      if (!permission) return true
      return permissionMatches(permissions, permission)
    },
    [permissions]
  )

  const hasAnyPermission = React.useCallback(
    (required: string[]) => {
      if (required.length === 0) return true
      return required.some((p) => hasPermission(p))
    },
    [hasPermission]
  )

  const value = React.useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      roles,
      permissions,
      mfaRequirement,
      hasPermission,
      hasAnyPermission,
      login,
      loginExternal,
      recoverAccount,
      recoverAccountExternal,
      completeTwoFactor,
      completeEmailVerification,
      completeRegistration,
      signInWithEmailCode,
      recoverWithEmailCode,
      logout,
      refreshUser: loadCurrentUser,
      rereadUser,
    }),
    [
      status,
      user,
      roles,
      permissions,
      mfaRequirement,
      hasPermission,
      hasAnyPermission,
      login,
      loginExternal,
      recoverAccount,
      recoverAccountExternal,
      completeTwoFactor,
      completeEmailVerification,
      completeRegistration,
      signInWithEmailCode,
      recoverWithEmailCode,
      logout,
      loadCurrentUser,
      rereadUser,
    ]
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = React.useContext(AuthContext)
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider")
  }
  return context
}
