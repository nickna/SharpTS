import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const configuration = process.argv[2] ?? "Release";
const output = path.resolve(process.argv[3] ?? path.join(repository, "artifacts/formatter-interop", crypto.randomUUID()));
const sourceRoot = path.join(repository, "tests/fixtures/FormatterInterop");
const formattedRoot = path.join(output, "formatted");
const crlfRoot = path.join(output, "formatted-crlf");
const prettier = path.join(repository, "tools/formatter-interop/node_modules/prettier/bin/prettier.cjs");
const packageJson = JSON.parse(await readFile(path.join(repository, "tools/formatter-interop/package.json"), "utf8"));

function run(command, args, input, timeout = 60_000) {
  const result = spawnSync(command, args, {
    cwd: repository, input, encoding: "utf8", timeout,
    maxBuffer: 16 * 1024 * 1024, windowsHide: true,
  });
  if (result.error) throw new Error(`${command}: ${result.error.message}\n${result.stderr}\n${result.stdout}`);
  assert.equal(result.status, 0, `${command} ${args.join(" ")}\n${result.stderr}\n${result.stdout}`);
  return result.stdout;
}

async function runEvidence(args) {
  const child = spawn("dotnet", args, { cwd: repository, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
  let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; child.kill(); }, 300_000);
  child.stdout.on("data", (data) => process.stdout.write(data));
  child.stderr.on("data", (data) => process.stderr.write(data));
  const code = await new Promise((resolve, reject) => {
    child.once("error", reject);
    child.once("close", resolve);
  }).finally(() => clearTimeout(timer));
  assert.equal(timedOut, false, "Compiler evidence exceeded five minutes");
  assert.equal(code, 0, `Compiler evidence failed: dotnet ${args.join(" ")}`);
}

await mkdir(formattedRoot, { recursive: true });
await rm(path.join(output, "summary.json"), { force: true });
const actualVersion = run(process.execPath, [prettier, "--version"]).trim();
assert.equal(actualVersion, packageJson.devDependencies.prettier, "Use npm ci with the checked-in lock");
const fixtures = JSON.parse(await readFile(path.join(sourceRoot, "fixtures.json"), "utf8"));
const results = [];
for (const fixture of fixtures) {
  const sourcePath = path.resolve(sourceRoot, fixture.path);
  assert.ok(sourcePath.startsWith(sourceRoot + path.sep), "Fixture path must stay within its source root");
  const text = await readFile(sourcePath, "utf8");
  const args = [prettier, "--stdin-filepath", sourcePath];
  const configPath = run(process.execPath, [prettier, "--find-config-path", sourcePath]).trim();
  assert.equal(path.resolve(repository, configPath), path.join(sourceRoot, ".prettierrc.json"),
    `${fixture.name}: real source filename must discover the fixture configuration`);
  const formatted = run(process.execPath, args, text.replace(/\r\n/g, "\n"));
  assert.ok(formatted.length > 0, `${fixture.name}: formatter returned empty output`);
  assert.ok(!formatted.includes("\r\n"), `${fixture.name}: endOfLine auto must retain LF`);
  assert.equal(run(process.execPath, args, formatted), formatted, `${fixture.name}: format is not idempotent`);
  const destination = path.join(formattedRoot, fixture.path);
  await mkdir(path.dirname(destination), { recursive: true });
  await writeFile(destination, formatted, "utf8");
  const crlfInput = text.replace(/\r?\n/g, "\r\n");
  const crlfFormatted = run(process.execPath, args, crlfInput);
  assert.ok(!/(?<!\r)\n/.test(crlfFormatted), `${fixture.name}: endOfLine auto must retain CRLF`);
  assert.equal(run(process.execPath, args, crlfFormatted), crlfFormatted, `${fixture.name}: CRLF idempotence`);
  const crlfDestination = path.join(crlfRoot, fixture.path);
  await mkdir(path.dirname(crlfDestination), { recursive: true });
  await writeFile(crlfDestination, crlfFormatted, "utf8");
  results.push({ name: fixture.name, path: fixture.path,
    originalSha256: createHash("sha256").update(text).digest("hex"),
    formattedSha256: createHash("sha256").update(formatted).digest("hex"),
    changed: text !== formatted, idempotent: true, crlfIdempotent: true, configPath,
  });
}
assert.ok(results.length > 0, "No formatter fixtures were executed");

