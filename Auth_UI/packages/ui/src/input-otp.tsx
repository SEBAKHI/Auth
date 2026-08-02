"use client"

import * as React from "react"
import { OTPInput, OTPInputContext } from "input-otp"
import { MinusIcon } from "lucide-react"

import { cn } from "@authsystem/ui/utils"

type OtpDirection = "ltr" | "rtl"

const InputOtpDirectionContext = React.createContext<OtpDirection>("ltr")

function InputOTP({
  className,
  containerClassName,
  dir = "ltr",
  ...props
}: React.ComponentProps<typeof OTPInput> & {
  containerClassName?: string
}) {
  const direction: OtpDirection = dir === "rtl" ? "rtl" : "ltr"

  return (
    <InputOtpDirectionContext.Provider value={direction}>
      <OTPInput
        data-slot="input-otp"
        dir={direction}
        containerClassName={cn(
          "flex items-center gap-2 has-disabled:opacity-50",
          direction === "ltr" ? "[direction:ltr]" : "[direction:rtl]",
          containerClassName
        )}
        className={cn("disabled:cursor-not-allowed", className)}
        {...props}
      />
    </InputOtpDirectionContext.Provider>
  )
}

function InputOTPGroup({ className, ...props }: React.ComponentProps<"div">) {
  const direction = React.useContext(InputOtpDirectionContext)

  return (
    <div
      data-slot="input-otp-group"
      // `dir` orders the slots here, not text: a code is entered and read in one
      // direction regardless of the surrounding locale, which is why the group
      // takes its direction from the field rather than the page.
      // eslint-disable-next-line no-restricted-syntax
      dir={direction}
      className={cn("flex items-center gap-2", className)}
      {...props}
    />
  )
}

function InputOTPSlot({
  index,
  className,
  ...props
}: React.ComponentProps<"div"> & {
  index: number
}) {
  const inputOTPContext = React.useContext(OTPInputContext)
  const { char, hasFakeCaret, isActive } = inputOTPContext?.slots[index] ?? {}

  return (
    <div
      data-slot="input-otp-slot"
      data-active={isActive}
      className={cn(
        "relative flex size-10 items-center justify-center rounded-2xl border border-transparent bg-input/50 text-base transition-[color,box-shadow,background-color] outline-none aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 data-[active=true]:z-10 data-[active=true]:border-ring data-[active=true]:ring-3 data-[active=true]:ring-ring/30 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40",
        className
      )}
      {...props}
    >
      {char}
      {hasFakeCaret && (
        <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
          <div className="h-4 w-px animate-caret-blink bg-foreground duration-1000" />
        </div>
      )}
    </div>
  )
}

function InputOTPSeparator({ ...props }: React.ComponentProps<"div">) {
  return (
    <div data-slot="input-otp-separator" role="separator" {...props}>
      <MinusIcon />
    </div>
  )
}

export { InputOTP, InputOTPGroup, InputOTPSlot, InputOTPSeparator }
// Re-exported so consumers outside packages/ui don't need their own
// dependency on input-otp just for the digit pattern.
export { REGEXP_ONLY_DIGITS } from "input-otp"
