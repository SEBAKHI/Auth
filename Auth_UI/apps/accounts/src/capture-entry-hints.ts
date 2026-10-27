import { captureEntryHints } from "@authsystem/i18n/flow-hints"

import { entryContextFor } from "./entry-hints"

// The language and colour mode the application that sent this tab asked for
// (ui_locales, ui_theme), kept for its flow and taken off the address bar.
//
// A module of its own, imported FIRST by main.tsx: imports are evaluated in
// order, and the router module snapshots the address when it is evaluated. Run
// any later, the router keeps the parameters and writes them back on its next
// navigation (a tab change, the return after sign-in), and a reload then
// applies the application's language over the one the user chose since.
captureEntryHints(entryContextFor(window.location))
