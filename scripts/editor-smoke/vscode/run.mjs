import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDirectory, "../../..");
const values = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  assert.ok(process.argv[index]?.startsWith("--"), "arguments must be --name value pairs");
  assert.ok(process.argv[index + 1], `missing value for ${process.argv[index]}`);
  values.set(process.argv[index].slice(2), process.argv[index + 1]);
}
const artifacts = path.resolve(root, values.get("artifacts") ?? "artifacts/formatter-interop/vscode");
const code = values.get("code") ?? (process.platform === "win32"
  ? path.join(process.env.LOCALAPPDATA, "Programs/Microsoft VS Code/Code.exe")
  : "code");
const config = {
  root,
  workspace: path.join(artifacts, "workspace"),
  fixture: path.join(artifacts, "workspace/format-on-save.ts"),
  result: path.join(artifacts, "extension-result.json"),
  server: path.resolve(root, values.get("server") ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll"),
  dotnet: values.get("dotnet") ?? "dotnet",
  prettierPath: path.join(root, "tools/formatter-interop/node_modules/prettier"),
  prettierVersion: "3.9.9",
  extensionVersion: "12.4.0",
};
await fs.access(config.server);
await fs.access(path.join(config.prettierPath, "package.json"));
await fs.mkdir(path.join(config.workspace, ".vscode"), { recursive: true });
await fs.rm(config.result, { force: true });
const finalResult = path.join(artifacts, "result.json");
await fs.rm(finalResult, { force: true });
await fs.writeFile(config.fixture,
  '@DotNetType("System.Text.StringBuilder")\ndeclare class Builder{constructor();Append(value:string):Builder;}\nconst message={text:"hello",count:1};\n');
await fs.writeFile(path.join(config.workspace, ".prettierrc.json"),
  JSON.stringify({ semi: true, singleQuote: false, endOfLine: "lf" }) + "\n");
await fs.writeFile(path.join(config.workspace, ".vscode/settings.json"),
  JSON.stringify({
    "editor.formatOnSave": true,
    "editor.formatOnSaveMode": "file",
    "editor.defaultFormatter": "esbenp.prettier-vscode",
    "editor.codeActionsOnSave": {},
    "prettier.prettierPath": config.prettierPath,
    "prettier.requireConfig": true,
    "typescript.format.enable": false,
    "files.autoSave": "off",
    "security.workspace.trust.enabled": false,
    "update.mode": "none",
    "extensions.autoCheckUpdates": false,
    "extensions.autoUpdate": false,
    "telemetry.telemetryLevel": "off",
  }, null, 2) + "\n");
const configPath = path.join(artifacts, "test-config.json");
await fs.writeFile(configPath, JSON.stringify(config, null, 2) + "\n");

const args = [
  "--user-data-dir", path.join(artifacts, "user-data"),
  "--extensions-dir", path.join(artifacts, "extensions"),
  "--extensionDevelopmentPath", scriptDirectory,
  "--extensionTestsPath", path.join(scriptDirectory, "test.cjs"),
  "--disable-workspace-trust", "--skip-welcome", "--skip-release-notes",
  "--disable-updates", "--disable-telemetry", "--disable-gpu", "--new-window",
  config.workspace,
];
const env = { ...process.env, SHARPTS_EDITOR_SMOKE_CONFIG: configPath };
delete env.ELECTRON_RUN_AS_NODE;
const startedAt = Date.now();
const child = spawn(code, args, { cwd: root, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let log = "";
child.stdout.on("data", (chunk) => { log += chunk; });
child.stderr.on("data", (chunk) => { log += chunk; });
let timedOut = false;
const timer = setTimeout(() => {
  timedOut = true;
  if (process.platform === "win32") {
    spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true });
  } else child.kill("SIGKILL");
}, 45000);
const exitCode = await new Promise((resolve, reject) => {
  child.once("error", reject);
  child.once("exit", resolve);
}).finally(() => clearTimeout(timer));
await fs.writeFile(path.join(artifacts, "extension-host.log"), log);
assert.equal(timedOut, false, `VS Code editor smoke exceeded 45 seconds; see ${artifacts}`);
assert.equal(exitCode, 0, `VS Code editor smoke failed; see ${path.join(artifacts, "extension-host.log")}`);
const result = JSON.parse(await fs.readFile(config.result, "utf8"));
assert.equal(result.status, "passed");

// Equal output alone could also come from the extension's bundled formatter.
// Verify the real pinned extension logged the requested local module/version
// during this run, rather than silently falling back to its bundled Prettier.
async function prettierLogs(directory) {
  const found = [];
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) found.push(...await prettierLogs(entryPath));
    else if (entry.isFile() && entry.name.endsWith("-Prettier.log")) {
      if ((await fs.stat(entryPath)).mtimeMs >= startedAt) found.push(entryPath);
    }
  }
  return found;
}
const logPaths = await prettierLogs(path.join(artifacts, "user-data/logs"));
const normalizeModulePath = (value) => process.platform === "win32"
  ? path.resolve(value).toLowerCase()
  : path.resolve(value);
let verifiedInstances = 0;
for (const logPath of logPaths) {
  const text = await fs.readFile(logPath, "utf8");
  for (const match of text.matchAll(/PrettierInstance:\r?\n(\{[\s\S]*?\r?\n\})/g)) {
    const instance = JSON.parse(match[1]);
    assert.equal(instance.version, config.prettierVersion, "editor formatter version must be pinned");
    assert.equal(normalizeModulePath(instance.modulePath), normalizeModulePath(config.prettierPath),
      "editor must use the pinned local formatter module");
    verifiedInstances++;
  }
}
assert.ok(verifiedInstances >= 2, "both saves must log the actual pinned formatter instance");
result.formatterModuleVerified = true;
result.prettierExtensionLogs = logPaths;
await fs.writeFile(finalResult, JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
