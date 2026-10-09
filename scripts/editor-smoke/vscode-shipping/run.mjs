import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const script = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(script, "../../..");
const values = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  assert.ok(process.argv[i]?.startsWith("--") && process.argv[i + 1], "use --name value pairs");
  values.set(process.argv[i].slice(2), process.argv[i + 1]);
}
const packageFile = path.resolve(root, values.get("package") ?? "artifacts/editor-shipping/vscode/package/package.json");
const packaged = JSON.parse(await fs.readFile(packageFile, "utf8"));
const hash = async (file) => createHash("sha256").update(await fs.readFile(file)).digest("hex");
assert.equal(await hash(packaged.vsix), packaged.sha256);
const code = values.get("code") ?? (process.platform === "win32"
  ? path.join(process.env.LOCALAPPDATA, "Programs/Microsoft VS Code/Code.exe") : "code");
let cli = values.get("code-cli");
if (process.platform === "win32" && !cli) {
  const install = path.dirname(code);
  const candidates = [path.join(install, "resources/app/out/cli.js")];
  for (const entry of await fs.readdir(install, { withFileTypes: true }))
    if (entry.isDirectory()) candidates.push(path.join(install, entry.name, "resources/app/out/cli.js"));
  const existing = [];
  for (const candidate of candidates) try { await fs.access(candidate); existing.push(candidate); } catch {}
  assert.equal(existing.length, 1, "use --code-cli for an ambiguous VS Code install");
  cli = existing[0];
}
const base = path.resolve(root, values.get("artifacts") ?? "artifacts/editor-shipping/vscode/runs");
await fs.mkdir(base, { recursive: true });
const artifacts = await fs.mkdtemp(path.join(base, "run-"));
const userData = path.join(artifacts, "user-data");
const extensions = path.join(artifacts, "extensions");
await fs.mkdir(extensions, { recursive: true });
const prettierExtension = path.resolve(root, values.get("prettier-extension")
  ?? "artifacts/formatter-interop/vscode/extensions/esbenp.prettier-vscode-12.4.0");
const prettierManifest = JSON.parse(await fs.readFile(path.join(prettierExtension, "package.json"), "utf8"));
assert.equal(prettierManifest.publisher + "." + prettierManifest.name, "esbenp.prettier-vscode");
assert.equal(prettierManifest.version, "12.4.0");
await fs.cp(prettierExtension, path.join(extensions, `esbenp.prettier-vscode-${prettierManifest.version}`), { recursive: true });
// Populate the fresh extension directory before the CLI creates its profile's
// installed-extension inventory. Later directory copies are not auto-installed.
const installArgs = ["--user-data-dir", userData, "--extensions-dir", extensions,
  "--install-extension", packaged.vsix, "--force"];
const installed = spawnSync(code, cli ? [cli, ...installArgs] : installArgs,
  { env: cli ? { ...process.env, ELECTRON_RUN_AS_NODE: "1" } : process.env,
    encoding: "utf8", timeout: 60_000, windowsHide: true });
await fs.writeFile(path.join(artifacts, "install.log"), installed.stdout + installed.stderr);
assert.equal(installed.status, 0, installed.stdout + installed.stderr);
const formatterManifest = JSON.parse(await fs.readFile(path.join(root, "tools/formatter-interop/package.json"), "utf8"));
const source = spawnSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8", windowsHide: true });
assert.equal(source.status, 0, source.stderr);
const config = { root, workspace: path.join(artifacts, "workspace"),
  fixture: path.join(artifacts, "workspace/main.ts"), dependency: path.join(artifacts, "workspace/dep.ts"),
  result: path.join(artifacts, "extension-result.json"), package: packaged,
  dotnet: values.get("dotnet") ?? "dotnet", sourceCommit: source.stdout.trim(),
  prettierPath: path.join(root, "tools/formatter-interop/node_modules/prettier"),
  prettierVersion: formatterManifest.devDependencies.prettier, prettierExtensionVersion: prettierManifest.version };
await fs.mkdir(path.join(config.workspace, ".vscode"), { recursive: true });
await fs.writeFile(config.fixture, 'import { ordinary } from "./dep";\n@DotNetType("System.Text.StringBuilder")\ndeclare class Builder{constructor();Append(value:string):Builder;}\nconst message={text:"hello",count:1};\nconst ordinaryUse=ordinary;\n');
await fs.writeFile(config.dependency, "export const ordinary: number = 123;\n");
await fs.writeFile(path.join(config.workspace, "tsconfig.json"), JSON.stringify({ compilerOptions: { experimentalDecorators: true, noEmit: true } }) + "\n");
await fs.writeFile(path.join(config.workspace, ".prettierrc.json"), JSON.stringify({ semi: true, singleQuote: false, endOfLine: "lf" }) + "\n");
await fs.writeFile(path.join(config.workspace, ".vscode/settings.json"), JSON.stringify({
  "sharpts.dotnetPath": config.dotnet, "sharpts.trace.server": "verbose",
  "editor.formatOnSave": true, "editor.formatOnSaveMode": "file", "editor.defaultFormatter": "esbenp.prettier-vscode",
  "editor.codeActionsOnSave": {}, "prettier.prettierPath": config.prettierPath, "prettier.requireConfig": true,
  "typescript.format.enable": false, "files.autoSave": "off", "security.workspace.trust.enabled": false,
  "update.mode": "none", "extensions.autoCheckUpdates": false, "extensions.autoUpdate": false,
  "telemetry.telemetryLevel": "off",
}, null, 2) + "\n");
const configFile = path.join(artifacts, "test-config.json");
await fs.writeFile(configFile, JSON.stringify(config, null, 2) + "\n");
const env = { ...process.env, SHARPTS_SHIPPING_SMOKE_CONFIG: configFile };
delete env.ELECTRON_RUN_AS_NODE;
const child = spawn(code, ["--user-data-dir", userData, "--extensions-dir", extensions,
  "--extensionDevelopmentPath", script, "--extensionTestsPath", path.join(script, "test.cjs"),
  "--disable-workspace-trust", "--skip-welcome", "--skip-release-notes", "--disable-updates",
  "--disable-telemetry", "--disable-gpu", "--new-window", config.workspace],
  { cwd: root, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let log = "";
child.stdout.on("data", (chunk) => { log += chunk; });
child.stderr.on("data", (chunk) => { log += chunk; });
let timedOut = false;
const timer = setTimeout(() => {
  timedOut = true;
  if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true });
  else child.kill("SIGKILL");
}, 90_000);
const exitCode = await new Promise((resolve, reject) => { child.once("error", reject); child.once("exit", resolve); })
  .finally(() => clearTimeout(timer));
