import type { IncomingMessage, ServerResponse } from "node:http"
import https from "node:https"
import type { AddressInfo, Socket } from "node:net"

import type { RequestLog } from "./request-log"

export type HostHandler = (
  request: IncomingMessage,
  response: ServerResponse,
  url: URL,
  body: Buffer
) => void | Promise<void>

export interface HarnessServer {
  port: number
  close(): Promise<void>
}

function readBody(request: IncomingMessage) {
  return new Promise<Buffer>((resolve, reject) => {
    const chunks: Buffer[] = []
    request.on("data", (chunk: Buffer) => chunks.push(chunk))
    request.on("end", () => resolve(Buffer.concat(chunks)))
    request.on("error", reject)
  })
}

/**
 * One node:https server per Playwright worker, on a random loopback port. It
 * serves every topology host and picks the handler from the Host header; the
 * proxy (proxy.ts) is the only way the browser reaches it. Every request is
 * logged BEFORE its handler runs, so the log holds what the browser sent even
 * when the handler throws.
 */
export async function startHarnessServer(
  tls: { cert: Buffer; key: Buffer },
  hosts: Record<string, HostHandler>,
  log: RequestLog
): Promise<HarnessServer> {
  const server = https.createServer({ cert: tls.cert, key: tls.key }, async (request, response) => {
    const host = (request.headers.host ?? "").replace(/:\d+$/, "").toLowerCase()
    const url = new URL(request.url ?? "/", `https://${host || "unknown.invalid"}`)
    log.record(host, request, url)
    const handler = hosts[host]
    if (!handler) {
      response.writeHead(421, { "content-type": "text/plain; charset=utf-8" })
      response.end(`harness: no handler for host "${host}"`)
      return
    }
    try {
      await handler(request, response, url, await readBody(request))
    } catch (error) {
      if (!response.headersSent) {
        response.writeHead(500, { "content-type": "text/plain; charset=utf-8" })
      }
      response.end(`harness: ${host}${url.pathname} failed: ${String(error)}`)
    }
  })
  // The browser keeps connections alive across tests; close them with the server.
  const sockets = new Set<Socket>()
  server.on("secureConnection", (socket) => {
    sockets.add(socket)
    socket.on("close", () => sockets.delete(socket))
  })

  await new Promise<void>((resolve, reject) => {
    server.once("error", reject)
    server.listen(0, "127.0.0.1", () => resolve())
  })

  return {
    port: (server.address() as AddressInfo).port,
    close: () =>
      new Promise<void>((resolve) => {
        for (const socket of sockets) socket.destroy()
        server.close(() => resolve())
      }),
  }
}
