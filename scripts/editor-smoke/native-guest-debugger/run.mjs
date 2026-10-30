import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { writeFileSync } from "node:fs";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

assert.equal(`${process.platform}-${process.arch}`, "win32-x64", "this acceptance slice requires Windows x64");
const script = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(script, "../../..");
const values = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  assert.ok(process.argv[i]?.startsWith("--") && process.argv[i + 1], "use --name value pairs");
  values.set(process.argv[i].slice(2), process.argv[i + 1]);
}
assert.ok(values.has("cpp-extension"), "pass the official installed/staged Microsoft C++ extension directory");
const code = values.get("code") ?? path.join(process.env.USERPROFILE, "AppData/Local/Programs/Microsoft VS Code/Code.exe");
const dotnet = values.get("dotnet") ?? "dotnet";
const compiler = path.resolve(root, values.get("compiler") ?? "src/SharpTS/bin/Release/net10.0/SharpTS.dll");
const cpp = path.resolve(values.get("cpp-extension"));
const manifest = JSON.parse(await fs.readFile(path.join(cpp, "package.json"), "utf8"));
assert.equal(`${manifest.publisher}.${manifest.name}`, "ms-vscode.cpptools");
// The SDK's supported findvcvarsall script calls Windows command-line tools.
// Drop old SDK-smoke artifact paths and deduplicate: cmd.exe discards an
// inherited PATH above its command-length limit, hiding even standard OS tools.
const toolEnv = { ...process.env };
const inheritedPath = Object.entries(toolEnv).filter(([name]) => name.toLowerCase() === "path")
  .flatMap(([, value]) => value.split(";"))
  .filter(value => value && !/[\\/]Temp[\\/]sharpts-sdk-smoke-|[\\/]\.codex[\\/]worktrees[\\/].*[\\/]artifacts[\\/]/i.test(value));
for (const name of Object.keys(toolEnv)) if (name.toLowerCase() === "path") delete toolEnv[name];
const seenPaths = new Set();
toolEnv.Path = [path.join(process.env.SystemRoot ?? "C:/Windows", "System32"),
  process.env.SystemRoot ?? "C:/Windows", ...(path.isAbsolute(dotnet) ? [path.dirname(dotnet)] : []),
  ...inheritedPath].filter(value => {
    const key = value.toLowerCase();
    if (seenPaths.has(key)) return false;
    seenPaths.add(key); return true;
  }).join(";");
assert.ok(toolEnv.Path.length < 8191, "native SDK command files require a clean PATH shorter than cmd.exe's limit");
const base = path.resolve(root, values.get("artifacts") ?? "artifacts/native-guest-debugger");
await fs.mkdir(base, { recursive: true });
const artifacts = await fs.mkdtemp(path.join(base, "run-"));
const workspace = path.join(artifacts, "workspace");
const userData = path.join(artifacts, "user-data");
const extensions = path.join(artifacts, "extensions");
await fs.mkdir(path.join(userData, "User"), { recursive: true });
await fs.mkdir(path.join(workspace, ".vscode"), { recursive: true });
await fs.mkdir(extensions, { recursive: true });
await fs.cp(cpp, path.join(extensions, `ms-vscode.cpptools-${manifest.version}`), { recursive: true });
const settings = { "security.workspace.trust.enabled": false, "update.mode": "none", "files.autoSave": "off",
  "extensions.autoCheckUpdates": false, "extensions.autoUpdate": false, "telemetry.telemetryLevel": "off",
  "C_Cpp.intelliSenseEngine": "disabled" };
for (const file of [path.join(userData, "User/settings.json"), path.join(workspace, ".vscode/settings.json")])
  await fs.writeFile(file, JSON.stringify(settings, null, 2) + "\n");
const hash = async file => createHash("sha256").update(await fs.readFile(file)).digest("hex");
const compilerSha256 = await hash(compiler);
const harnessHashes = {};
for (const name of ["run.mjs", "test.cjs", "extension.cjs", "package.json"])
  harnessHashes[name] = await hash(path.join(script, name));
