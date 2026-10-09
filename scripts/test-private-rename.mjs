import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile, unlink } from "node:fs/promises";
import { dirname, resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const server = resolve(repository, process.argv[2] ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
await mkdir(join(repository, "artifacts/private-rename-smoke"), { recursive: true });
const workspace = await mkdtemp(join(repository, "artifacts/private-rename-smoke/run-"));
await mkdir(join(workspace, "project"));
await writeFile(join(workspace, "project/tsconfig.json"), JSON.stringify({
  compilerOptions: { noLib: true, types: [] }, include: ["*.ts"],
}));
const base = "class Box {\n" +
  "  /* 😀 */ /*declaration*/#old: number = 1;\n" +
  "  static #taken: number = 2;\n" +
  "  #bump(delta: number): number { this./*write*/#old = delta; return this./*read*/#old; }\n" +
  "  run(candidate: object): number {\n" +
  "    const read = () => this./*closure*/#old;\n" +
  "    if (/*brand*/#old in candidate) return this./*brandRead*/#old;\n" +
  "    return read();\n  }\n" +
  "  invoke(): number { return this.#bump(3); }\n}\n" +
  "class Other { #old: number = 9; read(): number { return this.#old; } }\n" +
  "const text = '#old'; // #old is text, not an occurrence\n" +
  "const box = new Box(); box.invoke(); box.run(box);\n";
const selectedMarkers = ["declaration", "write", "read", "closure", "brand", "brandRead"];
await writeFile(join(workspace, "project/main.ts"), base);
const uri = (name = "project/main.ts") => pathToFileURL(join(workspace, name)).href;
const sameUri = (left, right) => process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
const positionAt = (text, offset) => {
  const prefix = text.slice(0, offset);
  return { line: prefix.split("\n").length - 1, character: offset - prefix.lastIndexOf("\n") - 1 };
};
function offsetAt(text, position) {
  let offset = 0;
  for (let line = 0; line < position.line; line++) {
    const end = text.indexOf("\n", offset);
    assert.notEqual(end, -1, "Edit line is inside captured text");
    offset = end + 1;
  }
  const value = offset + position.character;
  const end = text.indexOf("\n", offset);
  assert.ok(value <= (end < 0 ? text.length : end), "Edit character is inside its captured line");
  return value;
}
function markerRange(text, marker, spelling = "#old") {
  const comment = "/*" + marker + "*/";
  const index = text.indexOf(comment);
  assert.notEqual(index, -1, "Missing marker " + marker);
  const offset = index + comment.length;
  assert.equal(text.slice(offset, offset + spelling.length), spelling);
  return { start: positionAt(text, offset), end: positionAt(text, offset + spelling.length) };
}
const rangeKey = (range) => JSON.stringify(range);
function assertVersionedEdit(edit, text, version, markers, replacement = "#new") {
  assert.ok(edit, "Expected private rename edit");
  assert.ok(edit.changes === undefined || edit.changes === null, "Private rename never sends unversioned changes");
  assert.equal(edit.documentChanges?.length, 1, "Exactly one captured document is edited");
  const change = edit.documentChanges[0];
  assert.ok(sameUri(change.textDocument?.uri, uri()));
  assert.equal(change.textDocument.version, version, "Captured open-buffer version accompanies every private edit");
  const edits = change.edits;
  assert.equal(edits.length, markers.length, "Complete selected lexical domain, without unrelated private names");
  const expected = markers.map((marker) => rangeKey(markerRange(text, marker))).sort();
  assert.deepEqual(edits.map((value) => rangeKey(value.range)).sort(), expected, "Exact whole #token UTF-16 ranges");
  assert.equal(new Set(edits.map((value) => rangeKey(value.range))).size, edits.length, "No duplicate edits");
  const ordered = edits.map((value) => ({ ...value, start: offsetAt(text, value.range.start), end: offsetAt(text, value.range.end) }))
    .sort((left, right) => left.start - right.start);
  let end = -1;
  for (const value of ordered) {
    assert.ok(value.start >= end, "Private rename edits cannot overlap");
    assert.equal(text.slice(value.start, value.end), "#old");
    assert.equal(value.newText, replacement, "Both accepted spellings emit exactly one #");
    end = value.end;
  }
  return ordered;
}
function apply(text, edits) {
  for (const value of [...edits].reverse()) text = text.slice(0, value.start) + value.newText + text.slice(value.end);
  return text;
}
async function request(client, method, params) {
  const response = await client.request(method, params);
  assert.equal(response.error, undefined, method + ": " + JSON.stringify(response.error));
  return response.result;
}
async function diagnostics(client, start, version) {
  const deadline = performance.now() + 20_000;
  while (performance.now() < deadline) {
    const found = client.notifications.slice(start).find((item) => item.method === "textDocument/publishDiagnostics" &&
      sameUri(item.params.uri, uri()) && item.params.version === version);
    if (found) return found.params.diagnostics;
    await delay(25);
  }
  throw new Error("No diagnostics barrier for document version " + version);
}

const report = { workspace, server, clients: [] };
for (const configuration of [
  { mode: "full", documentChanges: true, lineEnding: "LF" },
  { mode: "full", documentChanges: true, lineEnding: "CRLF" },
  { mode: "full", documentChanges: false, lineEnding: "LF" },
  { mode: "full", documentChanges: undefined, lineEnding: "LF" },
  { mode: "interop-only", documentChanges: true, lineEnding: "LF" },
  { mode: "interop-only", documentChanges: false, lineEnding: "LF" },
  { mode: "interop-only", documentChanges: undefined, lineEnding: "LF" },
]) {
  const { mode, documentChanges, lineEnding } = configuration;
  const client = new LspStdioClient("dotnet", [server, "--language-features", mode, "--diagnostics",
    mode === "full" ? "all" : "sharpts-only"], { cwd: workspace });
  let failure;
  let phase = "initialize";
  let currentText = "";
  let version = 0;
  const cases = [];
  const runCase = async (name, action) => {
    phase = name;
    try { const value = await action(); cases.push(name); return value; }
    catch (error) { throw new Error(`${mode} documentChanges=${documentChanges} ${lineEnding}: ${name} ` +
      `(version=${version}, suffix=${JSON.stringify(currentText.slice(-160))}): ${error.message}`, { cause: error }); }
  };
  try {
    const initialized = await request(client, "initialize", {
      processId: process.pid, rootUri: pathToFileURL(workspace).href,
      workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "private-rename" }],
      capabilities: {
        workspace: { didChangeWatchedFiles: { dynamicRegistration: true },
          ...(documentChanges === undefined ? {} : { workspaceEdit: { documentChanges } }) },
        textDocument: { rename: { prepareSupport: true }, publishDiagnostics: { versionSupport: true } },
      },
    });
    assert.equal(Boolean(initialized.capabilities.renameProvider), mode === "full");
    client.notify("initialized", {});
    async function setSource(text) {
      currentText = lineEnding === "CRLF" ? text.replaceAll("\r\n", "\n").replaceAll("\n", "\r\n") : text;
      const start = client.notifications.length;
      version++;
      if (version === 1) client.notify("textDocument/didOpen", {
        textDocument: { uri: uri(), languageId: "typescript", version, text: currentText },
      });
      else client.notify("textDocument/didChange", {
        textDocument: { uri: uri(), version }, contentChanges: [{ text: currentText }],
      });
      return diagnostics(client, start, version);
    }
    const at = (marker = "read", spelling = "#old") => {
      const range = markerRange(currentText, marker, spelling);
      return { textDocument: { uri: uri() }, position: { ...range.start, character: range.start.character + 1 } };
    };
    const prepare = (marker, spelling) => request(client, "textDocument/prepareRename", at(marker, spelling));
    const rename = (newName, marker, spelling) => request(client, "textDocument/rename", { ...at(marker, spelling), newName });
    async function verifyLexicalRename() {
      assert.deepEqual(await setSource("const /*declaration*/lexical = 1;\n/*read*/lexical;\n"), []);
      assert.ok(await prepare("read", "lexical"));
      const result = await rename("renamedLexical", "read", "lexical");
      assert.ok(result?.changes, "Existing lexical changes remain available");
      assert.ok(result.documentChanges === undefined || result.documentChanges === null,
        "Negotiating private versioned edits does not replace the existing lexical edit contract");
      const entries = Object.entries(result.changes);
      assert.equal(entries.length, 1);
      assert.ok(sameUri(entries[0][0], uri()));
      const edits = entries[0][1];
      assert.equal(edits.length, 2);
      assert.deepEqual(edits.map((edit) => rangeKey(edit.range)).sort(),
        ["declaration", "read"].map((marker) => rangeKey(markerRange(currentText, marker, "lexical"))).sort());
      for (const edit of edits) assert.equal(edit.newText, "renamedLexical");
      const ordered = edits.map((edit) => ({ ...edit, start: offsetAt(currentText, edit.range.start),
        end: offsetAt(currentText, edit.range.end) })).sort((left, right) => left.start - right.start);
      assert.deepEqual(await setSource(apply(currentText, ordered)), [], "Applied lexical edit checks cleanly");
    }
    await setSource(base);
    if (mode === "interop-only") {
      await runCase("rename-methods-are-unregistered", async () => {
        for (const method of ["textDocument/prepareRename", "textDocument/rename"]) {
          const result = await client.request(method, { ...at(), newName: "new" });
          assert.equal(result.error?.code, -32601);
        }
      });
      report.clients.push({ ...configuration, cases, methodsAbsent: true });
      continue;
    }
    if (documentChanges !== true) {
      await runCase("private-feature-refuses-unversioned-client", async () => {
        assert.equal(await prepare(), null);
        assert.equal(await rename("new"), null);
      });
      await runCase("ordinary-lexical-rename-keeps-compatibility-without-versioned-edits", verifyLexicalRename);
      report.clients.push({ ...configuration, cases, privateRefused: true, lexicalPreserved: true });
      continue;
    }

    await runCase("whole-private-token-prepare-and-one-hash-edit-convention", async () => {
      const prepared = await prepare();
      assert.deepEqual(prepared.range, markerRange(currentText, "read"));
      assert.equal(prepared.placeholder, "#old");
      const plain = await rename("new");
      const prefixed = await rename("#new");
      assert.deepEqual(plain, prefixed, "Both accepted spellings have identical deterministic edits");
      assertVersionedEdit(plain, currentText, version, selectedMarkers);
      const keyword = await rename("class");
      assert.deepEqual(keyword, await rename("#class"), "Keyword IdentifierName accepts both spellings");
      const keywordEdits = assertVersionedEdit(keyword, currentText, version, selectedMarkers, "#class");
      assert.deepEqual(await setSource(apply(currentText, keywordEdits)), [], "Applied #class keyword edits check cleanly");
      const keywordPrepare = await prepare("read", "#class");
      assert.equal(keywordPrepare.placeholder, "#class");
      assert.deepEqual(keywordPrepare.range, markerRange(currentText, "read", "#class"));
      await setSource(base);
      const longer = await rename("longerName");
      assert.deepEqual(longer, await rename("#longerName"));
      const edits = assertVersionedEdit(longer, currentText, version, selectedMarkers, "#longerName");
      const oldOther = currentText.slice(currentText.indexOf("class Other"));
      const applied = apply(currentText, edits);
      assert.equal(applied.slice(applied.indexOf("class Other")), oldOther,
        "Unrelated owner, same-spelled private names, comments and string text remain unchanged");
      assert.deepEqual(await setSource(applied), [], "Applied edit reparses and checks through fresh diagnostics");
      const rebound = await prepare("read", "#longerName");
      assert.equal(rebound.placeholder, "#longerName");
      assert.deepEqual(rebound.range, markerRange(currentText, "read", "#longerName"));
    });

    for (const [name, source, markers] of [
      ["instance-method", "class Box { /*declaration*/#old(value: number): number { return value; } " +
        "run(): number { return this./*read*/#old(1); } } new Box().run();", ["declaration", "read"]],
      ["static-field", "class Box { static /*declaration*/#old: number = 1; " +
        "static run(): number { Box./*write*/#old = 2; return Box./*read*/#old; } } Box.run();", ["declaration", "write", "read"]],
      ["static-method", "class Box { static /*declaration*/#old(value: number): number { return value; } " +
        "static run(): number { return Box./*read*/#old(1); } } Box.run();", ["declaration", "read"]],
      ["class-expression", "const Model = class { /*declaration*/#old: number = 1; " +
        "run(): number { return this./*read*/#old; } }; new Model().run();", ["declaration", "read"]],
      ["class-inside-ordinary-function", "function make(): number { class Local { /*declaration*/#old: number = 1; " +
        "run(): number { return this./*read*/#old; } } return new Local().run(); } make();", ["declaration", "read"]],
    ]) {
      await runCase(name, async () => {
        assert.deepEqual(await setSource(source), []);
        assert.equal((await prepare()).placeholder, "#old");
        const edits = assertVersionedEdit(await rename("new"), currentText, version, markers);
        assert.deepEqual(await setSource(apply(currentText, edits)), [], "Applied supported-domain edit checks cleanly");
      });
    }

    await runCase("invalid-reserved-and-cross-facet-collision-names", async () => {
      await setSource(base);
      for (const invalid of ["", "#", "##new", "1bad", "bad-name", "bad name", "constructor", "#constructor", "taken", "#taken"])
        assert.equal(await rename(invalid), null, "Refused replacement " + JSON.stringify(invalid));
    });
    for (const [name, source, spelling] of [
      ["selected-owner-in-another-private-environment", "class Outer { make(): any { class Inner { /*read*/#old: number = 1; } return new Inner(); } }", "#old"],
      ["owner-contains-nested-class", "class Box { #old: number = 1; run(): number { class Inner {} return this./*read*/#old; } }", "#old"],
      ["unresolved-private-occurrence", "class Box { #old: number = 1; run(loose: any): number { loose.#old; return this./*read*/#old; } }", "#old"],
      ["recovered-domain", "class Box { #old: number = 1; run(): number { return this./*read*/#old; }", "#old"],
      ["unrelated-target-document-parse-error", "class Box { #old: number = 1; run(): number { return this./*read*/#old; } } const = 1;", "#old"],
      ["private-compound-original-is-parser-rejected", "class Box { #old: number = 1; run(): number { this.#old += 1; return this./*read*/#old; } }", "#old"],
      ["private-logical-original-is-parser-rejected", "class Box { #old: number = 1; run(): number { this.#old ||= 1; return this./*read*/#old; } }", "#old"],
      ["private-update-original-is-parser-rejected", "class Box { #old: number = 1; run(): number { this.#old++; return this./*read*/#old; } }", "#old"],
      ["public-member-of-branded-class", "class Box { #brand: number = 1; old: number = 1; run(): number { return this./*read*/old; } }", "old"],
      ["typescript-private", "class Box { private old: number = 1; run(): number { return this./*read*/old; } }", "old"],
      ["protected-member", "class Box { protected old: number = 1; run(): number { return this./*read*/old; } }", "old"],
      ["constructor-parameter-property", "class Box { constructor(public old: number) {} run(): number { return this./*read*/old; } }", "old"],
    ]) {
      await runCase(name, async () => {
        await setSource(source);
        assert.equal(await prepare("read", spelling), null);
        assert.equal(await rename("new", "read", spelling), null);
      });
    }

    await runCase("unrelated-broken-workspace-does-not-block-a-local-private-domain", async () => {
      await setSource(base);
      await mkdir(join(workspace, "broken"), { recursive: true });
      const broken = join(workspace, "broken/tsconfig.json");
      await writeFile(broken, "{ invalid unrelated config");
      const changedStart = client.notifications.length;
      client.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("broken/tsconfig.json"), type: 1 }] });
      await diagnostics(client, changedStart, version);
      assert.equal((await prepare()).placeholder, "#old");
      assertVersionedEdit(await rename("new"), currentText, version, selectedMarkers);
      await unlink(broken);
      const deletedStart = client.notifications.length;
      client.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("broken/tsconfig.json"), type: 3 }] });
      await diagnostics(client, deletedStart, version);
    });
    let cancellationOutcome;
    await runCase("actual-cancellation-preserves-peer-and-fresh-versioned-edit", async () => {
      const invalidatedStart = client.notifications.length;
      client.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri(), type: 2 }] });
      await diagnostics(client, invalidatedStart, version);
      const cancelled = client.beginRequest("textDocument/rename", { ...at(), newName: "new" });
      const peer = rename("new");
      void peer.catch(() => {});
      assert.ok(cancelled.cancel(), "Actual $/cancelRequest notification was sent");
      const response = await cancelled.response;
      if (response.error) {
        assert.equal(response.error.code, -32800);
        cancellationOutcome = "request-cancelled";
      } else if (response.result === null) cancellationOutcome = "no-result-during-invalidation";
      else {
        assertVersionedEdit(response.result, currentText, version, selectedMarkers);
        cancellationOutcome = "completed-before-cancel";
      }
      assertVersionedEdit(await peer, currentText, version, selectedMarkers);
      assertVersionedEdit(await rename("new"), currentText, version, selectedMarkers);
    });
    await runCase("versioned-client-and-private-service-preserve-ordinary-lexical-rename", verifyLexicalRename);
    report.clients.push({ ...configuration, cases, versionedEditsOnly: true, wholePrivateToken: true,
      appliedFreshDiagnostics: true, unrelatedSameNamePreserved: true, brokenWorkspaceIndependent: true,
      cancellationOutcome, peerAndFreshSurvived: true });
  } catch (error) {
    failure = error;
    await writeFile(join(workspace, "failure.json"), JSON.stringify({ configuration, phase, version, currentText,
      message: error.message, notifications: client.notifications.slice(-12), report }, null, 2) + "\n").catch(() => {});
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