const harness = path.join(sourceRoot, "bin", configuration, "net10.0/SharpTS.FormatterInteropEvidence.dll");
const semanticReport = path.join(output, "semantics.json");
await runEvidence([harness, sourceRoot, formattedRoot, semanticReport]);
const crlfSemanticReport = path.join(output, "semantics-crlf.json");
await runEvidence([harness, sourceRoot, crlfRoot, crlfSemanticReport]);

const server = path.join(repository, "src/SharpTS.LanguageServer/bin", configuration, "net10.0/SharpTS.LanguageServer.dll");
// Keep existing GUI metadata discovery inside the disposable protocol workspace.
await writeFile(path.join(output, "Smoke.csproj"), '<Project Sdk="SharpTS.Gui.Sdk/0.0.0" />\n');
await mkdir(path.join(output, "obj/@sharpts/gui"), { recursive: true });
await writeFile(path.join(output, "obj/@sharpts/gui/control-docs.generated.json"), '{"controls":[]}\n');
const protocol = [];
for (const mode of ["full", "interop-only"]) {
  const client = new LspStdioClient("dotnet", [server, "--language-features", mode], { cwd: output });
  try {
    const textDocument = Object.fromEntries([
      "hover", "completion", "signatureHelp", "definition", "references", "documentSymbol", "rename",
      "formatting", "rangeFormatting", "onTypeFormatting",
    ].map((name) => [name, { dynamicRegistration: false }]));
    textDocument.rename.prepareSupport = true;
    const initialized = await client.request("initialize", {
      processId: process.pid, rootUri: pathToFileURL(output).href,
      capabilities: { textDocument },
    });
    assert.ok(!initialized.error, JSON.stringify(initialized.error));
    const caps = initialized.result.capabilities;
    assert.ok(caps.hoverProvider && caps.completionProvider && caps.signatureHelpProvider,
      "Positive interop capabilities must be present; no-format verification must not be vacuous");
    assert.equal(caps.definitionProvider != null && caps.definitionProvider !== false, mode === "full");
    assert.equal(caps.renameProvider != null && caps.renameProvider !== false, mode === "full");
    for (const provider of ["documentFormattingProvider", "documentRangeFormattingProvider", "documentOnTypeFormattingProvider"]) {
      assert.ok(caps[provider] == null || caps[provider] === false, `${mode}: ${provider} must remain absent`);
    }
    client.notify("initialized", {});
    const uri = pathToFileURL(path.join(output, `interop-${mode}.ts`)).href;
    client.notify("textDocument/didOpen", { textDocument: {
      uri, languageId: "typescript", version: 1,
      text: '@DotNetType("System.Text.StringBuilder")\ndeclare class Builder {}\n',
    } });
    const hover = await client.request("textDocument/hover", { textDocument: { uri }, position: { line: 0, character: 5 } });
    assert.ok(!hover.error && JSON.stringify(hover.result).includes("StringBuilder"), `${mode}: interop hover must work`);
    for (const method of ["formatting", "rangeFormatting", "onTypeFormatting"]) {
      const formatting = await client.request(`textDocument/${method}`, {
        textDocument: { uri }, options: { tabSize: 2, insertSpaces: true },
        range: { start: { line: 0, character: 0 }, end: { line: 0, character: 10 } },
        position: { line: 0, character: 10 }, ch: ";",
      });
      assert.equal(formatting.error?.code, -32601, `${mode}: ${method} must not be served`);
    }
    assert.ok(!client.registeredMethods.some((method) => /textDocument\/(?:formatting|rangeFormatting|onTypeFormatting)$/.test(method)),
      `${mode}: no formatting method may be dynamically registered`);
    protocol.push({ mode, capabilities: caps, interopHover: true, formattingMethodsNotFound: true });
  } finally {
    await client.close();
  }
}

const report = { completed: true, prettier: actualVersion, node: process.version,
  dotnet: run("dotnet", ["--version"]).trim(), configuration, fixtures: results,
  semanticReport, crlfSemanticReport, protocol };
await writeFile(path.join(output, "summary.json"), JSON.stringify(report, null, 2) + "\n");
console.log(`Formatter interoperability passed: ${results.length} fixtures; both LSP modes. ${output}`);
