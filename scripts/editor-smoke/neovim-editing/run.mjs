import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, "../../..");
const options = new Map();
for (let index = 2; index < process.argv.length; index += 2) {
  assert.ok(process.argv[index]?.startsWith("--") && process.argv[index + 1], "use --name value pairs");
  options.set(process.argv[index].slice(2), process.argv[index + 1]);
}
const nvim = options.has("nvim") ? path.resolve(options.get("nvim"))
  : process.platform === "win32" ? path.join(root, "artifacts/editor-tools/nvim-win-arm64/bin/nvim.exe") : "nvim";
const server = path.resolve(root, options.get("server") ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
const core = path.join(path.dirname(server), "SharpTS.dll");
const dotnet = options.get("dotnet") ?? "dotnet";
const toolRoot = path.join(root, "tools/editor-interop");
const typescriptCli = path.join(toolRoot, "node_modules/typescript-language-server/lib/cli.mjs");
const tsserver = path.join(toolRoot, "node_modules/typescript/lib/tsserver.js");
const prettier = path.join(root, "tools/formatter-interop/node_modules/prettier/bin/prettier.cjs");
const artifacts = path.resolve(root, options.get("artifacts") ?? "artifacts/editor-editing/neovim");
const hash = (text) => createHash("sha256").update(text).digest("hex");
function output(command, args, input) {
  const child = spawnSync(command, args, { encoding: "utf8", input, cwd: root, windowsHide: true, timeout: 15_000 });
  assert.equal(child.error, undefined, child.error?.message);
  assert.equal(child.status, 0, child.stderr || child.stdout);
  return child.stdout.trimEnd();
}
for (const filename of [server, core, typescriptCli, tsserver, prettier]) await fs.access(filename);
const pins = JSON.parse(await fs.readFile(path.join(toolRoot, "package.json"), "utf8")).devDependencies;
const actualAdapter = output(process.execPath, [typescriptCli, "--version"]);
const actualTypescript = JSON.parse(await fs.readFile(path.join(toolRoot, "node_modules/typescript/package.json"), "utf8")).version;
assert.equal(actualAdapter, pins["typescript-language-server"], "Run npm ci --prefix tools/editor-interop");
assert.equal(actualTypescript, pins.typescript, "Run npm ci --prefix tools/editor-interop");
const prettierPin = JSON.parse(await fs.readFile(path.join(root, "tools/formatter-interop/package.json"), "utf8")).devDependencies.prettier;
assert.equal(output(process.execPath, [prettier, "--version"]), prettierPin, "Run npm ci --prefix tools/formatter-interop");
const editorVersion = output(nvim, ["--version"]).split(/\r?\n/)[0];
const formattingDoc = await fs.readFile(path.join(root, "docs/formatting.md"), "utf8");
const saveRecipe = formattingDoc.match(/```lua\r?\n([\s\S]*?)\r?\n```/)?.[1];
assert.ok(saveRecipe, "Missing documented Neovim save recipe");
await fs.mkdir(artifacts, { recursive: true });
const runDirectory = await fs.mkdtemp(path.join(artifacts, "run-"));
const dependency = "export class Model { /*depDeclaration*/field: number = 1; read(): number { return this./*self*/field; } }\n" +
  "export function pair(value: number, count: number): number { return value + count; }\n";
const main = "import { Model, pair } from './dep';\nconst item = new Model();\nconst /*lexicalDeclaration*/local = 1;\n" +
  "/* 😀 */ item./*member*/field;\n/*lexicalUse*/local;\npair(/*argument*/1, 2);\n";
const tsx = "/** @jsx h */\ndeclare function h(...args: any[]): any;\ndeclare function Panel(props: { title: string }): any;\n" +
  "const /*tsxDeclaration*/tsxValue: number = 1;\n/*tsxUse*/tsxValue;\nconst view=<Panel title=\"hello\"/>;\n";
const clr = "declare function DotNetType(name: string): any;\n/*clr*/@DotNetType(\"System.Text.StringBuilder\")\n" +
  "declare class Builder{constructor();append(value:string):Builder;}\n";
const privateSource = "class Box { /*privateDeclaration*/#old: number = 1; read(): number { return this./*privateUse*/#old; } }\n" +
  "class Other { #old: number = 2; read(): number { return this.#old; } }\n";
const report = {
  timestamp: new Date().toISOString(), platform: `${process.platform}/${process.arch}`,
  sourceCommit: output("git", ["rev-parse", "HEAD"]), sourceDirty: output("git", ["status", "--porcelain"]).length > 0,
  server, serverSha256: hash(await fs.readFile(server)), coreSha256: hash(await fs.readFile(core)),
  node: process.version, dotnet: output(dotnet, ["--version"]), editor: editorVersion,
  prettier: prettierPin, typescriptAdapter: actualAdapter, typescript: actualTypescript,
  harnessSha256: hash(await fs.readFile(path.join(directory, "test.lua"))), results: [],
  runnerSha256: hash(await fs.readFile(fileURLToPath(import.meta.url))),
  adapterLockSha256: hash(await fs.readFile(path.join(toolRoot, "package-lock.json"))),
  formattingRecipeSha256: hash(saveRecipe),
};
try {
  for (const mode of ["full", "coexistence"]) {
    console.log(`Neovim editing: ${mode}`);
    const workspace = path.join(runDirectory, mode);
    await fs.mkdir(workspace);
    await fs.writeFile(path.join(workspace, "tsconfig.json"), JSON.stringify({
      compilerOptions: { noLib: true, types: [], target: "ES2022", jsx: "react", experimentalDecorators: true },
      include: ["*.ts", "*.tsx"],
    }) + "\n");
    await fs.writeFile(path.join(workspace, ".prettierrc.json"), '{"semi":true,"singleQuote":true,"endOfLine":"lf"}\n');
    // Bound GUI discovery to this disposable project; never build/restore the project.
    await fs.writeFile(path.join(workspace, "Smoke.csproj"), '<Project Sdk="SharpTS.Gui.Sdk/0.0.0" />\n');
    await fs.mkdir(path.join(workspace, "obj/@sharpts/gui"), { recursive: true });
    await fs.writeFile(path.join(workspace, "obj/@sharpts/gui/control-docs.generated.json"), '{"controls":[]}\n');
    const files = {};
    for (const [name, text] of Object.entries({ "main.ts": main, "dep.ts": dependency, "view.tsx": tsx, "clr.ts": clr, "private.ts": privateSource })) {
      const filename = path.join(workspace, name);
      await fs.writeFile(filename, text);
      const formatted = spawnSync(process.execPath, [prettier, "--stdin-filepath", filename], {
        input: text, encoding: "utf8", windowsHide: true, timeout: 10_000, cwd: workspace,
      });
      assert.equal(formatted.status, 0, formatted.stderr);
      files[name] = { path: filename, source: text, formatted: formatted.stdout, sha256: hash(text) };
    }
    const recipe = path.join(workspace, "format-on-save.lua");
    await fs.writeFile(recipe, saveRecipe.replaceAll("/absolute/path/to/SharpTS", root.replaceAll("\\", "/")));
    const config = { mode, workspace, server, dotnet, node: process.execPath, typescriptCli, tsserver,
      typescript: actualTypescript, recipe, files, result: path.join(workspace, "result.json") };
    const configPath = path.join(workspace, "test-config.json");
    await fs.writeFile(configPath, JSON.stringify(config, null, 2) + "\n");
    const child = spawn(nvim, ["--headless", "-u", "NONE", "-l", path.join(directory, "test.lua")], {
      cwd: workspace, windowsHide: true, detached: process.platform !== "win32", stdio: ["ignore", "pipe", "pipe"],
      env: { ...process.env, SHARPTS_EDITOR_SMOKE_CONFIG: configPath,
        XDG_CONFIG_HOME: path.join(workspace, "config"), XDG_DATA_HOME: path.join(workspace, "data"),
        XDG_STATE_HOME: path.join(workspace, "state"), XDG_CACHE_HOME: path.join(workspace, "cache"),
        NVIM_APPNAME: "sharpts-editing-smoke", NVIM_LOG_FILE: path.join(workspace, "nvim.log") },
    });
    let log = "";
    let timedOut = false;
    child.stdout.on("data", (chunk) => { log = (log + chunk).slice(-1_000_000); });
    child.stderr.on("data", (chunk) => { log = (log + chunk).slice(-1_000_000); });
    const timer = setTimeout(() => {
      timedOut = true;
      if (process.platform === "win32") spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true });
      else { try { process.kill(-child.pid, "SIGKILL"); } catch { child.kill("SIGKILL"); } }
    }, 120_000);
    let code;
    try { code = await new Promise((resolve, reject) => { child.once("error", reject); child.once("close", resolve); }); }
    finally { clearTimeout(timer); await fs.writeFile(path.join(workspace, "process.log"), log); }
    let result;
    try { result = JSON.parse(await fs.readFile(config.result, "utf8")); }
    catch (error) { throw new Error(`${mode}: editor produced no result: ${log}`, { cause: error }); }
    report.results.push(result);
    assert.equal(timedOut, false, `${mode}: editor exceeded its process deadline`);
    assert.equal(code, 0, `${mode}: ${result.error ?? log}`);
    assert.equal(result.completed, true, JSON.stringify(result));
    console.log(`Neovim editing: ${mode} passed ${result.cases.length} cases`);
  }
  report.completed = true;
} catch (error) {
  report.completed = false;
  report.error = error.message;
  throw error;
} finally {
  await fs.writeFile(path.join(runDirectory, "summary.json"), JSON.stringify(report, null, 2) + "\n");
  console.log(JSON.stringify({ completed: report.completed, report: path.join(runDirectory, "summary.json"),
    error: report.error, modes: report.results.map((result) => result.mode) }, null, 2));
}
