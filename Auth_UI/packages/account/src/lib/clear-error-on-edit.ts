import * as React from "react"
import type { FieldValues, UseFormReturn } from "react-hook-form"

/**
 * A field's error goes away on its next edit, whichever field it is. Paired
 * with `reValidateMode: "onBlur"`, a field is judged again when it is left,
 * never on each keystroke (see useEmailFieldCheck for why), and an error never
 * stays under a value that is being corrected. One subscription for the whole
 * form, so a field added later follows the rule without being wired to it.
 */
export function useClearErrorOnEdit<T extends FieldValues>(
  form: UseFormReturn<T>
) {
  React.useEffect(() => {
    const subscription = form.watch((_values, { name, type }) => {
      // `type` is "change" only for an edit; a reset or setValue has none.
      if (type === "change" && name) form.clearErrors(name)
    })
    return () => subscription.unsubscribe()
  }, [form])
}
