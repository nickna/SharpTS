import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile, unlink } from "node:fs/promises";
import { dirname, resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const server = resolve(repository, process.argv[2] ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
await mkdir(join(repository, "artifacts/member-reference-smoke"), { recursive: true });
const workspace = await mkdtemp(join(repository, "artifacts/member-reference-smoke/run-"));
const owner = "export class Base {\r\n" +
  "  /* 😀 */ /*decl*/field: number = 1;\r\n" +
  "  constructor(public /*parameter*/token: number = 1) {}\r\n" +
  "  read(): number { return this./*self*/field; }\r\n}\r\n" +
  "export class Derived extends Base {}\r\n" +
  "export class Unrelated { field: number = 0; }\r\n";
const entry = "import { Derived } from '../lib/model';\r\n" +
  "const item = new Derived(1);\r\n" +
  "const /*lexicalDeclaration*/lexical = 1;\r\n" +
  "/* 😀 */ item./*read*/field;\r\n" +
  "item./*write*/field = 2;\r\n" +
  "item?./*optional*/field;\r\n" +
  "item./*update*/field++;\r\n" +
  "item./*token*/token;\r\n" +
  "/*lexicalUse*/lexical;\r\n";
const reverse = "import { Base } from '../lib/model';\r\n" +
  "const closed = new Base(1);\r\n" +
  "/* 😀 */ closed./*reverse*/field;\r\nclosed./*reverseToken*/token;\r\n";
const options = { noLib: true, types: [] };
await mkdir(join(workspace, "lib"));
await mkdir(join(workspace, "app"));
await writeFile(join(workspace, "lib/tsconfig.json"), JSON.stringify({ compilerOptions: options, files: ["model.ts"] }));
await writeFile(join(workspace, "app/tsconfig.json"), JSON.stringify({
  compilerOptions: options, files: ["main.ts", "reverse.ts"], references: [{ path: "../lib" }],
}));
await writeFile(join(workspace, "lib/model.ts"), owner);
await writeFile(join(workspace, "app/main.ts"), entry);
await writeFile(join(workspace, "app/reverse.ts"), reverse);
const uri = (name) => pathToFileURL(join(workspace, name)).href;
const canonicalUri = (value) => process.platform === "win32" ? value.toLowerCase() : value;
const sameUri = (left, right) => canonicalUri(left) === canonicalUri(right);
const positionAt = (text, offset) => {
  const prefix = text.slice(0, offset);
  return { line: prefix.split("\n").length - 1, character: offset - prefix.lastIndexOf("\n") - 1 };
};
function location(name, text, marker, spelling) {
  const comment = "/*" + marker + "*/";
  const offset = text.indexOf(comment);
  assert.notEqual(offset, -1, "Missing marker " + marker);
  const start = offset + comment.length;
  assert.equal(text.slice(start, start + spelling.length), spelling);
  return { uri: uri(name), range: { start: positionAt(text, start), end: positionAt(text, start + spelling.length) } };
}
function order(left, right) {
  const a = canonicalUri(left.uri), b = canonicalUri(right.uri);
  return (a < b ? -1 : a > b ? 1 : 0) || left.range.start.line - right.range.start.line ||
    left.range.start.character - right.range.start.character || left.range.end.line - right.range.end.line ||
    left.range.end.character - right.range.end.character;
}
const key = (value) => JSON.stringify({ uri: canonicalUri(value.uri), range: value.range });
function assertLocations(result, expected) {
  const actual = result ?? [];
  assert.ok(Array.isArray(actual));
  assert.equal(new Set(actual.map(key)).size, actual.length, "Complete source spans are deduplicated");
  assert.deepEqual(actual.map(key), [...actual].sort(order).map(key), "References have deterministic URI/range order");
  assert.deepEqual(actual.map(key), [...expected].sort(order).map(key), "Exact UTF-16/CRLF member token locations");
  return actual;
}
function fieldLocations(ownerText = owner, entryText = entry, includeDeclaration = true) {
  return [
    ...(includeDeclaration ? [location("lib/model.ts", ownerText, "decl", "field")] : []),
    location("lib/model.ts", ownerText, "self", "field"),
    ...["read", "write", "optional", "update"].map((marker) => location("app/main.ts", entryText, marker, "field")),
    location("app/reverse.ts", reverse, "reverse", "field"),
  ];
}
const tokenLocations = (ownerText = owner) => [
  location("lib/model.ts", ownerText, "parameter", "token"),
  location("app/main.ts", entry, "token", "token"),
  location("app/reverse.ts", reverse, "reverseToken", "token"),
];
const lexicalLocations = (text = entry) => ["lexicalDeclaration", "lexicalUse"].map((marker) =>
  location("app/main.ts", text, marker, "lexical"));
const requestAt = (marker = "read", text = entry, spelling = "field") => ({
  textDocument: { uri: uri("app/main.ts") },
  position: { ...location("app/main.ts", text, marker, spelling).range.start,
    character: location("app/main.ts", text, marker, spelling).range.start.character + 1 },
});
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
async function initialize(client) {
  const result = await request(client, "initialize", {
    processId: process.pid, rootUri: pathToFileURL(workspace).href,
    workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "member-references" }],
    capabilities: {
      workspace: { didChangeWatchedFiles: { dynamicRegistration: true }, workspaceFolders: true },
      textDocument: { references: {}, rename: { prepareSupport: true }, publishDiagnostics: { versionSupport: true } },
    },
  });
  client.notify("initialized", {});
  return result.capabilities;
}

