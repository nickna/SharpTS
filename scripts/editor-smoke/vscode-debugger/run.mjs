import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const script = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(script, "../../..");
const options = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  assert.ok(process.argv[i]?.startsWith("--") && process.argv[i + 1], "use --name value pairs");
  options.set(process.argv[i].slice(2), process.argv[i + 1]);
}
const mode = options.get("mode") ?? "both";
assert.ok(["development", "vsix", "both"].includes(mode), "--mode must be development, vsix, or both");
const modes = mode === "both" ? ["development", "vsix"] : [mode];
const code = options.get("code") ?? (process.platform === "win32"
  ? path.join(process.env.LOCALAPPDATA, "Programs/Microsoft VS Code/Code.exe") : "code");
let cli = options.get("code-cli");
if (process.platform === "win32" && !cli && modes.includes("vsix")) {
  const install = path.dirname(code);
  const candidates = [path.join(install, "resources/app/out/cli.js")];
  for (const entry of await fs.readdir(install, { withFileTypes: true }))
    if (entry.isDirectory()) candidates.push(path.join(install, entry.name, "resources/app/out/cli.js"));
  const existing = [];
  for (const candidate of candidates) try { await fs.access(candidate); existing.push(candidate); } catch {}
  assert.equal(existing.length, 1, "use --code-cli for an ambiguous VS Code install");
  cli = existing[0];
}
const hash = async (file) => createHash("sha256").update(await fs.readFile(file)).digest("hex");
const base = path.resolve(root, options.get("artifacts") ?? "artifacts/editor-debugger/vscode");
await fs.mkdir(base, { recursive: true });
const runDirectory = await fs.mkdtemp(path.join(base, "run-"));
const git = spawnSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8", windowsHide: true });
assert.equal(git.status, 0, git.stderr);
const results = [];
for (const variant of modes) {
  const artifacts = path.join(runDirectory, variant);
  const workspace = path.join(artifacts, "workspace");
  const userData = path.join(artifacts, "user-data");
  const extensions = path.join(artifacts, "extensions");
  await fs.mkdir(extensions, { recursive: true });
  await fs.cp(path.join(root, "tests/fixtures/InterpreterDebuggerAcceptance"), workspace, { recursive: true });
  let packaged;
  let developmentExtension;
  let binaryDirectory;
  if (variant === "vsix") {
    const packageFile = path.resolve(root, options.get("package") ?? "artifacts/editor-shipping/vscode/package/package.json");
    packaged = JSON.parse(await fs.readFile(packageFile, "utf8"));
    assert.equal(await hash(packaged.vsix), packaged.sha256, "VSIX must match the prepared package report");
    binaryDirectory = packaged.binaries;
    const args = ["--user-data-dir", userData, "--extensions-dir", extensions,
      "--install-extension", packaged.vsix, "--force"];
    const installed = spawnSync(code, cli ? [cli, ...args] : args, {
      env: cli ? { ...process.env, ELECTRON_RUN_AS_NODE: "1" } : process.env,
      encoding: "utf8", timeout: 60_000, windowsHide: true,
    });
    await fs.writeFile(path.join(artifacts, "install.log"), installed.stdout + installed.stderr);
    assert.equal(installed.status, 0, installed.stdout + installed.stderr);
  } else {
    developmentExtension = path.resolve(root, options.get("development-extension") ?? "extensions/vscode-sharpts");
    await fs.access(path.join(developmentExtension, "out/extension.js"));
    binaryDirectory = path.join(developmentExtension, "bin/server");
  }
  const binaryHashes = {};
  for (const name of ["SharpTS.LanguageServer.dll", "SharpTS.dll", "SharpTS.DebugAdapter.dll"])
    binaryHashes[name] = await hash(path.join(binaryDirectory, name));
  const project = path.join(workspace, "tsconfig.json");
  // A real, existing reference makes forwarding additionalReferences observable at launch.
  const references = [path.join(binaryDirectory, "SharpTS.dll")];
  const config = { mode: variant, artifacts, workspace, extensions, project, references, binaryHashes, package: packaged,
    developmentExtension, sourceCommit: git.stdout.trim(), result: path.join(artifacts, "result.json") };
  await fs.mkdir(path.join(workspace, ".vscode"), { recursive: true });
  await fs.writeFile(path.join(workspace, ".vscode/settings.json"), JSON.stringify({
    "sharpts.dotnetPath": options.get("dotnet") ?? "dotnet", "sharpts.projectFile": project,
    "sharpts.additionalReferences": references, "files.autoSave": "off",
    "security.workspace.trust.enabled": false, "update.mode": "none",
    "extensions.autoCheckUpdates": false, "extensions.autoUpdate": false,
    "telemetry.telemetryLevel": "off", "debug.openDebug": "neverOpen", "debug.saveBeforeStart": "allEditors",
  }, null, 2) + "\n");
  const launch = JSON.parse(await fs.readFile(path.join(workspace, "interpreter.launch.json"), "utf8"));
  await fs.writeFile(path.join(workspace, ".vscode/launch.json"), JSON.stringify(launch, null, 2) + "\n");
  await fs.writeFile(path.join(workspace, "command.ts"), "const savedValue = 40;\nconsole.log(`saved=${savedValue + 2}`);\n");
  await fs.writeFile(path.join(workspace, "terminate.ts"), "let ticks = 0;\nwhile (true) {\n    ticks++;\n}\n");
  const configFile = path.join(artifacts, "test-config.json");
  await fs.writeFile(configFile, JSON.stringify(config, null, 2) + "\n");
  const args = ["--user-data-dir", userData, "--extensions-dir", extensions,
    "--extensionDevelopmentPath", script];
  if (developmentExtension) args.push("--extensionDevelopmentPath", developmentExtension);
  args.push("--extensionTestsPath", path.join(script, "test.cjs"), "--disable-workspace-trust",
    "--skip-welcome", "--skip-release-notes", "--disable-updates", "--disable-telemetry",
    "--disable-gpu", "--new-window", workspace);
  const env = { ...process.env, SHARPTS_DEBUGGER_SMOKE_CONFIG: configFile };
  delete env.ELECTRON_RUN_AS_NODE;
  const child = spawn(code, args, { cwd: root, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
  let log = "";
  child.stdout.on("data", (chunk) => { log += chunk; });
  child.stderr.on("data", (chunk) => { log += chunk; });
  let timedOut = false;
  let cleanup;
  let timer;
  const completion = await new Promise((resolve) => {
    timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === "win32") {
        const killed = spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"],
          { windowsHide: true, encoding: "utf8", timeout: 10_000 });
        cleanup = { status: killed.status, error: killed.error?.message,
          output: (killed.stdout ?? "") + (killed.stderr ?? "") };
      } else cleanup = { killed: child.kill("SIGKILL") };
      // A failed tree cleanup must not leave the runner waiting forever on an exit event
      // or inherited output pipes. Preserve its diagnosis and let the caller inspect it.
      child.unref();
      child.stdout.destroy();
      child.stderr.destroy();
      resolve({ exitCode: null });
    }, 180_000);
    child.once("error", (error) => resolve({ exitCode: null, error: String(error) }));
    child.once("exit", (exitCode) => resolve({ exitCode }));
  }).finally(() => clearTimeout(timer));
  await fs.writeFile(path.join(artifacts, "extension-host.log"), log);
  await fs.writeFile(path.join(artifacts, "runner-result.json"), JSON.stringify({
    status: timedOut || completion.error || completion.exitCode !== 0 ? "failed" : "exited",
    artifacts, editorPid: child.pid, timedOut, cleanup, ...completion,
  }, null, 2) + "\n");
  assert.equal(timedOut, false, `VS Code exceeded 180 seconds; see ${artifacts}`);
  assert.equal(completion.error, undefined, `VS Code launch failed: ${completion.error}; see ${artifacts}`);
  assert.equal(completion.exitCode, 0, `VS Code failed; see ${artifacts}/extension-host.log`);
  const result = JSON.parse(await fs.readFile(config.result, "utf8"));
  assert.equal(result.status, "passed");
  results.push(result);
}
const result = { status: "passed", artifacts: runDirectory, results };
await fs.writeFile(path.join(runDirectory, "result.json"), JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify({ status: result.status, artifacts: runDirectory,
  results: results.map((item) => ({ mode: item.mode, editor: item.editor, platform: item.platform,
    extension: item.extension, sessions: item.sessions.length, coverage: item.coverage })) }, null, 2));
