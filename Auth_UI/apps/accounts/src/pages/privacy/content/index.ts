import type { LanguageCode } from "@authsystem/i18n"

import { ar } from "./ar"
import { en } from "./en"
import { fa } from "./fa"
import { fr } from "./fr"
import { tr } from "./tr"
import { ur } from "./ur"
import { zh } from "./zh"
import type { PrivacyPolicyContent } from "./types"

/**
 * One full policy document per supported language. The Record type makes a
 * missing language a compile error — the same parity guarantee the main
 * i18n resources have.
 */
export const PRIVACY_CONTENT: Record<LanguageCode, PrivacyPolicyContent> = {
  en,
  ar,
  tr,
  fr,
  zh,
  ur,
  fa,
}
