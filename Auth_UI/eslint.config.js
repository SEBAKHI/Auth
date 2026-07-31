import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['**/dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
    },
  },
  {
    // House-style guards. Each of these categories was swept to zero, so the rule
    // locks the win in rather than reporting pre-existing debt. Categories still
    // being migrated (raw div+Label form fields, ungrouped Select and DropdownMenu
    // items) are deliberately NOT gated yet — a rule that fires a hundred times is
    // noise nobody reads.
    files: ['**/*.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          // Physical direction breaks the three RTL locales. Logical equivalents:
          // ms-/me-, ps-/pe-, start-/end-, text-start/text-end, border-s/border-e,
          // rounded-s*/rounded-e*.
          selector:
            'JSXAttribute[name.name="className"] Literal[value=/(^|\\s)(ml|mr|pl|pr|left|right|border-l|border-r|rounded-l|rounded-r)-|(^|\\s)text-(left|right)(\\s|$)/]',
          message:
            'Use logical properties (ms/me, ps/pe, start/end, text-start/text-end, border-s/border-e, rounded-s/rounded-e) — the app ships RTL locales.',
        },
        {
          // The preset owns colour. Raw scales bypass it and break dark mode.
          selector:
            'JSXAttribute[name.name="className"] Literal[value=/(^|\\s)(text|bg|border|ring|fill|stroke|from|via|to)-(red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone)-[0-9]/]',
          message:
            'Use semantic tokens (bg-primary, text-muted-foreground, text-destructive, var(--chart-N)) — never a raw Tailwind colour scale.',
        },
        {
          // The native picker renders browser chrome the preset cannot reach.
          selector: 'JSXAttribute[name.name="type"] > Literal[value="date"]',
          message:
            'Use DatePicker from @astoom/ui/common/date-picker instead of a native date input.',
        },
        {
          // `space-y-*` sets a margin on every child but the last, which collapses
          // and is overridden per-child; `gap-*` is owned by the container and
          // composes with the Luma spacing primitives.
          selector:
            'JSXAttribute[name.name="className"] Literal[value=/(^|\\s)-?space-[xy]-/]',
          message:
            'Use flex + gap-* (space-y-4 → flex flex-col gap-4, space-x-2 → flex gap-2). On a list keep the markers with [&>li+li]:mt-*, and on an element that is already grid just use gap-*.',
        },
        {
          // `i18n.dir()` reads i18next's `resolvedLanguage`, which settles on the
          // fallback while the on-demand bundle is still loading and is never
          // recomputed by `addResourceBundle`. On a cold Arabic load it answered
          // `ltr` against an RTL document, which inverted the table's column-resize
          // drag and opened the row-detail sheet from the wrong edge.
          selector:
            'CallExpression[callee.type="MemberExpression"][callee.object.name="i18n"][callee.property.name="dir"]',
          message:
            'Use directionForLanguage(i18n.language) from @astoom/i18n — the same source DirectionProvider writes onto documentElement.dir. i18n.dir() reads resolvedLanguage, which is the fallback locale on a cold load.',
        },
        {
          // Chrome resolves `dir="auto"` from a control's VALUE, never from its
          // placeholder or its surroundings — so an empty field always computes
          // `ltr` and opens against the wrong edge in the three RTL locales. Prose
          // the admin writes should inherit the console's direction; copy for one
          // target locale should say which, via `directionForLanguage(lang)`.
          selector:
            'JSXOpeningElement[name.name=/^(Input|Textarea|InputGroupInput|InputGroupTextarea|NativeSelect|input|textarea)$/] > JSXAttribute[name.name="dir"][value.value="auto"]',
          message:
            'dir="auto" on a control resolves from its value, so an empty field renders LTR in an RTL locale. Drop the attribute to inherit the UI direction, or pass directionForLanguage(<the locale being edited>) when the field holds one locale\'s copy.',
        },
        {
          // `text-align: start` inherits as a keyword and is re-resolved at every
          // element against *that element's* direction. So a `dir` on a block box
          // does not just isolate the run, it flips the block's alignment — which
          // left-aligned single cells inside otherwise right-aligned RTL tables.
          selector:
            'JSXOpeningElement[name.name=/^(p|div|dl|dd|dt|ul|ol|li|section|article|aside|main|header|footer|nav|figure|figcaption|blockquote|table|caption|thead|tbody|tr|td|th|h[1-6])$/] > JSXAttribute[name.name="dir"]',
          message:
            'Put the direction on an inline <bdi> inside the block, not on the block itself: `dir` re-resolves the inherited `text-align: start` and breaks RTL alignment. A block whose *content* owns its direction (a rendered preview, a code surface) is the documented exception.',
        },
      ],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            {
              name: 'lucide-react',
              importNames: ['Loader2', 'Loader2Icon'],
              message:
                'Use Spinner from @astoom/ui/spinner — it owns the spin animation and the status role.',
            },
          ],
        },
      ],
    },
  },
  {
    // `dialog.tsx` and `alert-dialog.tsx` centre physically on purpose; `dialog.tsx`
    // documents at length why logical centring plus an `rtl:` translate caused a
    // feedback loop that overflowed the viewport, and the alert dialog cites that
    // same note. `sonner.tsx` maps the toast loading glyph, which is what Spinner
    // itself wraps.
    files: [
      'packages/ui/src/dialog.tsx',
      'packages/ui/src/alert-dialog.tsx',
      'packages/ui/src/sonner.tsx',
      'packages/ui/src/spinner.tsx',
    ],
    rules: {
      'no-restricted-syntax': 'off',
      'no-restricted-imports': 'off',
    },
  },
  {
    // `avatar.tsx` stacks its group members with `-space-x-2` on purpose: the
    // avatars must *overlap*, and `gap-*` cannot take a negative value. Tailwind
    // v4 implements `space-x-*` with `margin-inline-start`, so the overlap already
    // flips correctly under RTL. The component documents this at length.
    files: ['packages/ui/src/avatar.tsx'],
    rules: {
      'no-restricted-syntax': 'off',
    },
  },
  {
    // The design system is not an HMR boundary: its primitives deliberately
    // export their `cva` variant builders (`buttonVariants`, `toggleVariants`, …)
    // next to the component, which is the upstream shadcn shape and what lets
    // sibling components compose them. `only-export-components` is aimed at app
    // route/page modules, where a stale non-component export really does break
    // fast refresh.
    files: ['packages/ui/src/**/*.{ts,tsx}'],
    rules: {
      'react-refresh/only-export-components': 'off',
    },
  },
])