const sourceHashes = {};
const markers = {};
for (const name of ["main.ts", "helper.ts"]) {
  const source = await fs.readFile(path.join(root, "tests/fixtures/NativeGuestDebuggerAcceptance", name));
  const destination = path.join(workspace, name);
  await fs.writeFile(destination, source);
  sourceHashes[name] = await hash(destination);
  source.toString("utf8").split(/\r?\n/).forEach((line, i) => {
    const marker = line.match(/@(break|step):([\w-]+)/);
    if (marker) markers[`${marker[1]}:${marker[2]}`] = { path: destination, line: i + 1 };
  });
}
async function runOwned(command, args, options = {}) {
  const { timeout = 300_000, ...childOptions } = options;
  const child = spawn(command, args, { cwd: workspace, env: toolEnv, windowsHide: true,
    stdio: ["ignore", "pipe", "pipe"], ...childOptions });
  let stdout = "", stderr = "", error, timedOut = false, cleanupError, cleanupTimer;
  let finishTimeout;
  const timeoutCompletion = new Promise(resolve => { finishTimeout = resolve; });
  child.stdout.on("data", chunk => { stdout += chunk; });
  child.stderr.on("data", chunk => { stderr += chunk; });
  child.once("error", value => { error = value; });
  const timer = setTimeout(() => {
    timedOut = true;
    if (child.pid) {
      const cleanup = spawnSync(path.join(process.env.SystemRoot ?? "C:/Windows", "System32/taskkill.exe"),
        ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true, timeout: 10_000 });
      if (cleanup.status !== 0) cleanupError = `${cleanup.stderr ?? ""} ${cleanup.error ?? ""}`;
    }
    cleanupTimer = setTimeout(() => {
      cleanupError = `${cleanupError ?? ""} Owned PID ${child.pid} did not close after tree cleanup.`.trim();
      try { child.kill(); } catch (error) { cleanupError += ` Direct-child termination failed: ${error}`; }
      child.stdout.destroy();
      child.stderr.destroy();
      child.unref();
      finishTimeout(null);
    }, 2_000);
  }, timeout);
  const status = await Promise.race([new Promise(resolve => child.once("close", resolve)), timeoutCompletion])
    .finally(() => { clearTimeout(timer); clearTimeout(cleanupTimer); });
  return { status, stdout, stderr, error, timedOut, cleanupError };
}
async function execute(command, args, logName, options = {}) {
  const result = await runOwned(command, args, options);
  const output = `${result.stdout}\n${result.stderr}\n${result.error ?? ""}\n${result.cleanupError ?? ""}`;
  if (logName) writeFileSync(path.join(artifacts, logName), output);
  assert.equal(result.timedOut, false, `${command} exceeded deadline; see owned-tree cleanup log in ${artifacts}`);
  assert.equal(result.status, 0, `${command} failed; see ${logName ? path.join(artifacts, logName) : artifacts}: ${result.error ?? ""}`);
  return result.stdout;
}
const entry = path.join(workspace, "main.ts");
const project = path.join(workspace, "native-project");
const publish = path.join(workspace, "native");
await execute(dotnet, [compiler, "--compile", entry, "--ref-asm", "--debug", "--verify",
  "--emit-native-project", project, "--runtime", "win-x64"], "export.log");
await execute(dotnet, ["publish", path.join(project, "NativeApp.csproj"), "-c", "Debug", "-r", "win-x64", "-o", publish,
  "--disable-build-servers", "-m:1", "-nr:false"], "publish.log");
