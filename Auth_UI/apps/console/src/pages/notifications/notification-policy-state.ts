import type { PrivacyPolicyContent } from "@authsystem/ui/common/policy-document"

/** Shape used when starting a language that has no document yet. */
const EMPTY_DOCUMENT: PrivacyPolicyContent = {
  title: "",
  effectiveDate: "",
  versionLabel: "Version",
  intro: [""],
  sections: [],
  retention: {
    heading: "",
    intro: "",
    columns: ["", "", ""],
    rows: [],
  },
  deletion: {
    heading: "",
    paragraphs: [""],
    bullets: [""],
    button: "",
    signedInHint: "",
  },
  rights: [],
  closing: [],
  contactDpoLabel: "",
  contactVerbisLabel: "",
  contactKepLabel: "",
  unfilledWarning: "",
}

export interface PolicyContentState {
  source: unknown
  language: string
  doc: PrivacyPolicyContent | null
  parseError: string | null
  dirty: boolean
}

/** Parses one language without allowing stale query data to leak across tabs. */
export function parsePolicyDocument(
  source: { contentJson?: string | null } | null | undefined,
  language: string
): PolicyContentState {
  const raw = source?.contentJson
  if (!source) {
    return { source, language, doc: null, parseError: null, dirty: false }
  }
  if (!raw) {
    return {
      source,
      language,
      doc: structuredClone(EMPTY_DOCUMENT),
      parseError: null,
      dirty: false,
    }
  }
  try {
    return {
      source,
      language,
      doc: JSON.parse(raw) as PrivacyPolicyContent,
      parseError: null,
      dirty: false,
    }
  } catch (error) {
    return {
      source,
      language,
      doc: null,
      parseError: (error as Error).message,
      dirty: false,
    }
  }
}
