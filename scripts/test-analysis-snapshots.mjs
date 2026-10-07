import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile, stat, utimes } from "node:fs/promises";
import { dirname, resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const server = resolve(repository, process.argv[2] ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
await mkdir(join(repository, "artifacts/analysis-smoke"), { recursive: true });
const workspace = await mkdtemp(join(repository, "artifacts/analysis-smoke/run-"));
const source = "import { value } from './dep';\nconst result: number = value;\n";
const original = "export const value: number = 123;\n";
const changed = "export const value: string = 'x';\n";
assert.equal(original.length, changed.length);
await writeFile(join(workspace, "tsconfig.json"), JSON.stringify({
  compilerOptions: { noLib: true, types: [] }, include: ["*.ts"],
}));
await writeFile(join(workspace, "main.ts"), source);
await writeFile(join(workspace, "dep.ts"), original);
const memberDependency = "export class Base {\r\n  /* 😀 */ field: number = 1;\r\n  read(): number { return this.field; }\r\n}\r\nexport class Derived extends Base {}\r\n";
const memberSource = "import { Derived as Renamed } from './member-dep';\nconst item = new Renamed();\nitem./*field*/field;\nitem./*method*/read();\nconst loose: any = item; loose./*any*/field;\nconst shape = { field: 1 }; shape./*structural*/field;\n";
await writeFile(join(workspace, "member-dep.ts"), memberDependency);
await writeFile(join(workspace, "member-main.ts"), memberSource);
const uri = (name) => pathToFileURL(join(workspace, name)).href;
const sameUri = (left, right) => process.platform === "win32"
  ? left.toLowerCase() === right.toLowerCase() : left === right;
const asList = (value) => Array.isArray(value) ? value : value ? [value] : [];
const positionAt = (text, offset) => {
  const prefix = text.slice(0, offset);
  const lastLine = prefix.lastIndexOf("\n");
  return { line: prefix.split("\n").length - 1, character: offset - lastLine - 1 };
};
const memberPosition = (marker) => {
  const comment = `/*${marker}*/`;
  const offset = memberSource.indexOf(comment);
  assert.notEqual(offset, -1, `missing marker ${marker}`);
  return { textDocument: { uri: uri("member-main.ts") }, position: positionAt(memberSource, offset + comment.length + 1) };
};
const expectedMember = (text, name) => {
  const offset = text.indexOf(name === "field" ? "field:" : "read():");
  assert.notEqual(offset, -1);
  return { uri: uri("member-dep.ts"), range: { start: positionAt(text, offset), end: positionAt(text, offset + name.length) } };
};
const assertMember = (result, expected) => {
  const locations = asList(result);
  assert.equal(locations.length, 1);
  assert.ok(sameUri(locations[0].uri, expected.uri));
  assert.deepEqual(locations[0].range, expected.range);
};
const requestPosition = { textDocument: { uri: uri("main.ts") }, position: { line: 1, character: 23 } };
const report = { workspace, server, full: {}, interopOnly: {} };

async function request(client, method, params) {
  const response = await client.request(method, params);
  assert.equal(response.error, undefined, `${method}: ${JSON.stringify(response.error)}`);
  return response.result;
}
async function initialize(client) {
  const result = await request(client, "initialize", {
    processId: process.pid, rootUri: pathToFileURL(workspace).href,
    workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "snapshot-smoke" }],
    capabilities: {
      workspace: { didChangeWatchedFiles: { dynamicRegistration: true }, workspaceFolders: true },
      textDocument: { definition: {}, references: {}, rename: { prepareSupport: true },
        hover: { contentFormat: ["markdown"] }, documentSymbol: {}, publishDiagnostics: { versionSupport: true } },
    },
  });
  client.notify("initialized", {});
  for (const key of ["documentFormattingProvider", "documentRangeFormattingProvider", "documentOnTypeFormattingProvider"])
    assert.ok(!result.capabilities[key], key);
  return result.capabilities;
}
async function diagnosticsAfter(client, start, predicate, { document = "main.ts", version = 1 } = {}) {
  const end = performance.now() + 20_000;
  while (performance.now() < end) {
    const match = client.notifications.slice(start).find((item) => item.method === "textDocument/publishDiagnostics" &&
      sameUri(item.params.uri, uri(document)) && item.params.version === version && predicate(item.params.diagnostics));
    if (match) return match.params.diagnostics;
    await delay(25);
  }
  throw new Error(`Expected diagnostics were not published: ${JSON.stringify(client.notifications.slice(start))}`);
}

