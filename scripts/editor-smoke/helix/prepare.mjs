import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const options = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  assert.ok(process.argv[index]?.startsWith("--") && process.argv[index + 1], "use --name value pairs");
  options.set(process.argv[index].slice(2), process.argv[index + 1]);
}
const mode = options.get("mode") ?? "full";
assert.ok(["full", "interop-only"].includes(mode));
const hx = path.resolve(root, options.get("hx") ?? "artifacts/editor-tools/helix-25.07.1-x86_64-windows/hx.exe");
const server = path.resolve(root, options.get("server") ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
const workspace = path.resolve(root, options.get("artifacts") ?? `artifacts/formatter-interop/helix/${mode}`);
const prettier = path.join(root, "tools/formatter-interop/node_modules/prettier/bin/prettier.cjs");
await fs.access(hx);
await fs.access(server);
const pin = JSON.parse(await fs.readFile(path.join(root, "tools/formatter-interop/package.json"), "utf8")).devDependencies.prettier;
const installed = spawnSync(process.execPath, [prettier, "--version"], { encoding: "utf8", windowsHide: true, timeout: 10000 });
assert.equal(installed.status, 0, installed.stderr);
const prettierVersion = installed.stdout.trim();
assert.equal(prettierVersion, pin, "installed formatter differs from the exact repository pin");
await fs.mkdir(path.join(workspace, ".helix"), { recursive: true });
await fs.writeFile(path.join(workspace, ".prettierrc.json"), '{"semi":true,"endOfLine":"lf"}\n');
// Bound GUI metadata discovery to this disposable fixture instead of user ancestors.
await fs.writeFile(path.join(workspace, "Smoke.csproj"), '<Project Sdk="SharpTS.Gui.Sdk/0.0.0" />\n');
await fs.mkdir(path.join(workspace, "obj/@sharpts/gui"), { recursive: true });
await fs.writeFile(path.join(workspace, "obj/@sharpts/gui/control-docs.generated.json"), '{"controls":[]}\n');
const tomlString = (value) => JSON.stringify(value.replaceAll("\\", "/"));
const formatter = `{ command = "node", args = [${tomlString(prettier)}, "--stdin-filepath", "%{buffer_name}"] }`;
await fs.writeFile(path.join(workspace, ".helix/languages.toml"), `
[language-server.sharpts]
command = "dotnet"
args = [${tomlString(server)}, "--language-features", "${mode}", "--diagnostics", "${mode === "full" ? "all" : "sharpts-only"}"]

[[language]]
name = "typescript"
language-servers = ["sharpts"]
auto-format = true
formatter = ${formatter}

[[language]]
name = "tsx"
language-servers = ["sharpts"]
auto-format = true
formatter = ${formatter}
`);
await fs.writeFile(path.join(workspace, "config.toml"), '[editor]\nauto-pairs = false\n');
const fixtures = [];
for (const extension of ["ts", "tsx"]) {
  const file = path.join(workspace, `format-on-save.${extension}`);
  const original = '@DotNetType("System.Text.StringBuilder")\ndeclare class Builder{constructor();append(value:string):Builder;}\nconst message={text:"original",count:1};\n'
    + (extension === "tsx" ? 'declare function Panel(props:{title:string}):any;\nconst view=<Panel title="hello"/>;\n' : "");
  const edited = original.replace('text:"original"', 'text:"edited"');
  const format = spawnSync(process.execPath, [prettier, "--stdin-filepath", file], {
    input: edited, cwd: workspace, encoding: "utf8", windowsHide: true, timeout: 10000,
  });
  assert.equal(format.status, 0, format.stderr);
  assert.notEqual(edited, format.stdout);
  await fs.writeFile(file, original);
  fixtures.push({ file, edited, expected: format.stdout });
}
const version = spawnSync(hx, ["--version"], { encoding: "utf8", windowsHide: true });
assert.equal(version.status, 0, version.stderr);
const config = {
  mode, workspace, hx, server, fixtures, prettierVersion, version: version.stdout.trim(),
  runtime: path.join(path.dirname(hx), "runtime"),
  log: path.join(workspace, "helix.log"),
  editorConfig: path.join(workspace, "config.toml"),
};
await fs.rm(config.log, { force: true });
await fs.writeFile(path.join(workspace, "test-config.json"), JSON.stringify(config, null, 2) + "\n");
console.log(JSON.stringify(config, null, 2));
