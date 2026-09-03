import { existsSync, readFileSync } from "node:fs"
import { dirname, join } from "node:path"

import { describe, expect, it } from "vitest"

import {
  FALLBACK_PASSWORD_POLICY,
  PASSWORD_CHARACTER_CLASSES,
  evaluatePassword,
  normalizePasswordPolicy,
  type PasswordPolicy,
} from "./password-policy"

const EVERYTHING: PasswordPolicy = {
  minimumLength: 8,
  requireUppercase: true,
  requireLowercase: true,
  requireDigit: true,
  requireSpecialCharacter: true,
}

function metById(value: string, policy: PasswordPolicy) {
  return Object.fromEntries(
    evaluatePassword(value, policy).map((rule) => [rule.id, rule.met])
  )
}

describe("evaluatePassword", () => {
  it("lists only the rules the policy enables, minimum length first", () => {
    const ids = evaluatePassword("", {
      ...FALLBACK_PASSWORD_POLICY,
      requireDigit: true,
    }).map((rule) => rule.id)

    expect(ids).toEqual(["minLength", "digit"])
  })

  it("carries the configured minimum so the label can name it", () => {
    const [minLength] = evaluatePassword("", {
      ...EVERYTHING,
      minimumLength: 12,
    })

    expect(minLength).toEqual({ id: "minLength", met: false, count: 12 })
  })

  it("judges every rule from the value as typed", () => {
    expect(metById("abc", EVERYTHING)).toEqual({
      minLength: false,
      uppercase: false,
      lowercase: true,
      digit: false,
      special: false,
    })
    expect(metById("Abcdefg1!", EVERYTHING)).toEqual({
      minLength: true,
      uppercase: true,
      lowercase: true,
      digit: true,
      special: true,
    })
  })

  it("counts UTF-16 code units, the same length the server measures", () => {
    // One emoji is a surrogate pair: two units here and two in string.Length.
    expect(
      metById("😀", { ...FALLBACK_PASSWORD_POLICY, minimumLength: 2 })
    ).toEqual({
      minLength: true,
    })
    expect(
      metById("😀", { ...FALLBACK_PASSWORD_POLICY, minimumLength: 3 })
    ).toEqual({
      minLength: false,
    })
  })

  it("recognises only ASCII letters and symbols, exactly like the server", () => {
    // A Latin-1 capital, an Arabic-Indic digit and an Arabic question mark all
    // fail the server's [A-Z] / [0-9] / explicit symbol classes, so the list
    // must not tick them either.
    expect(metById("Éé٣؟", EVERYTHING)).toEqual({
      minLength: false,
      uppercase: false,
      lowercase: false,
      digit: false,
      special: false,
    })
  })

  it("accepts every symbol the server accepts and nothing more", () => {
    const serverSymbols = "!@#$%^&*()-_=+[]{}|;:'\",.<>?/\\"
    for (const symbol of serverSymbols) {
      expect(metById(symbol, EVERYTHING).special, symbol).toBe(true)
    }
    for (const notASymbol of ["`", "~", " ", "£", "€"]) {
      expect(metById(notASymbol, EVERYTHING).special, notASymbol).toBe(false)
    }
  })
})

describe("normalizePasswordPolicy", () => {
  it("coerces the int32 minimum the schema types as number | string", () => {
    expect(
      normalizePasswordPolicy({
        minimumLength: "10",
        requireUppercase: true,
        requireLowercase: false,
        requireDigit: true,
        requireSpecialCharacter: false,
      })
    ).toEqual({
      minimumLength: 10,
      requireUppercase: true,
      requireLowercase: false,
      requireDigit: true,
      requireSpecialCharacter: false,
    })
  })
})

describe("FALLBACK_PASSWORD_POLICY", () => {
  it("is the registry floor and no other rule", () => {
    // Anything stricter would refuse passwords the server accepts whenever the
    // policy could not be fetched; anything looser than the floor is impossible
    // for the server to be configured to.
    expect(FALLBACK_PASSWORD_POLICY).toEqual({
      minimumLength: 6,
      requireUppercase: false,
      requireLowercase: false,
      requireDigit: false,
      requireSpecialCharacter: false,
    })
  })
})

/**
 * The client classes are copies of the server's, and a copy drifts the day
 * someone edits one side. Reading the C# source makes the drift a failing test
 * instead of a password that ticks every box here and is refused there.
 */
describe("the character classes mirror PasswordValidator.cs", () => {
  /** The server's validator, found from wherever vitest was started. */
  function validatorSource(): string {
    const relative = "Auth/Auth.Application/Validators/PasswordValidator.cs"
    let dir = process.cwd()
    for (let i = 0; i < 8; i++) {
      const candidate = join(dir, relative)
      if (existsSync(candidate)) return readFileSync(candidate, "utf8")
      dir = dirname(dir)
    }
    throw new Error(`${relative} not found above ${process.cwd()}`)
  }

  const source = validatorSource()

  /** The pattern literal on the [GeneratedRegex] that declares `name`, unescaped. */
  function serverPattern(name: string): string {
    const declaration = new RegExp(
      String.raw`\[GeneratedRegex\((@?"(?:[^"]|"")*")\)\]\s*private static partial Regex ${name}\(\)`
    ).exec(source)
    if (!declaration) {
      throw new Error(`${name} is no longer declared in PasswordValidator.cs`)
    }
    const literal = declaration[1]
    return literal.startsWith("@")
      ? literal.slice(2, -1).replace(/""/g, '"')
      : literal.slice(1, -1)
  }

  it("still finds the server's declarations (guards the parser itself)", () => {
    expect(
      source.match(/\[GeneratedRegex\(/g)?.length ?? 0
    ).toBeGreaterThanOrEqual(4)
  })

  it.each([
    ["UppercaseRegex", "uppercase"],
    ["LowercaseRegex", "lowercase"],
    ["DigitRegex", "digit"],
    ["SpecialCharRegex", "special"],
  ] as const)(
    "%s equals the client's %s class byte for byte",
    (server, client) => {
      expect(PASSWORD_CHARACTER_CLASSES[client]).toBe(serverPattern(server))
    }
  )
})
