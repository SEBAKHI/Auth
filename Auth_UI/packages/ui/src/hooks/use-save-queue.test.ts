import { renderHook } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { useSaveQueue } from "./use-save-queue"

/** A promise the test settles when it chooses. */
function held<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 0))

describe("useSaveQueue", () => {
  it("starts a save only once the one before it has settled", async () => {
    const { result } = renderHook(() => useSaveQueue())
    const first = held<string>()
    const started: string[] = []

    const a = result.current(() => {
      started.push("a")
      return first.promise
    })
    const b = result.current(async () => {
      started.push("b")
      return "b"
    })
    await tick()
    expect(started).toEqual(["a"])

    first.resolve("a")
    await expect(a).resolves.toBe("a")
    await expect(b).resolves.toBe("b")
    expect(started).toEqual(["a", "b"])
  })

  it("runs the next save after a failed one, and the failure reaches its own caller only", async () => {
    const { result } = renderHook(() => useSaveQueue())
    const first = held<string>()

    const a = result.current(() => first.promise)
    const b = result.current(async () => "b")
    first.reject(new Error("refused"))

    await expect(a).rejects.toThrow("refused")
    await expect(b).resolves.toBe("b")
  })

  it("keeps one queue across renders", async () => {
    const { result, rerender } = renderHook(() => useSaveQueue())
    const queue = result.current
    rerender()
    expect(result.current).toBe(queue)
  })
})
