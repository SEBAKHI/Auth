import { describe, expect, it } from "vitest"

import { cspHeaders, parsePolicy } from "./csp-policy"

/**
 * The reader every web.config guard of card S02 uses. It must read a policy
 * the way a browser applies it, or a guard passes what the browser enforces
 * differently.
 */
describe("parsePolicy", () => {
  it("reads directive names case-insensitively, as a browser does", () => {
    expect(parsePolicy("STYLE-SRC 'self'; Img-Src data:")).toEqual({
      "style-src": ["'self'"],
      "img-src": ["data:"],
    })
  })

  it("refuses a repeated directive: the browser would apply only its first copy", () => {
    expect(() => parsePolicy("style-src 'self' 'unsafe-inline'; style-src 'self' 'sha256-x'")).toThrow(
      /repeats style-src/
    )
    expect(() => parsePolicy("style-src 'self'; STYLE-SRC 'unsafe-inline'")).toThrow(/repeats style-src/)
  })

  it("keeps an empty directive and drops empty parts", () => {
    expect(parsePolicy("default-src 'none';; upgrade-insecure-requests ;")).toEqual({
      "default-src": ["'none'"],
      "upgrade-insecure-requests": [],
    })
  })

  it("so a web.config with a repeated directive fails every guard that reads it", () => {
    const xml = `<?xml version="1.0"?><configuration><system.webServer><httpProtocol><customHeaders>
      <add name="Content-Security-Policy" value="style-src 'self' 'unsafe-inline'; style-src 'self'" />
    </customHeaders></httpProtocol></system.webServer></configuration>`
    expect(() => cspHeaders(xml)).toThrow(/repeats style-src/)
  })
})