const full = new LspStdioClient("dotnet", [server, "--language-features", "full", "--diagnostics", "all"], { cwd: workspace });
try {
  const caps = await initialize(full);
  assert.ok(caps.definitionProvider && caps.referencesProvider && caps.renameProvider);
  full.notify("textDocument/didOpen", { textDocument: { uri: uri("main.ts"), languageId: "typescript", version: 1, text: source } });
  await diagnosticsAfter(full, 0, (items) => items.length === 0);
  const definitions = (await Promise.all(Array.from({ length: 4 }, () => request(full, "textDocument/definition", requestPosition)))).map(asList);
  assert.equal(definitions[0].length, 1);
  assert.ok(sameUri(definitions[0][0].uri, uri("dep.ts")));
  definitions.forEach((result) => assert.deepEqual(result, definitions[0]));
  const references = await request(full, "textDocument/references", { ...requestPosition, context: { includeDeclaration: true } });
  assert.equal(references.length, 3);
  assert.ok(await request(full, "textDocument/prepareRename", requestPosition));
  const rename = await request(full, "textDocument/rename", { ...requestPosition, newName: "nextValue" });
  assert.equal(Object.values(rename.changes).flat().length, 3);

  const stamp = await stat(join(workspace, "dep.ts"));
  await writeFile(join(workspace, "dep.ts"), changed);
  await utimes(join(workspace, "dep.ts"), stamp.atime, stamp.mtime);
  const changedStart = full.notifications.length;
  full.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("dep.ts"), type: 2 }] });
  const errors = await diagnosticsAfter(full, changedStart, (items) => items.some((item) => /not assignable/i.test(item.message)));
  const dirtyStart = full.notifications.length;
  full.notify("textDocument/didOpen", { textDocument: { uri: uri("dep.ts"), languageId: "typescript", version: 1, text: original } });
  await diagnosticsAfter(full, dirtyStart, (items) => items.length === 0);
  const closeStart = full.notifications.length;
  full.notify("textDocument/didClose", { textDocument: { uri: uri("dep.ts") } });
  await diagnosticsAfter(full, closeStart, (items) => items.some((item) => /not assignable/i.test(item.message)));

  await writeFile(join(workspace, "consumer.ts"), "import { value } from './dep'; value;\n");
  full.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("consumer.ts"), type: 1 }] });
  const expanded = await request(full, "textDocument/references", { ...requestPosition, context: { includeDeclaration: true } });
  assert.equal(expanded.length, 5);
  assert.ok(full.registeredMethods.includes("workspace/didChangeWatchedFiles"));
  assert.ok(!full.registeredMethods.some((method) => /formatting/i.test(method)));

  const memberStart = full.notifications.length;
  full.notify("textDocument/didOpen", { textDocument: { uri: uri("member-main.ts"), languageId: "typescript", version: 1, text: memberSource } });
  await diagnosticsAfter(full, memberStart, (items) => items.length === 0, { document: "member-main.ts" });
  const memberDefinitions = await Promise.all(Array.from({ length: 4 }, () => request(full, "textDocument/definition", memberPosition("field"))));
  memberDefinitions.forEach((result) => assertMember(result, expectedMember(memberDependency, "field")));
  assertMember(await request(full, "textDocument/definition", memberPosition("method")), expectedMember(memberDependency, "read"));
  assert.equal(asList(await request(full, "textDocument/definition", memberPosition("any"))).length, 0);
  assert.equal(asList(await request(full, "textDocument/definition", memberPosition("structural"))).length, 0);

  const dirtyMember = "// dirty open dependency\r\n" + memberDependency;
  const openMemberStart = full.notifications.length;
  full.notify("textDocument/didOpen", { textDocument: { uri: uri("member-dep.ts"), languageId: "typescript", version: 1, text: dirtyMember } });
  await diagnosticsAfter(full, openMemberStart, (items) => items.length === 0, { document: "member-dep.ts" });
  assertMember(await request(full, "textDocument/definition", memberPosition("field")), expectedMember(dirtyMember, "field"));
  const dirtyMemberV2 = "// version two\r\n" + dirtyMember;
  const changeMemberStart = full.notifications.length;
  full.notify("textDocument/didChange", { textDocument: { uri: uri("member-dep.ts"), version: 2 }, contentChanges: [{ text: dirtyMemberV2 }] });
  await diagnosticsAfter(full, changeMemberStart, (items) => items.length === 0, { document: "member-dep.ts", version: 2 });
  assertMember(await request(full, "textDocument/definition", memberPosition("field")), expectedMember(dirtyMemberV2, "field"));

  // Force a cold shared analysis, then exercise actual wire cancellation. A server can legally
  // complete before it observes cancellation; the gated handler tests prove the inflight case.
  full.notify("workspace/didChangeWatchedFiles", { changes: [{ uri: uri("member-dep.ts"), type: 2 }] });
  const cancelledDefinition = full.beginRequest("textDocument/definition", memberPosition("field"));
  const cancelSent = cancelledDefinition.cancel();
  const cancelledResponse = await cancelledDefinition.response;
  let cancellationOutcome;
  if (cancelledResponse.error) {
    assert.equal(cancelledResponse.error.code, -32800);
    cancellationOutcome = "request-cancelled";
  } else if (asList(cancelledResponse.result).length === 0) {
    cancellationOutcome = "no-result-during-invalidation";
  } else {
    assertMember(cancelledResponse.result, expectedMember(dirtyMemberV2, "field"));
    cancellationOutcome = "completed-before-cancel";
  }
  assertMember(await request(full, "textDocument/definition", memberPosition("field")), expectedMember(dirtyMemberV2, "field"));

  const removedMember = dirtyMemberV2.replaceAll("field", "renamed");
  const removeMemberStart = full.notifications.length;
  full.notify("textDocument/didChange", { textDocument: { uri: uri("member-dep.ts"), version: 3 }, contentChanges: [{ text: removedMember }] });
  await diagnosticsAfter(full, removeMemberStart, (items) => items.length === 0, { document: "member-dep.ts", version: 3 });
  assert.equal(asList(await request(full, "textDocument/definition", memberPosition("field"))).length, 0);
  assertMember(await request(full, "textDocument/definition", memberPosition("method")), expectedMember(removedMember, "read"));
  const closeMemberStart = full.notifications.length;
  full.notify("textDocument/didClose", { textDocument: { uri: uri("member-dep.ts") } });
  await diagnosticsAfter(full, closeMemberStart, (items) => items.length === 0, { document: "member-main.ts" });
  assertMember(await request(full, "textDocument/definition", memberPosition("field")), expectedMember(memberDependency, "field"));

  report.full = { concurrentDefinitions: 4, references: references.length, expandedReferences: expanded.length,
    renameEdits: 3, closedDiskErrors: errors.length, dirtyOverlayAndClose: "passed", watching: "registered",
    members: { concurrentDefinitions: 4, inheritedFieldAndMethod: "passed", utf16CrLfRanges: "passed", refusedAnyAndStructural: "passed",
      dirtyVersions: [1, 2, 3], removedMemberRefused: true, closeRestoredDisk: true, cancelSent, cancellationOutcome, freshAfterCancellation: true } };
} finally { await full.close(); }

