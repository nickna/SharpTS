import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { dirname, resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const server = resolve(repository, process.argv[2] ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
await mkdir(join(repository, "artifacts/semantic-completion-smoke"), { recursive: true });
const workspace = await mkdtemp(join(repository, "artifacts/semantic-completion-smoke/run-"));
const dependency = "export class Base { field: number = 1; read(): number { return this.field; } }\n" +
  "export class Derived extends Base { own: string = 'own'; }\n";
const dirtyDependency = dependency.replaceAll("field", "changedField");
const header = "import { Derived } from './dep';\r\nconst item = new Derived();\r\n";
await writeFile(join(workspace, "tsconfig.json"), JSON.stringify({
  compilerOptions: { noLib: true, types: [] }, include: ["*.ts"],
}));
await writeFile(join(workspace, "dep.ts"), dependency);
await writeFile(join(workspace, "main.ts"), header);
const uri = (name) => pathToFileURL(join(workspace, name)).href;
const sameUri = (left, right) => process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
const positionAt = (text, offset) => {
  const prefix = text.slice(0, offset);
  const last = prefix.lastIndexOf("\n");
  return { line: prefix.split("\n").length - 1, character: offset - last - 1 };
};
const offsetAt = (text, position) => {
  let offset = 0;
  for (let line = 0; line < position.line; line++) {
    const end = text.indexOf("\n", offset);
    assert.notEqual(end, -1);
    offset = end + 1;
  }
  return offset + position.character;
};
const itemsOf = (result) => Array.isArray(result) ? result : result?.items ?? [];
async function request(client, method, params) {
  const response = await client.request(method, params);
  assert.equal(response.error, undefined, method + ": " + JSON.stringify(response.error));
  return response.result;
}
async function diagnostics(client, start, name, version) {
  const deadline = performance.now() + 20_000;
  while (performance.now() < deadline) {
    const found = client.notifications.slice(start).find((item) => item.method === "textDocument/publishDiagnostics" &&
      sameUri(item.params.uri, uri(name)) && item.params.version === version);
    if (found) return found.params.diagnostics;
    await delay(25);
  }
  throw new Error("No diagnostics barrier for " + name + " version " + version);
}
function assertItems(items, supportedKinds) {
  assert.ok(items.length <= 256, "Semantic item bound");
  const labels = items.map((item) => item.label);
  assert.deepEqual(labels, [...new Set(labels)].sort(), "Deterministic unique labels");
  for (const item of items) {
    assert.ok(item.insertTextFormat === undefined || item.insertTextFormat === 1, "Plain text completion");
    assert.ok(item.detail === undefined || item.detail.length <= 4096, "Bounded detail");
    assert.ok(item.textEdit?.range, "Exact simple text edit");
    if (supportedKinds) assert.ok(item.kind === undefined || supportedKinds.includes(item.kind), "Negotiated item kind");
  }
}
function selected(items, name) {
  const item = items.find((candidate) => candidate.label === name);
  assert.ok(item, "Missing completion " + name + ": " + items.map((candidate) => candidate.label).join(", "));
  return item;
}
function apply(text, item, start, end) {
  assert.deepEqual(item.textEdit.range, { start: positionAt(text, start), end: positionAt(text, end) });
  const actualStart = offsetAt(text, item.textEdit.range.start);
  const actualEnd = offsetAt(text, item.textEdit.range.end);
  return text.slice(0, actualStart) + item.textEdit.newText + text.slice(actualEnd);
}

const report = { workspace, server, clients: [] };
for (const configuration of [
  { mode: "full", restrictedKinds: false },
  { mode: "full", restrictedKinds: true },
  { mode: "full", restrictedKinds: true, supportedKinds: [2] },
  { mode: "interop-only", restrictedKinds: true },
]) {
  const { mode, restrictedKinds } = configuration;
  const supportedKinds = restrictedKinds ? configuration.supportedKinds ?? [1] : undefined;
  const client = new LspStdioClient("dotnet", [server, "--language-features", mode, "--diagnostics",
    mode === "full" ? "all" : "sharpts-only"], { cwd: workspace });
  let version = 0;
  let currentText = "";
  let failure;
  try {
    const capabilities = await request(client, "initialize", {
      processId: process.pid, rootUri: pathToFileURL(workspace).href,
      workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "semantic-completion" }],
      capabilities: { textDocument: {
        completion: { completionItem: { snippetSupport: false },
          ...(supportedKinds ? { completionItemKind: { valueSet: supportedKinds } } : {}) },
        publishDiagnostics: { versionSupport: true },
      } },
    });
    const triggers = capabilities.capabilities.completionProvider?.triggerCharacters ?? [];
    for (const legacy of ["@", "<", " ", "=", "\""]) assert.ok(triggers.includes(legacy), "Legacy trigger " + legacy);
    assert.equal(triggers.includes("."), mode === "full", "Member trigger isolation");
    for (const feature of ["documentFormattingProvider", "documentRangeFormattingProvider", "documentOnTypeFormattingProvider"])
      assert.ok(!capabilities.capabilities[feature]);
    client.notify("initialized", {});
    async function setSource(text) {
      const start = client.notifications.length;
      currentText = text;
      version++;
      if (version === 1) client.notify("textDocument/didOpen", {
        textDocument: { uri: uri("main.ts"), languageId: "typescript", version, text },
      });
      else client.notify("textDocument/didChange", {
        textDocument: { uri: uri("main.ts"), version }, contentChanges: [{ text }],
      });
      return diagnostics(client, start, "main.ts", version);
    }
    async function complete(offset = currentText.length) {
      return itemsOf(await request(client, "textDocument/completion", {
        textDocument: { uri: uri("main.ts") }, position: positionAt(currentText, offset), context: { triggerKind: 1 },
      }));
    }
    const lexical = header + "const lexicalName: number = 1;\r\n" +
      "function read() { const lexicalLocal = lexicalName; return /* 😀 */ lexicZZ; }\r\n";
    await setSource(lexical);
    const lexicalStart = lexical.indexOf("lexicZZ");
    let items = await complete(lexicalStart + 5);
    if (mode === "full") {
      assertItems(items, supportedKinds);
      const edited = apply(lexical, selected(items, "lexicalLocal"), lexicalStart, lexicalStart + "lexicZZ".length);
      assert.deepEqual(await setSource(edited), [], "Applied lexical edit checks cleanly");
    } else assert.deepEqual(items, []);

    const partial = header + "/* 😀 */ item.fiZZ;\r\n";
    await setSource(partial);
    const partialStart = partial.indexOf("fiZZ");
    items = await complete(partialStart + 2);
    if (mode === "full") {
      assertItems(items, supportedKinds);
      const edited = apply(partial, selected(items, "field"), partialStart, partialStart + 4);
      assert.deepEqual(await setSource(edited), [], "Applied member edit checks cleanly");
    } else assert.deepEqual(items, []);

    for (const suffix of ["item.", "item?."]) {
      await setSource(header + suffix);
      items = await complete();
      if (mode === "full") {
        assertItems(items, supportedKinds);
        const field = selected(items, "field");
        selected(items, "read");
        const concurrent = await Promise.all(Array.from({ length: 4 }, () => complete()));
        concurrent.forEach((values) => assert.deepEqual(values, items));
        const edited = apply(currentText, field, currentText.length, currentText.length) + ";";
        assert.deepEqual(await setSource(edited), [], "Applied recovered member edit checks cleanly");
      } else assert.deepEqual(items, []);
    }
    if (mode === "full") {
      await setSource("const shape = { total: 1 }; shape.");
      selected(await complete(), "total");
      await setSource("import * as Model from './dep'; Model.");
      selected(await complete(), "Derived");
      await setSource(header + "item.");
      const dirtyStart = client.notifications.length;
      client.notify("textDocument/didOpen", {
        textDocument: { uri: uri("dep.ts"), languageId: "typescript", version: 1, text: dirtyDependency },
      });
      await diagnostics(client, dirtyStart, "dep.ts", 1);
      const changed = await complete();
      selected(changed, "changedField");
      assert.ok(!changed.some((item) => item.label === "field"));
      const closeStart = client.notifications.length;
      client.notify("textDocument/didClose", { textDocument: { uri: uri("dep.ts") } });
      await diagnostics(client, closeStart, "main.ts", version);
      selected(await complete(), "field");
    }
    for (const refused of [
      "const item: any = {}; item.",
      "missing.",
      "const value = 1.",
      "const lexicalName = 1; // lexical",
      "const lexicalName = 1; const text = 'lexical';",
    ]) {
      await setSource(refused);
      const offset = refused.includes("'lexical'") ? refused.indexOf("'lexical'") + 4 : refused.length;
      assert.deepEqual(await complete(offset), [], "Refused unreliable context: " + refused);
    }
    await setSource("@Do");
    selected(await complete(), "DotNetType");
    report.clients.push({ mode, supportedKinds, ordinaryCompletion: mode === "full", decoratorCompletion: true,
      memberTriggers: mode === "full", recoveredDotAndOptional: mode === "full",
      exactAppliedUtf16CrLfEdits: mode === "full", dirtyDependencyAndClose: mode === "full" });
  } catch (error) {
    failure = error;
    error.message = `${mode} version ${version} (${currentText.slice(-100)}): ${error.message}; ` +
      `notifications: ${JSON.stringify(client.notifications.slice(-10))}`;
    throw error;
  } finally {
    try { await client.close(); }
    catch (error) {
      if (!failure) throw error;
      console.error("Cleanup after test failure: " + error.message);
    }
  }
}
await writeFile(join(workspace, "result.json"), JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report, null, 2));
