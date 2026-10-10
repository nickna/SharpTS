import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const script = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(script, "../../..");
const values = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  assert.ok(process.argv[index]?.startsWith("--") && process.argv[index + 1], "use --name value pairs");
  values.set(process.argv[index].slice(2), process.argv[index + 1]);
}
const code = values.get("code") ?? (process.platform === "win32"
  ? path.join(process.env.USERPROFILE, "AppData/Local/Programs/Microsoft VS Code/Code.exe") : "code");
const dotnet = values.get("dotnet") ?? "dotnet";
const compiler = path.resolve(root, values.get("compiler") ?? "src/SharpTS/bin/Debug/net10.0/SharpTS.dll");
const hash = async file => createHash("sha256").update(await fs.readFile(file)).digest("hex");
const compilerSha256 = await hash(compiler);
const csharp = path.resolve(values.get("csharp-extension") ?? "");
const runtime = path.resolve(values.get("dotnet-runtime-extension") ?? "");
assert.ok(values.has("csharp-extension") && values.has("dotnet-runtime-extension"),
  "pass existing C# and .NET runtime extension directories; this test never installs marketplace packages");
const base = path.resolve(root, values.get("artifacts") ?? "artifacts/compiled-debugger");
await fs.mkdir(base, { recursive: true });
const artifacts = await fs.mkdtemp(path.join(base, "run-"));
const userData = path.join(artifacts, "user-data");
const extensions = path.join(artifacts, "extensions");
const workspace = path.join(artifacts, "workspace");
await fs.mkdir(extensions, { recursive: true });
await fs.mkdir(path.join(workspace, ".vscode"), { recursive: true });
const extensionVersions = {};
for (const [directory, expected] of [[csharp, "ms-dotnettools.csharp"], [runtime, "ms-dotnettools.vscode-dotnet-runtime"]]) {
  const manifest = JSON.parse(await fs.readFile(path.join(directory, "package.json"), "utf8"));
  assert.equal(`${manifest.publisher}.${manifest.name}`, expected);
  extensionVersions[expected] = manifest.version;
  await fs.cp(directory, path.join(extensions, `${expected}-${manifest.version}`), { recursive: true });
}
const markers = {};
const fixture = path.resolve(root, values.get("fixture") ?? "tests/fixtures/CompiledDebuggerAcceptance");
const existingAssembly = values.has("assembly") ? path.resolve(root, values.get("assembly")) : null;
const sourceNames = existingAssembly
  ? (await fs.readdir(fixture)).filter(name => /\.(cs|ts)$/.test(name))
  : ["main.ts", "helper.ts"];
for (const name of sourceNames) {
  const sourcePath = path.join(fixture, name);
  const source = await fs.readFile(sourcePath, "utf8");
  const destination = existingAssembly ? sourcePath : path.join(workspace, name);
  // Exercise exact-source validation with encodings that text-only hashing loses.
  const bytes = name === "main.ts"
    ? Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from(source, "utf8")])
    : Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(source, "utf16le")]);
  if (!existingAssembly) await fs.writeFile(destination, bytes);
  source.split(/\r?\n/).forEach((line, index) => {
    const marker = line.match(/@(break|step):([\w-]+)/);
    if (marker) markers[`${marker[1]}:${marker[2]}`] = { path: destination, line: index + 1 };
  });
}
const settings = {
  "security.workspace.trust.enabled": false, "files.autoSave": "off",
  "update.mode": "none", "extensions.autoCheckUpdates": false, "extensions.autoUpdate": false,
  "telemetry.telemetryLevel": "off", "dotnet.server.useOmnisharp": false,
};
if (values.has("dotnet")) settings["dotnetAcquisitionExtension.sharedExistingDotnetPath"] = dotnet;
await fs.writeFile(path.join(workspace, ".vscode/settings.json"), JSON.stringify(settings, null, 2) + "\n");
await fs.mkdir(path.join(userData, "User"), { recursive: true });
await fs.writeFile(path.join(userData, "User/settings.json"), JSON.stringify(settings, null, 2) + "\n");
function execute(command, args, options = {}) {
  const result = spawnSync(command, args, { cwd: workspace, encoding: "utf8", timeout: 60_000,
    windowsHide: true, ...options });
  assert.equal(result.status, 0, `${command} ${args.join(" ")}\n${result.error ?? ""}\n${result.stdout}\n${result.stderr}`);
  return result.stdout;
}
const entry = existingAssembly ? path.join(fixture, sourceNames[0]) : path.join(workspace, "main.ts");
const assembly = existingAssembly ?? path.join(workspace, "debug/main.dll");
const plainAssembly = path.join(workspace, "plain/main.dll");
let compileLog = "Precompiled debugger control\n";
if (!existingAssembly) {
  await fs.mkdir(path.dirname(assembly), { recursive: true });
  await fs.mkdir(path.dirname(plainAssembly), { recursive: true });
  compileLog = execute(dotnet, [compiler, "--compile", entry, "--ref-asm", "-g", "-o", assembly]);
  execute(dotnet, [compiler, "--compile", entry, "--ref-asm", "-o", plainAssembly]);
  await assert.rejects(fs.access(plainAssembly.replace(/\.dll$/, ".pdb")));
}
assert.ok((await fs.stat(assembly.replace(/\.dll$/, ".pdb"))).size > 0);
const expectedOutput = execute(dotnet, [existingAssembly ?? plainAssembly]).replaceAll("\r\n", "\n");
assert.equal(execute(dotnet, [assembly]).replaceAll("\r\n", "\n"), expectedOutput);
await fs.writeFile(path.join(artifacts, "compile.log"), compileLog);
let cli = values.get("code-cli");
if (process.platform === "win32" && !cli) {
  const install = path.dirname(code);
  const candidates = [path.join(install, "resources/app/out/cli.js")];
  for (const entry of await fs.readdir(install, { withFileTypes: true }))
    if (entry.isDirectory()) candidates.push(path.join(install, entry.name, "resources/app/out/cli.js"));
  const existing = [];
  for (const candidate of candidates) try { await fs.access(candidate); existing.push(candidate); } catch {}
  assert.equal(existing.length, 1, "pass --code-cli if the VS Code install is ambiguous");
  cli = existing[0];
}
execute(code, cli ? [cli, "--user-data-dir", userData, "--extensions-dir", extensions, "--list-extensions"]
  : ["--user-data-dir", userData, "--extensions-dir", extensions, "--list-extensions"],
  { env: cli ? { ...process.env, ELECTRON_RUN_AS_NODE: "1" } : process.env });
