// jsdom ships no type declarations and @types/jsdom is not a dependency here.
// The harness uses one thing from it: a DOMParser that works in a Playwright
// worker, where no DOM is global. Declared narrowly so nothing else leaks in.
declare module "jsdom" {
  export class JSDOM {
    constructor(html?: string)
    readonly window: { DOMParser: typeof DOMParser }
  }
}
