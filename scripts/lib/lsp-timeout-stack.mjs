import { execFile } from "node:child_process";
import { promisify } from "node:util";

const run = promisify(execFile);
const timeoutMs = 5_000;

export async function captureLspTimeoutStack(pid) {
  if (process.platform !== "win32") return { status: "skipped", reason: "Windows stack capture only" };
  if (!Number.isInteger(pid) || pid <= 0) return { status: "skipped", reason: "LSP process ID unavailable" };
  const tool = process.env.SHARPTS_LSP_DOTNET_STACK;
  if (!tool) return { status: "skipped", reason: "SHARPTS_LSP_DOTNET_STACK is unset", pid };

  const startedUtc = new Date().toISOString();
  try {
    const { stdout, stderr } = await run(tool, ["report", "--process-id", String(pid)], {
      windowsHide: true, timeout: timeoutMs, maxBuffer: 1024 * 1024,
    });
    return { status: "captured", pid, startedUtc, timeoutMs, stdout, stderr };
  } catch (error) {
    return {
      status: error.killed && error.code === null ? "timed-out" : "failed",
      pid, startedUtc, timeoutMs,
      error: error.message, code: error.code, signal: error.signal,
      stdout: error.stdout ?? "", stderr: error.stderr ?? "",
    };
  }
}