const interop = new LspStdioClient("dotnet", [server, "--language-features", "interop-only"], { cwd: workspace });
try {
  const caps = await initialize(interop);
  for (const key of ["definitionProvider", "referencesProvider", "renameProvider", "documentSymbolProvider"])
    assert.ok(!caps[key], key);
  const text = '@DotNetType("System.String")\ndeclare class NetString {}\n';
  interop.notify("textDocument/didOpen", { textDocument: { uri: uri("interop.ts"), languageId: "typescript", version: 1, text } });
  const hover = await request(interop, "textDocument/hover", { textDocument: { uri: uri("interop.ts") }, position: { line: 0, character: 4 } });
  assert.match(JSON.stringify(hover), /System\.String/);
  const refused = await interop.request("textDocument/definition", { textDocument: { uri: uri("interop.ts") }, position: { line: 1, character: 15 } });
  assert.equal(refused.error?.code, -32601);
  interop.notify("textDocument/didOpen", { textDocument: { uri: uri("member-main.ts"), languageId: "typescript", version: 1, text: memberSource } });
  const refusedMember = await interop.request("textDocument/definition", memberPosition("field"));
  assert.equal(refusedMember.error?.code, -32601);
  assert.ok(!interop.registeredMethods.some((method) => /formatting/i.test(method)));
  report.interopOnly = { generalCapabilitiesAbsent: true, clrHover: "passed", definitionError: -32601, memberDefinitionError: -32601 };
} finally { await interop.close(); }
await writeFile(join(workspace, "result.json"), JSON.stringify(report, null, 2) + "\n");
console.log(JSON.stringify(report, null, 2));
