import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
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
const nvim = path.resolve(root, options.get("nvim") ?? "artifacts/editor-tools/nvim-win-arm64/bin/nvim.exe");
const server = path.resolve(root, options.get("server") ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
const artifacts = path.resolve(root, options.get("artifacts") ?? "artifacts/formatter-interop/neovim");
const prettier = path.join(root, "tools/formatter-interop/node_modules/prettier/bin/prettier.cjs");
await fs.access(nvim);
await fs.access(server);
const pin = JSON.parse(await fs.readFile(path.join(root, "tools/formatter-interop/package.json"), "utf8")).devDependencies.prettier;
const installed = spawnSync(process.execPath, [prettier, "--version"], { encoding: "utf8", windowsHide: true, timeout: 10000 });
assert.equal(installed.status, 0, installed.stderr);
const prettierVersion = installed.stdout.trim();
assert.equal(prettierVersion, pin, "installed formatter differs from the exact repository pin");
const markdown = await fs.readFile(path.join(root, "docs/formatting.md"), "utf8");
const recipe = markdown.match(/```lua\r?\n([\s\S]*?)\r?\n```/)?.[1];
assert.ok(recipe, "documentation must include the tested Neovim recipe");
const results = [];
for (const mode of ["full", "interop-only"]) {
  const workspace = path.join(artifacts, mode);
  await fs.mkdir(workspace, { recursive: true });
  // Bound the existing GUI metadata discovery walk to this disposable workspace.
  await fs.writeFile(path.join(workspace, "Smoke.csproj"), '<Project Sdk="SharpTS.Gui.Sdk/0.0.0" />\n');
  const gui = path.join(workspace, "obj/@sharpts/gui");
  await fs.mkdir(gui, { recursive: true });
  await fs.writeFile(path.join(gui, "control-docs.generated.json"), '{"controls":[]}\n');
  await fs.writeFile(path.join(workspace, ".prettierrc.json"), '{"semi":true,"endOfLine":"lf"}\n');
  const recipePath = path.join(workspace, "doc-recipe.lua");
  await fs.writeFile(recipePath, recipe.replaceAll("/absolute/path/to/SharpTS", root.replaceAll("\\", "/")));
  const fixtures = [];
  for (const extension of ["ts", "tsx"]) {
    const file = path.join(workspace, `format-on-save.${extension}`);
    const original = '@DotNetType("System.Text.StringBuilder")\ndeclare class Builder{constructor();append(value:string):Builder;}\nconst message={text:"original",count:1};\n'
      + (extension === "tsx" ? 'const view=<Panel title="hello"/>;\n' : "");
    const edited = original.replace('text:"original"', 'text:"edited"');
    const format = spawnSync(process.execPath, [prettier, "--stdin-filepath", file], {
      input: edited, cwd: workspace, encoding: "utf8", timeout: 10000, windowsHide: true,
    });
    assert.equal(format.status, 0, format.stderr);
    assert.notEqual(edited, format.stdout, "save fixture must require formatting");
    await fs.writeFile(file, original);
    fixtures.push({ file, edited, expected: format.stdout });
  }
  const config = {
    mode, workspace, server, fixtures, recipe: recipePath,
    result: path.join(workspace, "result.json"),
  };
  const configPath = path.join(workspace, "test-config.json");
  await fs.writeFile(configPath, JSON.stringify(config, null, 2));
  await fs.rm(config.result, { force: true });
  const child = spawn(nvim, ["--headless", "-u", "NONE", "-l", path.join(directory, "test.lua")], {
    cwd: workspace,
    env: {
      ...process.env, SHARPTS_EDITOR_SMOKE_CONFIG: configPath,
      XDG_CONFIG_HOME: path.join(workspace, "config"), XDG_DATA_HOME: path.join(workspace, "data"),
      XDG_STATE_HOME: path.join(workspace, "state"), XDG_CACHE_HOME: path.join(workspace, "cache"),
      NVIM_APPNAME: "sharpts-editor-smoke", NVIM_LOG_FILE: path.join(workspace, "nvim.log"),
    },
    windowsHide: true,
    stdio: ["ignore", "pipe", "pipe"],
  });
  let log = "";
  child.stdout.on("data", (chunk) => { log += chunk; });
  child.stderr.on("data", (chunk) => { log += chunk; });
  const timer = setTimeout(() => {
    if (process.platform === "win32") {
      spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { windowsHide: true });
    } else child.kill("SIGKILL");
  }, 30000);
  let code;
  try {
    code = await new Promise((resolve, reject) => {
      child.once("error", reject);
      child.once("exit", resolve);
    });
  } finally { clearTimeout(timer); }
  await fs.writeFile(path.join(workspace, "process.log"), log);
  assert.equal(code, 0, log);
  const result = JSON.parse(await fs.readFile(config.result, "utf8"));
  assert.equal(result.completed, true, JSON.stringify(result));
  for (const fixture of fixtures) {
    assert.equal(await fs.readFile(fixture.file, "utf8"), fixture.expected, fixture.file);
  }
  results.push(result);
}
const summary = {
  completed: true, timestamp: new Date().toISOString(), platform: `${process.platform}/${process.arch}`,
  node: process.version, prettier: prettierVersion, results,
};
await fs.writeFile(path.join(artifacts, "summary.json"), JSON.stringify(summary, null, 2) + "\n");
console.log(JSON.stringify(summary, null, 2));