await fs.writeFile(path.join(artifacts, "extension-host.log"), log);
assert.equal(timedOut, false, `VS Code exceeded 90 seconds; see ${artifacts}`);
assert.equal(exitCode, 0, `VS Code failed; see ${artifacts}/extension-host.log`);
const result = JSON.parse(await fs.readFile(config.result, "utf8"));
assert.equal(result.status, "passed");
async function logFiles(directory) {
  const files = [];
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    const target = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await logFiles(target));
    else if (entry.isFile() && entry.name.endsWith(".log")) files.push(target);
  }
  return files;
}
const logs = await logFiles(path.join(userData, "logs"));
const normalize = (value) => process.platform === "win32" ? path.resolve(value).toLowerCase() : path.resolve(value);
let instances = 0;
const formatterLogs = [];
for (const file of logs.filter((file) => file.endsWith("-Prettier.log"))) {
  const text = await fs.readFile(file, "utf8");
  for (const match of text.matchAll(/PrettierInstance:\r?\n(\{[\s\S]*?\r?\n\})/g)) {
    const instance = JSON.parse(match[1]);
    assert.equal(instance.version, config.prettierVersion);
    assert.equal(normalize(instance.modulePath), normalize(config.prettierPath));
    instances++;
  }
  formatterLogs.push(file);
}
assert.ok(instances >= 2, "both saves must log the pinned local formatter instance");
// Parse real LanguageClient trace JSON, including dynamic registration. VS Code
// advertises dynamic support, so initialize alone does not describe its providers.
function payload(text, start, prefix) {
  const from = text.indexOf(prefix, start);
  assert.ok(from >= 0, `missing trace ${prefix}`);
  const begin = from + prefix.length;
  let depth = 0, quoted = false, escaped = false;
  for (let index = begin; index < text.length; index++) {
    const char = text[index];
    if (quoted) {
      if (escaped) escaped = false;
      else if (char === "\\") escaped = true;
      else if (char === '"') quoted = false;
    } else if (char === '"') quoted = true;
    else if (char === "{" || char === "[") depth++;
    else if ((char === "}" || char === "]") && --depth === 0)
      return JSON.parse(text.slice(begin, index + 1));
  }
  assert.fail("unterminated trace JSON");
}
const serverLogs = logs.filter((file) => file.endsWith("-SharpTS Language Server.log"));
assert.equal(serverLogs.length, 1, "exactly one shipping SharpTS LanguageClient must be active");
const trace = await fs.readFile(serverLogs[0], "utf8");
const initialize = trace.indexOf("Received response 'initialize - (0)'");
assert.ok(initialize >= 0, "shipping client must initialize its installed server");
const initialized = payload(trace, initialize, "Result: ");
const registeredMethods = [];
for (const match of trace.matchAll(/Received request 'client\/registerCapability - \(\d+\)'/g))
  registeredMethods.push(...payload(trace, match.index, "Params: ").registrations.map((item) => item.method));
for (const method of ["textDocument/hover", "textDocument/completion", "textDocument/signatureHelp"])
  assert.ok(registeredMethods.includes(method), `shipping interop client must register ${method}`);
for (const provider of ["definitionProvider", "referencesProvider", "renameProvider", "documentSymbolProvider",
  "documentFormattingProvider", "documentRangeFormattingProvider", "documentOnTypeFormattingProvider"])
  assert.ok(!initialized.capabilities[provider], `shipping mode must not advertise ${provider}`);
assert.ok(!registeredMethods.some((method) => /^textDocument\/(definition|references|rename|documentSymbol|formatting|rangeFormatting|onTypeFormatting)$/.test(method)),
  "shipping client must not dynamically claim ordinary navigation or formatting");
const successfulHovers = [...trace.matchAll(/Received response 'textDocument\/hover - \(\d+\)'[^\n]*\r?\nResult: /g)].length;
assert.ok(successfulHovers >= 6, "decorator and CLR member hovers must obtain fresh real server responses across both saves");
Object.assign(result, { formatterModuleVerified: true, formatterInstances: instances, formatterLogs,
  serverInfo: initialized.serverInfo, initializedCapabilities: initialized.capabilities,
  registeredMethods: [...new Set(registeredMethods)].sort(), successfulSharpTsHovers: successfulHovers, serverLog: serverLogs[0],
  artifacts, installLog: path.join(artifacts, "install.log"), editorLogFiles: logs });
await fs.writeFile(path.join(artifacts, "result.json"), JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
