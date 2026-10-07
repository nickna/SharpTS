import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { dirname, resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const server = resolve(repository, process.argv[2] ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
await mkdir(join(repository, "artifacts/semantic-hover-smoke"), { recursive: true });
const workspace = await mkdtemp(join(repository, "artifacts/semantic-hover-smoke/run-"));
const dependency = "export const value: number = 123;\n";
const dirtyDependency = "export const value: string = 'dirty';\n";
const source = "import { value as renamed } from './dep';\r\n" +
  "import { StringBuilder as SB } from 'dotnet:System.Text.StringBuilder';\r\n" +
  "const local = /*alias*/renamed;\r\n/* 😀 */ /*local*/local;\r\n" +
  "const builder = new SB();\r\nbuilder./*clr*/append('x');\r\n";
const decorator = '@DotNetType("System.String")\ndeclare class NetString {}\n';
await writeFile(join(workspace, "tsconfig.json"), JSON.stringify({
  compilerOptions: { noLib: true, types: [], experimentalDecorators: true }, include: ["*.ts"],
}));
await writeFile(join(workspace, "main.ts"), source);
await writeFile(join(workspace, "dep.ts"), dependency);
const uri = (name) => pathToFileURL(join(workspace, name)).href;
const sameUri = (left, right) => process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
const positionAt = (text, offset) => {
  const prefix = text.slice(0, offset);
  const lastLine = prefix.lastIndexOf("\n");
  return { line: prefix.split("\n").length - 1, character: offset - lastLine - 1 };
};
const markerRange = (marker) => {
  const comment = `/*${marker}*/`;
  const index = source.indexOf(comment);
  assert.notEqual(index, -1, `Missing marker ${marker}`);
  const start = index + comment.length;
  const name = source.slice(start).match(/^[A-Za-z_][A-Za-z_0-9]*/)?.[0];
  assert.ok(name);
  return { start: positionAt(source, start), end: positionAt(source, start + name.length) };
};
const hoverPosition = (marker) => {
  const range = markerRange(marker);
  return { textDocument: { uri: uri("main.ts") }, position: { ...range.start, character: range.start.character + 1 } };
};
async function request(client, method, params) {
  const response = await client.request(method, params);
  assert.equal(response.error, undefined, `${method}: ${JSON.stringify(response.error)}`);
  return response.result;
}
async function awaitDiagnostics(client, start, document, version) {
  const deadline = performance.now() + 20_000;
  while (performance.now() < deadline) {
    const match = client.notifications.slice(start).find((item) => item.method === "textDocument/publishDiagnostics" &&
      sameUri(item.params.uri, uri(document)) && item.params.version === version);
    if (match) {
      assert.deepEqual(match.params.diagnostics, [], `Unexpected ${document} diagnostics`);
      return;
    }
    await delay(25);
  }
  throw new Error(`No version ${version} diagnostics for ${document}: ${JSON.stringify(client.notifications.slice(start))}`);
}
function assertSemanticHover(hover, marker, format, type) {
  assert.ok(hover, `${marker} semantic hover was absent`);
  assert.equal(hover.contents.kind, format);
  assert.match(hover.contents.value, new RegExp(`\\b${type}\\b`));
  assert.deepEqual(hover.range, markerRange(marker));
  if (format === "markdown") assert.match(hover.contents.value, /```typescript/);
  else assert.ok(!hover.contents.value.includes("```"));
}

const report = { workspace, server, clients: [] };
for (const mode of ["full", "interop-only"]) {
  for (const format of ["markdown", "plaintext"]) {
    const client = new LspStdioClient("dotnet", [server, "--language-features", mode, "--diagnostics", mode === "full" ? "all" : "sharpts-only"], { cwd: workspace });
    try {
      const initialized = await request(client, "initialize", {
        processId: process.pid, rootUri: pathToFileURL(workspace).href,
        workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "semantic-hover" }],
        capabilities: { textDocument: { hover: { contentFormat: [format] }, publishDiagnostics: { versionSupport: true } } },
      });
      assert.ok(initialized.capabilities.hoverProvider);
      client.notify("initialized", {});
      client.notify("textDocument/didOpen", { textDocument: { uri: uri("main.ts"), languageId: "typescript", version: 1, text: source } });
      if (mode === "full") await awaitDiagnostics(client, 0, "main.ts", 1);

      const local = await request(client, "textDocument/hover", hoverPosition("local"));
      const alias = await request(client, "textDocument/hover", hoverPosition("alias"));
      if (mode === "full") {
        assertSemanticHover(local, "local", format, "number");
        assertSemanticHover(alias, "alias", format, "number");
        const concurrent = await Promise.all(Array.from({ length: 4 }, () => request(client, "textDocument/hover", hoverPosition("local"))));
        concurrent.forEach((hover) => assertSemanticHover(hover, "local", format, "number"));
      } else {
        assert.equal(local, null);
        assert.equal(alias, null);
      }
      const clr = await request(client, "textDocument/hover", hoverPosition("clr"));
      assert.match(JSON.stringify(clr), /Append/);
      assert.equal(clr.contents.kind, format);
      client.notify("textDocument/didOpen", { textDocument: { uri: uri("decorator.ts"), languageId: "typescript", version: 1, text: decorator } });
      const clrDecorator = await request(client, "textDocument/hover", {
        textDocument: { uri: uri("decorator.ts") }, position: { line: 0, character: 4 },
      });
      assert.match(JSON.stringify(clrDecorator), /System\.String/);
      assert.equal(clrDecorator.contents.kind, format);

      if (mode === "full") {
        const openStart = client.notifications.length;
        client.notify("textDocument/didOpen", { textDocument: { uri: uri("dep.ts"), languageId: "typescript", version: 1, text: dirtyDependency } });
        await awaitDiagnostics(client, openStart, "dep.ts", 1);
        assertSemanticHover(await request(client, "textDocument/hover", hoverPosition("alias")), "alias", format, "string");
        assertSemanticHover(await request(client, "textDocument/hover", hoverPosition("local")), "local", format, "string");
        const closeStart = client.notifications.length;
        client.notify("textDocument/didClose", { textDocument: { uri: uri("dep.ts") } });
        await awaitDiagnostics(client, closeStart, "main.ts", 1);
        assertSemanticHover(await request(client, "textDocument/hover", hoverPosition("local")), "local", format, "number");
      }
      report.clients.push({ mode, format, ordinaryHover: mode === "full", clrUsage: true, clrDecorator: true,
        exactUtf16CrLfRanges: mode === "full", dirtyAliasAndClose: mode === "full" });
    } finally { await client.close(); }
  }
}
await writeFile(join(workspace, "result.json"), JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report, null, 2));