const config = { workspace, entry, assembly, expectedOutput, markers, extensionVersions,
  result: path.join(artifacts, "extension-result.json"), transcript: path.join(artifacts, "transcript.json") };
try { config.expectations = JSON.parse(await fs.readFile(path.join(fixture, "expectations.json"), "utf8")); }
catch (error) { if (error.code !== "ENOENT") throw error; }
config.observeOnly = values.get("observe-only") === "true";
const configFile = path.join(artifacts, "config.json");
await fs.writeFile(configFile, JSON.stringify(config, null, 2) + "\n");
const env = { ...process.env, SHARPTS_COMPILED_DEBUGGER_CONFIG: configFile };
delete env.ELECTRON_RUN_AS_NODE;
const child = spawn(code, ["--user-data-dir", userData, "--extensions-dir", extensions,
  "--extensionDevelopmentPath", script, "--extensionTestsPath", path.resolve(script, values.get("tests") ?? "test.cjs"),
  "--disable-workspace-trust", "--skip-welcome", "--skip-release-notes", "--disable-updates",
  "--disable-telemetry", "--disable-gpu", "--new-window", workspace],
  { cwd: root, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let log = "";
child.stdout.on("data", chunk => { log += chunk; });
child.stderr.on("data", chunk => { log += chunk; });
let timedOut = false;
const timer = setTimeout(() => {
  timedOut = true;
  if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true });
  else child.kill("SIGKILL");
}, 180_000);
const exitCode = await new Promise((resolve, reject) => {
  child.once("error", reject); child.once("exit", resolve);
}).finally(() => clearTimeout(timer));
await fs.writeFile(path.join(artifacts, "extension-host.log"), log);
assert.equal(timedOut, false, `VS Code exceeded 180 seconds; see ${artifacts}`);
assert.equal(exitCode, 0, `VS Code failed; see ${artifacts}`);
const result = JSON.parse(await fs.readFile(config.result, "utf8"));
assert.equal(result.status, "passed");
assert.equal(await hash(compiler), compilerSha256, "compiler changed during acceptance; rerun without a concurrent build");
Object.assign(result, { compilerSha256, assemblySha256: await hash(assembly),
  pdbSha256: await hash(assembly.replace(/\.dll$/, ".pdb")), extensionVersions,
  debugAndNonDebugOutputMatch: existingAssembly ? null : true,
  nonDebugPdbAbsent: existingAssembly ? null : true, referenceAssemblyRewrite: !existingAssembly,
  expectedOutput, artifacts,
  verifiedUtc: new Date().toISOString(),
  sourceCommit: execute("git", ["rev-parse", "HEAD"], { cwd: root }).trim(),
  workingTreeChangesIncluded: Boolean(execute("git", ["status", "--porcelain"], { cwd: root }).trim()),
  sourceEncodings: existingAssembly ? null
    : { "main.ts": "UTF-8 with BOM", "helper.ts": "UTF-16 LE with BOM" } });
await fs.writeFile(path.join(artifacts, "result.json"), JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify({ status: result.status, editor: result.editor, csharpVersion: result.csharpVersion,
  stops: result.observations.length, debugAndNonDebugOutputMatch: result.debugAndNonDebugOutputMatch,
  artifacts }, null, 2));
