import { spawn } from "node:child_process";

export class LspRequestTimeoutError extends Error {
  constructor(method, id, timeoutMs, stderr) {
    super(`LSP ${method} timed out: ${stderr}`);
    this.method = method;
    this.id = id;
    this.timeoutMs = timeoutMs;
  }
}

/** Small bounded stdio client for repository smoke tests, with no runtime dependencies. */
export class LspStdioClient {
  registeredMethods = [];
  notifications = [];
  #process;
  #pending = new Map();
  #nextId = 1;
  #buffer = Buffer.alloc(0);
  #stderr = "";
  #closing = false;
  #exit;
  #failure;
  #trace;

  constructor(command, args, { cwd, env, timeoutMs = 20_000, trace = false } = {}) {
    this.timeoutMs = timeoutMs;
    this.#trace = trace ? [] : null;
    this.#process = spawn(command, args, { cwd, env, windowsHide: true, stdio: "pipe" });
    this.#exit = new Promise((resolve) => {
      this.#process.once("close", (code, signal) => {
        this.#fail(new Error(`LSP exited (${code ?? signal}): ${this.#stderr}`));
        resolve({ code, signal });
      });
    });
    this.#process.on("error", (error) => this.#fail(error));
    this.#process.stdin.on("error", (error) => this.#fail(error));
    this.#process.stderr.on("data", (chunk) => {
      this.#stderr = (this.#stderr + chunk.toString("utf8")).slice(-65_536);
    });
    this.#process.stdout.on("data", (chunk) => {
      try {
        if (this.#trace) this.#recordTrace("stdout", { bytes: chunk.length });
        this.#buffer = Buffer.concat([this.#buffer, chunk]);
        if (this.#buffer.length > 16 * 1024 * 1024) throw new Error("LSP frame exceeds smoke limit");
        this.#readFrames();
      } catch (error) {
        this.#fail(error);
        this.#process.kill();
      }
    });
  }

  get livePid() {
    return this.#process.exitCode === null && this.#process.signalCode === null ? this.#process.pid : undefined;
  }

  diagnosticTrace() {
    return { pid: this.#process.pid, wire: this.#trace?.slice(), stderr: this.#stderr };
  }

  #recordTrace(event, { id, method, bytes }) {
    if (!this.#trace) return;
    if (this.#trace.length === 128) this.#trace.shift();
    this.#trace.push({ utc: new Date().toISOString(), event, id, method, bytes });
  }

  #fail(error) {
    this.#failure ??= error;
    for (const { reject, timer } of this.#pending.values()) {
      clearTimeout(timer);
      reject(error);
    }
    this.#pending.clear();
  }

  #send(message) {
    if (this.#failure) throw this.#failure;
    const data = Buffer.from(JSON.stringify({ jsonrpc: "2.0", ...message }), "utf8");
    this.#recordTrace("send", message);
    this.#process.stdin.write(Buffer.concat([
      Buffer.from(`Content-Length: ${data.length}\r\n\r\n`, "ascii"), data,
    ]));
  }

  #readFrames() {
    for (;;) {
      const headerEnd = this.#buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;
      const header = this.#buffer.subarray(0, headerEnd).toString("ascii");
      const length = /^content-length:\s*(\d+)\s*$/im.exec(header);
      if (!length) throw new Error(`Invalid LSP header: ${header}`);
      const size = Number(length[1]);
      if (size > 16 * 1024 * 1024) throw new Error("LSP payload exceeds smoke limit");
      const end = headerEnd + 4 + size;
      if (this.#buffer.length < end) return;
      const message = JSON.parse(this.#buffer.subarray(headerEnd + 4, end).toString("utf8"));
      this.#buffer = this.#buffer.subarray(end);
      this.#recordTrace("receive", message);
      if (message.method && message.id !== undefined) {
        // Clients in these tests have no configuration or registration side effects.
        if (message.method === "workspace/configuration") {
          this.#send({ id: message.id, result: (message.params?.items ?? []).map(() => null) });
        } else if (message.method === "client/registerCapability") {
          this.registeredMethods.push(...(message.params?.registrations ?? []).map((item) => item.method));
          this.#send({ id: message.id, result: null });
        } else {
          this.#send({ id: message.id, error: { code: -32601, message: "Unsupported smoke client method" } });
        }
      } else if (message.id !== undefined) {
        const pending = this.#pending.get(message.id);
        if (!pending) continue;
        this.#pending.delete(message.id);
        clearTimeout(pending.timer);
        pending.resolve(message);
      } else if (message.method) {
        this.notifications.push(message);
        if (this.notifications.length > 1024) this.notifications.shift();
      }
    }
  }

  notify(method, params) { this.#send({ method, params }); }

  request(method, params) { return this.beginRequest(method, params).response; }

  /** Cancellation sends the LSP notification; it never invents a local server response. */
  beginRequest(method, params) {
    const id = this.#nextId++;
    const response = this.#closing ? Promise.reject(new Error("LSP client is closing"))
      : this.#failure ? Promise.reject(this.#failure) : new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.#pending.delete(id);
        if (this.#trace) this.#recordTrace("timeout", { id, method });
        reject(new LspRequestTimeoutError(method, id, this.timeoutMs, this.#stderr));
      }, this.timeoutMs);
      this.#pending.set(id, { resolve, reject, timer });
      try {
        this.#send({ id, method, params });
      } catch (error) {
        clearTimeout(timer);
        this.#pending.delete(id);
        reject(error);
      }
    });
    return {
      id, response,
      cancel: () => {
        if (!this.#pending.has(id)) return false;
        this.notify("$/cancelRequest", { id });
        return true;
      },
    };
  }

  async close({ force = false } = {}) {
    try {
      if (!force && this.#process.exitCode === null && !this.#process.killed) {
        const response = await this.request("shutdown", null);
        if (response.error) throw new Error(JSON.stringify(response.error));
        this.#closing = true;
        this.notify("exit", null);
        let timer;
        const exit = await Promise.race([
          this.#exit,
          new Promise((_, reject) => { timer = setTimeout(() => reject(new Error("LSP exit timed out")), 5_000); }),
        ]).finally(() => clearTimeout(timer));
        if (exit.code !== 0) throw new Error(`LSP shutdown failed: ${this.#stderr}`);
      }
    } finally {
      this.#closing = true;
      if (this.#process.exitCode === null) this.#process.kill();
      this.#fail(new Error("LSP client closed"));
    }
  }
}
