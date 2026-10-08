import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const hx = path.resolve(process.argv[2] ?? path.join(root, "artifacts/editor-tools/helix-25.07.1-x86_64-windows/hx.exe"));
const node = process.execPath;
const server = path.join(root, "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
const prettier = path.join(root, "tools/formatter-interop/node_modules/prettier/bin/prettier.cjs");
for (const file of [hx, server, prettier]) await fs.access(file);
const parent = path.join(root, "artifacts/editor-contract/helix");
await fs.mkdir(parent, { recursive: true });
const workspace = await fs.mkdtemp(path.join(parent, "run-"));
const execute = (command, args, options = {}) => {
  const result = spawnSync(command, args, { encoding: "utf8", windowsHide: true, timeout: 15000, ...options });
  assert.equal(result.status, 0, result.stderr || result.error?.message);
  return result.stdout.trim();
};
const formatterVersion = execute(node, [prettier, "--version"]);
assert.equal(formatterVersion, JSON.parse(await fs.readFile(path.join(root, "tools/formatter-interop/package.json"), "utf8")).devDependencies.prettier);
const initial = {
  "dependency.ts": "export class Counter {\n  #secret: number = 1;\n  value: number = 2;\n  add(left: number, right: number): number { return this.#secret + left + right; }\n}\n",
  "main.ts": "import { Counter } from './dependency';\nconst counter = new Counter();\nconst result = counter.add(1, 2);\nconst lexical = result;\nlexical;\n",
  "view.tsx": 'declare const React: any;\ndeclare function Panel(props: { title: string }): any;\nconst title = "before";\nconst view = <Panel title={title}/>;\n',
};
for (const [name, text] of Object.entries(initial)) await fs.writeFile(path.join(workspace, name), text);
const stages = {
  "dependency-dirty": initial["dependency.ts"].replaceAll("#secret", "#renamedSecret").replace("  value:", "  extra: number = 4;\n  value:"),
  "main-completion": initial["main.ts"] + "counter.\n",
  "main-valid": initial["main.ts"].replace("const result =", "const result=").replace("add(1, 2)", "add(1,2)"),
  "main-bad": initial["main.ts"] + 'const bad: number = "wrong";\n',
  "view-dirty": initial["view.tsx"].replace('title = "before"', 'title="after"'),
};
for (const [name, text] of Object.entries(stages)) await fs.writeFile(path.join(workspace, `${name}.txt`), text);
// The native :pipe command replaces Helix's selected buffer with stdout, keeping
// edits unsaved and sending the editor's own didChange. It avoids terminal paste
// and auto-indent differences; this helper never launches or speaks to the LSP.
await fs.writeFile(path.join(workspace, "stage.cjs"), 'const fs = require("node:fs"); process.stdout.write(fs.readFileSync(process.argv[2] + ".txt", "utf8"));\n');
await fs.writeFile(path.join(workspace, "tsconfig.json"), JSON.stringify({ compilerOptions: { noLib: true, types: [], jsx: "react" }, include: ["*.ts", "*.tsx"] }) + "\n");
await fs.writeFile(path.join(workspace, ".prettierrc.json"), '{"semi":true,"endOfLine":"lf"}\n');
await fs.writeFile(path.join(workspace, "Smoke.csproj"), '<Project Sdk="SharpTS.Gui.Sdk/0.0.0" />\n');
await fs.mkdir(path.join(workspace, "obj/@sharpts/gui"), { recursive: true });
await fs.writeFile(path.join(workspace, "obj/@sharpts/gui/control-docs.generated.json"), '{"controls":[]}\n');
await fs.mkdir(path.join(workspace, ".helix"));
const toml = (value) => JSON.stringify(value.replaceAll("\\", "/"));
await fs.writeFile(path.join(workspace, ".helix/languages.toml"), `[language-server.sharpts]
command = "dotnet"
args = [${toml(server)}, "--language-features", "full", "--diagnostics", "all"]

[[language]]
name = "typescript"
language-servers = ["sharpts"]
auto-format = true
formatter = { command = ${toml(node)}, args = [${toml(prettier)}, "--stdin-filepath", "%{buffer_name}"] }

[[language]]
name = "tsx"
language-servers = ["sharpts"]
auto-format = true
formatter = { command = ${toml(node)}, args = [${toml(prettier)}, "--stdin-filepath", "%{buffer_name}"] }
`);
await fs.writeFile(path.join(workspace, "config.toml"), '[editor]\nauto-pairs = false\nauto-completion = false\n\n[keys.insert]\nC-k = "signature_help"\n');
const config = {
  workspace, hx, runtime: path.join(path.dirname(hx), "runtime"), server, node,
  editorConfig: path.join(workspace, "config.toml"), log: path.join(workspace, "helix.log"),
  editor: execute(hx, ["--version"]), formatter: { version: formatterVersion, executable: node, module: prettier },
  commit: execute("git", ["rev-parse", "HEAD"], { cwd: root }),
  serverSha256: createHash("sha256").update(await fs.readFile(server)).digest("hex"),
  platform: process.platform, architecture: process.arch, nodeVersion: process.version, initial, stages,
};
await fs.writeFile(path.join(workspace, "test-config.json"), JSON.stringify(config, null, 2) + "\n");
console.log(JSON.stringify({ workspace, editor: config.editor, log: config.log, editorConfig: config.editorConfig, runtime: config.runtime }, null, 2));
