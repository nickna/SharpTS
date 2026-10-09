import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const workspace = path.resolve(process.argv[2] ?? "artifacts/formatter-interop/helix/full");
const config = JSON.parse(await fs.readFile(path.join(workspace, "test-config.json"), "utf8"));
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const pin = JSON.parse(await fs.readFile(path.join(root, "tools/formatter-interop/package.json"), "utf8")).devDependencies.prettier;
const installed = spawnSync(process.execPath, [path.join(root, "tools/formatter-interop/node_modules/prettier/bin/prettier.cjs"), "--version"],
  { encoding: "utf8", windowsHide: true, timeout: 10000 });
assert.equal(installed.status, 0, installed.stderr);
const prettierVersion = installed.stdout.trim();
assert.equal(prettierVersion, pin, "installed formatter differs from the exact repository pin");
if (config.prettierVersion) assert.equal(prettierVersion, config.prettierVersion);
const log = await fs.readFile(config.log, "utf8");
const messages = log.split(/\r?\n/).flatMap((line, index) => {
  const match = line.match(/\bsharpts (->|<-) (\{.*\})$/);
  return match ? [{ direction: match[1], message: JSON.parse(match[2]), index }] : [];
});
const initialize = messages.findLast((item) => item.direction === "->" && item.message.method === "initialize");
assert.ok(initialize, "no real LSP initialize request");
const initialized = messages.find((item) => item.index > initialize.index && item.direction === "<-"
  && item.message.id === initialize.message.id);
assert.ok(initialized?.message.result?.capabilities, "no successful initialize response");
const capabilities = initialized.message.result.capabilities;
for (const name of ["documentFormattingProvider", "documentRangeFormattingProvider", "documentOnTypeFormattingProvider"]) {
  assert.ok(!capabilities[name], `SharpTS advertised ${name}`);
}
assert.equal(Boolean(capabilities.definitionProvider), config.mode === "full", "wrong feature mode");
const formattingMethod = /^textDocument\/(formatting|rangeFormatting|onTypeFormatting)$/;
for (const item of messages.filter((item) => item.direction === "<-" && item.message.method === "client/registerCapability")) {
  assert.ok(!item.message.params.registrations.some((registration) => formattingMethod.test(registration.method)),
    "SharpTS dynamically registered formatting");
}
const hoverEvidence = [];
for (const fixture of config.fixtures) {
  assert.equal(await fs.readFile(fixture.file, "utf8"), fixture.expected, `save bytes differ: ${fixture.file}`);
  const uri = pathToFileURL(fixture.file).href.toLowerCase();
  const save = messages.find((item) => item.direction === "->" && item.message.method === "textDocument/didSave"
    && item.message.params.textDocument.uri.toLowerCase() === uri);
  assert.ok(save, `no real didSave for ${fixture.file}`);
  assert.equal(save.message.params.text, fixture.expected, "didSave did not contain formatted source");
  const hovers = messages.filter((item) => item.direction === "->" && item.message.method === "textDocument/hover"
    && item.message.params.textDocument.uri.toLowerCase() === uri).flatMap((request) => {
    const response = messages.find((item) => item.index > request.index && item.direction === "<-"
      && item.message.id === request.message.id);
    const contents = response?.message.result?.contents?.value;
    return !response?.message.error && contents?.includes("System.Text.StringBuilder")
      && contents.includes("Represents a mutable string of characters") ? [{ request, response }] : [];
  });
  const before = hovers.find((item) => item.response.index < save.index);
  const after = hovers.find((item) => item.request.index > save.index);
  assert.ok(before, `no successful pre-save hover for ${fixture.file}`);
  assert.ok(after, `no successful fresh post-save hover for ${fixture.file}`);
  hoverEvidence.push({ file: fixture.file, beforeRequest: before.request.message.id, afterRequest: after.request.message.id });
}
assert.ok(!messages.some((item) => formattingMethod.test(item.message.method)),
  "formatting was incorrectly delegated to LSP");
const result = {
  completed: true, timestamp: new Date().toISOString(), editor: config.version,
  mode: config.mode, node: process.version, prettier: prettierVersion,
  formatted: config.fixtures.map((fixture) => fixture.file),
  hoverEvidence, interopHover: true, lspFormattingRequests: 0, log: config.log,
};
await fs.writeFile(path.join(workspace, "result.json"), JSON.stringify(result, null, 2) + "\n");
console.log(JSON.stringify(result, null, 2));
