import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const options = new Map();
for (let i = 2; i < process.argv.length; i += 2) {
  assert.ok(process.argv[i]?.startsWith("--") && process.argv[i + 1], "use --name value pairs");
  options.set(process.argv[i].slice(2), process.argv[i + 1]);
}
assert.ok(options.has("binaries"), "--binaries must name the coordinated published LS + DAP directory");
const binaries = path.resolve(root, options.get("binaries"));
const artifacts = path.resolve(root, options.get("artifacts") ?? "artifacts/editor-shipping/vscode/package");
const extension = path.join(root, "extensions/vscode-sharpts");
await fs.mkdir(artifacts, { recursive: true });
const stage = await fs.mkdtemp(path.join(artifacts, "extension-"));
for (const name of ["SharpTS.LanguageServer.dll", "SharpTS.LanguageServer.runtimeconfig.json", "SharpTS.dll", "SharpTS.DebugAdapter.dll"])
  await fs.access(path.join(binaries, name));
const compile = spawnSync(process.execPath, [path.join(extension, "node_modules/typescript/bin/tsc"), "-p", extension],
  { cwd: root, encoding: "utf8", timeout: 60_000, windowsHide: true });
assert.equal(compile.status, 0, compile.stdout + compile.stderr);
for (const name of ["package.json", "package-lock.json", ".vscodeignore", "out"])
  await fs.cp(path.join(extension, name), path.join(stage, name), { recursive: true, force: true });
await fs.copyFile(path.join(root, "LICENSE"), path.join(stage, "LICENSE"));
await fs.copyFile(path.join(root, "docs/language-server.md"), path.join(stage, "README.md"));
// Use the exact installed production tree, without executing package acquisition/hooks here.
const lock = JSON.parse(await fs.readFile(path.join(extension, "package-lock.json"), "utf8"));
for (const [relative, metadata] of Object.entries(lock.packages)) {
  if (!relative.startsWith("node_modules/") || metadata.dev) continue;
  assert.ok(!relative.split("/").includes(".."));
  await fs.cp(path.join(extension, relative), path.join(stage, relative), { recursive: true });
}
await fs.cp(binaries, path.join(stage, "bin/server"), { recursive: true, force: true });
const vsix = path.join(artifacts, "vscode-sharpts.vsix");
const packed = spawnSync(process.execPath, [path.join(extension, "node_modules/@vscode/vsce/vsce"),
  "package", "--out", vsix], { cwd: stage, encoding: "utf8", timeout: 60_000, windowsHide: true });
await fs.writeFile(path.join(artifacts, "package.log"), packed.stdout + packed.stderr);
assert.equal(packed.status, 0, packed.stdout + packed.stderr);
const hash = async (file) => createHash("sha256").update(await fs.readFile(file)).digest("hex");
const version = JSON.parse(await fs.readFile(path.join(stage, "package.json"), "utf8")).version;
const report = { vsix, version, sha256: await hash(vsix), binaries,
  serverSha256: await hash(path.join(binaries, "SharpTS.LanguageServer.dll")),
  coreSha256: await hash(path.join(binaries, "SharpTS.dll")), stage };
await fs.writeFile(path.join(artifacts, "package.json"), JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report, null, 2));
