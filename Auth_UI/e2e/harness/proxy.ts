import http from "node:http"
import net from "node:net"

export interface EgressAttempt {
  /** host:port for CONNECT, the absolute URL for a plain-HTTP proxy request. */
  target: string
  kind: "CONNECT" | "HTTP"
}

export interface HarnessProxy {
  url: string
  /** Attempts to leave the topology since the last clear(), in order. */
  egress(): readonly EgressAttempt[]
  clearEgress(): void
  close(): Promise<void>
}

/**
 * The browser's only way out. Chromium, given this as the context `proxy`,
 * sends every https request as HTTP CONNECT and resolves no name itself
 * (spike item 1). Topology hosts on port 443 are tunnelled to the harness
 * server; anything else is refused with 403 and recorded, so a test that
 * reaches for the real network fails at its end instead of reaching it.
 */
export async function startHarnessProxy(
  hosts: readonly string[],
  serverPort: number
): Promise<HarnessProxy> {
  const allowed = new Set(hosts.map((host) => host.toLowerCase()))
  const egress: EgressAttempt[] = []
  const sockets = new Set<net.Socket>()

  const proxy = http.createServer((request, response) => {
    // A plain-http request through the proxy: never forwarded.
    egress.push({ target: request.url ?? "", kind: "HTTP" })
    response.writeHead(403, { "content-type": "text/plain; charset=utf-8" })
    response.end("harness proxy: plain http is outside the topology")
  })

  proxy.on("connect", (request, client: net.Socket, head: Buffer) => {
    sockets.add(client)
    client.on("close", () => sockets.delete(client))
    client.on("error", () => client.destroy())
    const target = request.url ?? ""
    const [host, port] = target.split(":")
    if (!allowed.has((host ?? "").toLowerCase()) || port !== "443") {
      egress.push({ target, kind: "CONNECT" })
      client.end("HTTP/1.1 403 Forbidden\r\n\r\n")
      return
    }
    const upstream = net.connect(serverPort, "127.0.0.1", () => {
      client.write("HTTP/1.1 200 Connection Established\r\n\r\n")
      if (head.length) upstream.write(head)
      upstream.pipe(client)
      client.pipe(upstream)
    })
    sockets.add(upstream)
    upstream.on("close", () => sockets.delete(upstream))
    upstream.on("error", () => client.destroy())
    client.on("close", () => upstream.destroy())
  })

  await new Promise<void>((resolve, reject) => {
    proxy.once("error", reject)
    proxy.listen(0, "127.0.0.1", () => resolve())
  })

  return {
    url: `http://127.0.0.1:${(proxy.address() as net.AddressInfo).port}`,
    egress: () => [...egress],
    clearEgress: () => {
      egress.length = 0
    },
    close: () =>
      new Promise<void>((resolve) => {
        for (const socket of sockets) socket.destroy()
        proxy.close(() => resolve())
      }),
  }
}
