import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"

import { api } from "@authsystem/api/client"
import type { LanguageCode } from "@authsystem/i18n"

import { PRIVACY_CONTENT } from "./content"
import {
  FALLBACK_DISCLOSURE,
  POLICY_VERSION,
  type PolicyDisclosure,
  type PrivacyPolicyContent,
} from "./content/types"

export interface ResolvedPolicy {
  content: PrivacyPolicyContent
  disclosure: PolicyDisclosure
  version: string
  /** True when the bundled copy is being shown because the API is unreachable. */
  isFallback: boolean
}

/**
 * Resolves the policy to render: the DATABASE is authoritative (content is
 * edited in the console, not deployed), and the numeric disclosures come from
 * the running configuration on every request.
 *
 * The bundled document is a genuine fallback, not the source of truth — a
 * privacy policy must stay readable even if the API is down, since it is a
 * legal disclosure the user has a right to see.
 */
export function usePrivacyPolicy(): { policy: ResolvedPolicy; isLoading: boolean } {
  const { i18n } = useTranslation()
  const language = i18n.language

  const query = useQuery({
    queryKey: ["privacy-policy", language],
    queryFn: async () => {
      const { data, error } = await api.GET("/api/v1/privacy-policy/published", {
        params: { query: { language } },
      })
      if (error || !data) throw error ?? new Error("Policy unavailable")
      return data
    },
    staleTime: 5 * 60 * 1000,
    retry: 1,
  })

  const bundled =
    PRIVACY_CONTENT[language as LanguageCode] ?? PRIVACY_CONTENT.en

  if (query.data?.contentJson) {
    try {
      // The generated client types .NET ints as `number | string`; coerce at
      // the boundary so the rendered document always shows a real number.
      const asNumber = (value: number | string | undefined, fallback: number) =>
        value === undefined ? fallback : Number(value)
      const served = query.data.disclosure

      return {
        policy: {
          content: JSON.parse(query.data.contentJson) as PrivacyPolicyContent,
          disclosure: {
            graceDays: asNumber(served?.graceDays, FALLBACK_DISCLOSURE.graceDays),
            otpValidityMinutes: asNumber(
              served?.otpValidityMinutes,
              FALLBACK_DISCLOSURE.otpValidityMinutes
            ),
            loginAttemptRetentionDays: asNumber(
              served?.loginAttemptRetentionDays,
              FALLBACK_DISCLOSURE.loginAttemptRetentionDays
            ),
            outboxRetentionDays: asNumber(
              served?.outboxRetentionDays,
              FALLBACK_DISCLOSURE.outboxRetentionDays
            ),
            policyVersion: served?.policyVersion ?? FALLBACK_DISCLOSURE.policyVersion,
          },
          version: query.data.version ?? POLICY_VERSION,
          isFallback: false,
        },
        isLoading: false,
      }
    } catch {
      /* stored document unparseable — fall through to the bundled copy */
    }
  }

  return {
    policy: {
      content: bundled,
      disclosure: FALLBACK_DISCLOSURE,
      version: POLICY_VERSION,
      isFallback: true,
    },
    isLoading: query.isLoading,
  }
}
