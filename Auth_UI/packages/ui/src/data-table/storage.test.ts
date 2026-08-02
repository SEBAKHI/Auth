import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

const fetchUiPreferences = vi.fn(async () => ({}) as Record<string, string>)
const putUiPreference =
  vi.fn<(key: string, value: string) => Promise<boolean>>()

vi.mock("@authsystem/api/ui-preferences", () => ({
  fetchUiPreferences: () => fetchUiPreferences(),
  putUiPreference: (key: string, value: string) => putUiPreference(key, value),
  deleteUiPreference: vi.fn(async () => true),
}))

const {
  __resetDataTableStorage,
  readTableLayout,
  setDataTableScope,
  subscribeTableLayout,
  writeTableLayout,
} = await import("./storage")

const ALICE = "aaaaaaaa-0000-0000-0000-000000000001"
const BOB = "bbbbbbbb-0000-0000-0000-000000000002"

function stubLocalStorage(): Map<string, string> {
  const store = new Map<string, string>()
  const mock: Storage = {
    getItem: (key) => store.get(key) ?? null,
    setItem: (key, value) => {
      store.set(key, String(value))
    },
    removeItem: (key) => {
      store.delete(key)
    },
    clear: () => store.clear(),
    key: (index) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size
    },
  }
  Object.defineProperty(window, "localStorage", {
    value: mock,
    configurable: true,
    writable: true,
  })
  return store
}

let store: Map<string, string>

beforeEach(() => {
  vi.useFakeTimers()
  store = stubLocalStorage()
  fetchUiPreferences.mockClear()
  putUiPreference.mockClear()
  putUiPreference.mockResolvedValue(true)
  fetchUiPreferences.mockResolvedValue({})
})

afterEach(() => {
  __resetDataTableStorage()
  vi.useRealTimers()
})

describe("scoping", () => {
  it("persists nothing until a user is known", () => {
    writeTableLayout("users", { order: ["a"] })

    expect(store.size).toBe(0)
    expect(readTableLayout("users")).toEqual({})
  })

  it("keeps two accounts on one browser apart", () => {
    setDataTableScope(ALICE)
    writeTableLayout("users", { order: ["a", "b"] })

    setDataTableScope(BOB)
    expect(readTableLayout("users")).toEqual({})

    setDataTableScope(ALICE)
    expect(readTableLayout("users")).toEqual({ order: ["a", "b"] })
  })

  it("leaves the layout in place when the user signs out", () => {
    setDataTableScope(ALICE)
    writeTableLayout("users", { order: ["a"] })

    setDataTableScope(null)
    setDataTableScope(ALICE)

    expect(readTableLayout("users")).toEqual({ order: ["a"] })
  })
})

describe("legacy migration", () => {
  it("folds the unscoped keys into the first scope and deletes them", () => {
    store.set("dt:cols:users", JSON.stringify({ email: true }))
    store.set("dt:size:users", JSON.stringify({ name: 200 }))
    store.set("dt:order:users", JSON.stringify(["name", "email"]))

    setDataTableScope(ALICE)

    expect(readTableLayout("users")).toEqual({
      cols: { email: true },
      size: { name: 200 },
      order: ["name", "email"],
    })
    // Deleting is the point: leaving them keeps the cross-account leak.
    expect(store.has("dt:cols:users")).toBe(false)
    expect(store.has("dt:size:users")).toBe(false)
    expect(store.has("dt:order:users")).toBe(false)
  })

  it("runs once, so a second account does not inherit the first one's layout", () => {
    store.set("dt:order:users", JSON.stringify(["name"]))

    setDataTableScope(ALICE)
    setDataTableScope(BOB)

    expect(readTableLayout("users")).toEqual({})
  })
})

describe("server sync", () => {
  it("debounces writes and sends one document per table", async () => {
    setDataTableScope(ALICE)

    writeTableLayout("users", { order: ["a"] })
    writeTableLayout("users", { order: ["a", "b"] })
    writeTableLayout("roles", { order: ["x"] })
    expect(putUiPreference).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1000)

    expect(putUiPreference).toHaveBeenCalledTimes(2)
    expect(putUiPreference).toHaveBeenCalledWith(
      "table:users",
      JSON.stringify({ order: ["a", "b"] })
    )
    expect(putUiPreference).toHaveBeenCalledWith(
      "table:roles",
      JSON.stringify({ order: ["x"] })
    )
  })

  it("applies the server copy over the local cache and notifies the table", async () => {
    fetchUiPreferences.mockResolvedValue({
      "table:users": JSON.stringify({ order: ["from", "server"] }),
      "ignored:key": JSON.stringify({ order: ["nope"] }),
    })
    const listener = vi.fn()

    setDataTableScope(ALICE)
    subscribeTableLayout("users", listener)
    await vi.runAllTimersAsync()

    expect(readTableLayout("users")).toEqual({ order: ["from", "server"] })
    expect(listener).toHaveBeenCalled()
  })

  it("does not let a slow response overwrite the next account's layout", async () => {
    // Alice's fetch is still in flight when Bob signs in; Bob's own fetch
    // answers empty. Alice's late payload must not land under Bob's scope.
    let releaseAlice: (value: Record<string, string>) => void = () => {}
    fetchUiPreferences
      .mockReturnValueOnce(
        new Promise((resolve) => {
          releaseAlice = resolve
        })
      )
      .mockResolvedValue({})

    setDataTableScope(ALICE)
    setDataTableScope(BOB)
    releaseAlice({ "table:users": JSON.stringify({ order: ["alice"] }) })
    await vi.runAllTimersAsync()

    expect(readTableLayout("users")).toEqual({})
  })

  it("keeps an edit made while the fetch was in flight", async () => {
    let release: (value: Record<string, string>) => void = () => {}
    fetchUiPreferences.mockReturnValue(
      new Promise((resolve) => {
        release = resolve
      })
    )

    setDataTableScope(ALICE)
    writeTableLayout("users", { order: ["just", "moved"] })
    release({ "table:users": JSON.stringify({ order: ["stale"] }) })
    await vi.runAllTimersAsync()

    expect(readTableLayout("users")).toEqual({ order: ["just", "moved"] })
  })
})