const report = { workspace, server, full: {}, interopOnly: {} };
for (const mode of ["full", "interop-only"]) {
  const client = new LspStdioClient("dotnet", [server, "--language-features", mode, "--diagnostics",
    mode === "full" ? "all" : "sharpts-only"], { cwd: workspace });
  let failure;
  let phase = "initialize";
  const runCase = async (name, action) => {
    phase = name;
    try { return await action(); }
    catch (error) { throw new Error(`${mode}: ${name}: ${error.message}`, { cause: error }); }
  };
  try {
    const capabilities = await initialize(client);
    assert.equal(Boolean(capabilities.referencesProvider), mode === "full");
    assert.equal(Boolean(capabilities.renameProvider), mode === "full");
    client.notify("textDocument/didOpen", {
      textDocument: { uri: uri("app/main.ts"), languageId: "typescript", version: 1, text: entry },
    });
    await diagnostics(client, 0, "app/main.ts", 1);
    if (mode === "interop-only") {
      await runCase("references-method-is-unregistered", async () => {
        const response = await client.request("textDocument/references", { ...requestAt(), context: { includeDeclaration: true } });
        assert.equal(response.error?.code, -32601);
      });
      report.interopOnly = { capabilityAbsent: true, referencesError: -32601 };
      continue;
    }
    const references = (includeDeclaration = true, at = requestAt()) => request(client, "textDocument/references",
      { ...at, context: { includeDeclaration } });
    await runCase("two-projects-closed-reverse-importer-and-declaration-filter", async () => {
      const withDeclaration = assertLocations(await references(), fieldLocations());
      assertLocations(await references(false), fieldLocations(owner, entry, false));
      const repeated = await Promise.all(Array.from({ length: 4 }, () => references()));
      repeated.forEach((result) => assertLocations(result, fieldLocations()));
      assert.ok(withDeclaration.some((value) => sameUri(value.uri, uri("app/reverse.ts"))), "Closed reverse importer is included");
      report.full.referenceCount = withDeclaration.length;
    });
    await runCase("complete-member-references-do-not-enable-public-or-parameter-property-rename", async () => {
      assertLocations(await references(true, requestAt("token", entry, "token")), tokenLocations());
      for (const at of [requestAt(), requestAt("token", entry, "token")]) {
        assert.equal(await request(client, "textDocument/prepareRename", at), null);
        assert.equal(await request(client, "textDocument/rename", { ...at, newName: "nextMember" }), null);
      }
    });
    await runCase("dirty-owner-anchor-and-source-ranges-move-together", async () => {
      const dirty = "// dirty owner version one\r\n" + owner;
      const openStart = client.notifications.length;
      client.notify("textDocument/didOpen", { textDocument: {
        uri: uri("lib/model.ts"), languageId: "typescript", version: 1, text: dirty,
      } });
      assert.deepEqual(await diagnostics(client, openStart, "lib/model.ts", 1), []);
      assertLocations(await references(), fieldLocations(dirty));
      const changed = "// dirty owner version two\r\n" + dirty;
      const changeStart = client.notifications.length;
      client.notify("textDocument/didChange", { textDocument: { uri: uri("lib/model.ts"), version: 2 },
        contentChanges: [{ text: changed }] });
      assert.deepEqual(await diagnostics(client, changeStart, "lib/model.ts", 2), []);
      assertLocations(await references(), fieldLocations(changed));
      const removed = changed.replaceAll("field", "renamedField");
      const removeStart = client.notifications.length;
      client.notify("textDocument/didChange", { textDocument: { uri: uri("lib/model.ts"), version: 3 },
        contentChanges: [{ text: removed }] });
      assert.deepEqual(await diagnostics(client, removeStart, "lib/model.ts", 3), []);
      assertLocations(await references(), []);
      const closeStart = client.notifications.length;
      client.notify("textDocument/didClose", { textDocument: { uri: uri("lib/model.ts") } });
      await diagnostics(client, closeStart, "app/main.ts", 1);
      assertLocations(await references(), fieldLocations());
      report.full.dirtyOwnerVersions = [1, 2, 3];
      report.full.closeRestoresDisk = true;
    });
    await runCase("incomplete-config-discovery-refuses-member-results-and-preserves-lexical-results", async () => {
      assertLocations(await references(true, requestAt("lexicalUse", entry, "lexical")), lexicalLocations());
      await mkdir(join(workspace, "broken"));
      const broken = join(workspace, "broken/tsconfig.json");
      await writeFile(broken, "{ invalid config");
      const changedStart = client.notifications.length;
      client.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("broken/tsconfig.json"), type: 1 }] });
      await diagnostics(client, changedStart, "app/main.ts", 1);
      assertLocations(await references(), []);
      assertLocations(await references(true, requestAt("lexicalUse", entry, "lexical")), lexicalLocations());
      await unlink(broken);
      const restoredStart = client.notifications.length;
      client.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("broken/tsconfig.json"), type: 3 }] });
      await diagnostics(client, restoredStart, "app/main.ts", 1);
      assertLocations(await references(), fieldLocations());
      report.full.incompleteMemberRefusalPreservesLexical = true;
    });
    await runCase("wire-cancellation-with-surviving-peer-and-fresh-request", async () => {
      client.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("lib/model.ts"), type: 2 }] });
      const cancelled = client.beginRequest("textDocument/references", { ...requestAt(), context: { includeDeclaration: true } });
      const peer = references();
      void peer.catch(() => {});
      const cancelSent = cancelled.cancel();
      const response = await cancelled.response;
      let cancellationOutcome;
      if (response.error) {
        assert.equal(response.error.code, -32800);
        cancellationOutcome = "request-cancelled";
      } else if ((response.result ?? []).length === 0) cancellationOutcome = "no-result-during-invalidation";
      else {
        assertLocations(response.result, fieldLocations());
        cancellationOutcome = "completed-before-cancel";
      }
      assert.ok(cancelSent, "Actual $/cancelRequest notification was sent");
      assertLocations(await peer, fieldLocations());
      assertLocations(await references(), fieldLocations());
      report.full.cancellation = { cancelSent, cancellationOutcome, peerAndFreshSurvived: true };
    });
    report.full = { ...report.full, twoConfiguredProjects: true, projectReference: true, closedReverseImporter: true,
      exactUtf16CrLfRanges: true, deterministicDeduplicated: true, includeDeclaration: [true, false],
      publicAndParameterPropertyRenameDenied: true };
  } catch (error) {
    failure = error;
    await writeFile(join(workspace, "failure.json"), JSON.stringify({ mode, phase, message: error.message,
      notifications: client.notifications.slice(-12), report }, null, 2) + "\n").catch(() => {});
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