const program = path.join(publish, "NativeApp.exe");
const nativePdb = path.join(publish, "NativeApp.pdb");
const image = await fs.readFile(program);
const pe = image.readUInt32LE(0x3c);
assert.equal(image.readUInt32LE(pe), 0x4550);
assert.equal(image.readUInt16LE(pe + 4), 0x8664);
assert.equal(image.readUInt16LE(pe + 24), 0x20b);
assert.equal(image.readUInt32LE(pe + 24 + 112 + 14 * 8), 0, "native executable has no CLR header");
assert.ok((await fs.stat(nativePdb)).size > 0);
const nativeEnv = { ...toolEnv, Path: "" };
for (const name of Object.keys(nativeEnv)) if (/^DOTNET_ROOT(?:_|$)|^DOTNET_HOST_PATH$/i.test(name)) delete nativeEnv[name];
const expectedOutput = (await execute(program, ["40"], "standalone.log", { env: nativeEnv })).replaceAll("\r\n", "\n");
assert.equal(expectedOutput, "native-debug=42\n");
let cli = values.get("code-cli");
if (!cli) {
  const install = path.dirname(code);
  const candidates = [path.join(install, "resources/app/out/cli.js")];
  for (const item of await fs.readdir(install, { withFileTypes: true }))
    if (item.isDirectory()) candidates.push(path.join(install, item.name, "resources/app/out/cli.js"));
  const found = [];
  for (const candidate of candidates) try { await fs.access(candidate); found.push(candidate); } catch {}
  assert.equal(found.length, 1, "pass --code-cli if the VS Code installation is ambiguous");
  cli = found[0];
}
await execute(code, [cli, "--user-data-dir", userData, "--extensions-dir", extensions, "--list-extensions"], "extensions.log",
  { env: { ...toolEnv, ELECTRON_RUN_AS_NODE: "1" } });
const config = { workspace, entry, program, nativePdb, markers, expectedOutput,
  cppVersion: manifest.version, result: path.join(artifacts, "extension-result.json"),
  transcript: path.join(artifacts, "transcript.json") };
const configFile = path.join(artifacts, "config.json");
await fs.writeFile(configFile, JSON.stringify(config, null, 2) + "\n");
const env = { ...toolEnv, SHARPTS_NATIVE_DEBUGGER_CONFIG: configFile };
delete env.ELECTRON_RUN_AS_NODE;
const editor = await runOwned(code, ["--user-data-dir", userData, "--extensions-dir", extensions,
  "--extensionDevelopmentPath", script, "--extensionTestsPath", path.join(script, "test.cjs"),
  "--disable-workspace-trust", "--skip-welcome", "--skip-release-notes", "--disable-updates",
  "--disable-telemetry", "--disable-gpu", "--new-window", workspace],
  { cwd: root, env, timeout: 180_000 });
await fs.writeFile(path.join(artifacts, "extension-host.log"), `${editor.stdout}\n${editor.stderr}\n${editor.error ?? ""}\n${editor.cleanupError ?? ""}`);
assert.equal(editor.timedOut, false, `isolated editor exceeded deadline; see ${artifacts}`);
assert.equal(editor.status, 0, `isolated editor failed; see ${artifacts}`);
const result = JSON.parse(await fs.readFile(config.result, "utf8"));
assert.equal(result.status, "passed");
assert.equal(await hash(compiler), compilerSha256, "compiler changed during acceptance");
for (const [name, expected] of Object.entries(harnessHashes))
  assert.equal(await hash(path.join(script, name)), expected, "harness changed during acceptance");
for (const [name, expected] of Object.entries(sourceHashes)) {
  assert.equal(await hash(path.join(root, "tests/fixtures/NativeGuestDebuggerAcceptance", name)), expected,
    "original TypeScript source changed during acceptance");
  assert.equal(await hash(path.join(workspace, name)), expected,
    "compiled/debugged TypeScript source changed during acceptance");
}
Object.assign(result, { artifacts, compilerSha256, sourceHashes, cppVersion: manifest.version,
  harnessHashes,
  guestSha256: await hash(path.join(project, "guest/Guest.dll")),
  guestPdbSha256: await hash(path.join(project, "guest/Guest.pdb")),
  nativeSha256: await hash(program), nativePdbSha256: await hash(nativePdb),
  nativeHeaderVerified: true, standaloneNoDotNetOutput: expectedOutput,
  verifiedUtc: new Date().toISOString(), sourceCommit: (await execute("git", ["rev-parse", "HEAD"], null, { cwd: root })).trim() });
await fs.writeFile(path.join(artifacts, "result.json"), JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify({ status: result.status, editor: result.editor,
  cppVersion: manifest.version, stops: result.observations.length, artifacts }, null, 2));
