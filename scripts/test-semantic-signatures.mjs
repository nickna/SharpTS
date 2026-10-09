import assert from "node:assert/strict";
import { mkdir, mkdtemp, writeFile } from "node:fs/promises";
import { dirname, resolve, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

const repository = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const server = resolve(repository, process.argv[2] ?? "src/SharpTS.LanguageServer/bin/Release/net10.0/SharpTS.LanguageServer.dll");
await mkdir(join(repository, "artifacts/semantic-signature-smoke"), { recursive: true });
const workspace = await mkdtemp(join(repository, "artifacts/semantic-signature-smoke/run-"));
const dependency = "export function fromDependency(diskValue: number): number { return diskValue; }\n";
const dirtyDependency = "export function fromDependency(dirtyValue: string): string { return dirtyValue; }\n";
const header = "import { fromDependency as imported } from './dep';\r\n" +
  "function pair(first: number, second: string): number { return first; }\r\n" +
  "function choose(value: number): boolean;\r\n" +
  "function choose(value: string): boolean;\r\n" +
  "function choose(value: any): boolean { return true; }\r\n" +
  "function identity<T>(value: T): T { return value; }\r\n" +
  "function many(head: number, ...tail: string[]): number { return head; }\r\n" +
  "declare function utf16(emoji: '😀', count: number): number;\r\n" +
  "declare class Build { constructor(value: number); constructor(value: string); }\r\n";
await writeFile(join(workspace, "tsconfig.json"), JSON.stringify({
  compilerOptions: { noLib: true, types: [], experimentalDecorators: true }, include: ["*.ts"],
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
const caretMarker = "/*caret*/";
function marked(text) {
  const offset = text.indexOf(caretMarker);
  assert.notEqual(offset, -1, "Missing caret marker");
  assert.equal(text.indexOf(caretMarker, offset + 1), -1, "Duplicate caret marker");
  return { text: text.slice(0, offset) + text.slice(offset + caretMarker.length), offset };
}
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
function parameterText(signature, index, labelOffsets) {
  const parameter = signature.parameters?.[index];
  assert.ok(parameter, "Missing parameter " + index + ": " + JSON.stringify(signature));
  if (!labelOffsets) {
    assert.equal(typeof parameter.label, "string", "String-label capability fallback");
    return parameter.label;
  }
  assert.ok(Array.isArray(parameter.label), "Negotiated label offsets");
  assert.equal(parameter.label.length, 2);
  const [start, end] = parameter.label;
  assert.ok(Number.isInteger(start) && Number.isInteger(end) && start >= 0 && end > start && end <= signature.label.length,
    "UTF-16 label offsets stay inside the exact signature label");
  assert.ok(!/[\uDC00-\uDFFF]/.test(signature.label[start]), "Parameter start does not split a surrogate pair");
  assert.ok(!/[\uD800-\uDBFF]/.test(signature.label[end - 1]), "Parameter end does not split a surrogate pair");
  return signature.label.slice(start, end);
}
function assertHelp(help, negotiated) {
  assert.ok(help, "Expected signature help");
  assert.ok(help.signatures?.length > 0 && help.signatures.length <= 32, "Bounded public candidate list");
  for (const signature of help.signatures) {
    assert.equal(typeof signature.label, "string");
    assert.ok(signature.label.length > 0 && signature.label.length <= 4096, "Bounded signature label");
    for (let index = 0; index < (signature.parameters?.length ?? 0); index++)
      assert.match(parameterText(signature, index, negotiated), /:/);
    if (!negotiated) assert.equal(signature.activeParameter, undefined, "Per-signature active parameter is negotiated");
  }
  return help;
}
function assertNoWinner(help) {
  // An omitted activeSignature may cause a client to highlight candidate zero by default;
  // the server must not publish that presentation default as a checker-selected overload.
  assert.ok(help.activeSignature === undefined || help.activeSignature === null,
    "Candidates-only/recovered call has no selected overload");
}
function assertActive(help, rawIndex, negotiated, signatureIndex = help.activeSignature ?? 0) {
  assert.equal(help.activeParameter, rawIndex, "Top-level active parameter");
  if (negotiated) assert.equal(help.signatures[signatureIndex].activeParameter, rawIndex,
    "Negotiated per-signature active parameter");
}

const report = { workspace, server, clients: [] };
for (const configuration of [
  { mode: "full", negotiated: true },
  { mode: "full", negotiated: false },
  { mode: "interop-only", negotiated: true },
  { mode: "interop-only", negotiated: false },
]) {
  const { mode, negotiated } = configuration;
  const client = new LspStdioClient("dotnet", [server, "--language-features", mode, "--diagnostics",
    mode === "full" ? "all" : "sharpts-only"], { cwd: workspace });
  let version = 0;
  let currentText = "";
  let currentOffset = 0;
  let failure;
  let cancellationOutcome;
  let clrCall = "unavailable";
  const checkedCases = [];
  try {
    const initialized = await request(client, "initialize", {
      processId: process.pid, rootUri: pathToFileURL(workspace).href,
      workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "semantic-signatures" }],
      // Deliberately omit hover and completion capabilities: signature-only clients must initialize.
      capabilities: { textDocument: {
        signatureHelp: { contextSupport: true, signatureInformation: {
          parameterInformation: { labelOffsetSupport: negotiated }, activeParameterSupport: negotiated,
        } },
        publishDiagnostics: { versionSupport: true },
      } },
    });
    const provider = initialized.capabilities.signatureHelpProvider;
    assert.ok(provider, "Signature help remains registered in both modes");
    for (const trigger of ["(", ","]) assert.ok(provider.triggerCharacters?.includes(trigger), "Legacy trigger " + trigger);
    client.notify("initialized", {});
    async function setSource(markedText) {
      const fixture = marked(markedText);
      currentText = fixture.text;
      currentOffset = fixture.offset;
      const start = client.notifications.length;
      version++;
      if (version === 1) client.notify("textDocument/didOpen", {
        textDocument: { uri: uri("main.ts"), languageId: "typescript", version, text: currentText },
      });
      else client.notify("textDocument/didChange", {
        textDocument: { uri: uri("main.ts"), version }, contentChanges: [{ text: currentText }],
      });
      return diagnostics(client, start, "main.ts", version);
    }
    function params(context = { triggerKind: 1, isRetrigger: false }) {
      return { textDocument: { uri: uri("main.ts") }, position: positionAt(currentText, currentOffset), context };
    }
    async function help(context) { return request(client, "textDocument/signatureHelp", params(context)); }
    async function semanticCase(name, fixture, validate, context) {
      try {
        await setSource(fixture);
        const result = await help(context);
        if (mode === "full") validate(assertHelp(result, negotiated));
        else assert.equal(result, null, "Interop refuses ordinary signature: " + name);
        checkedCases.push(name);
        return result;
      } catch (error) {
        throw new Error(`${name} (${mode}, negotiated=${negotiated}, version=${version}, ` +
          `source=${JSON.stringify(currentText.slice(-140))}): ${error.message}`, { cause: error });
      }
    }

    await semanticCase("ordinary-pair", header + "/* 😀 */ pair(/*caret*/1, 'text');\r\n", (result) => {
      assert.equal(result.signatures.length, 1);
      assert.match(parameterText(result.signatures[0], 0, negotiated), /^first: number$/);
      assert.match(parameterText(result.signatures[0], 1, negotiated), /^second: string$/);
      assert.equal(result.activeSignature, 0);
      assertActive(result, 0, negotiated);
    }, { triggerKind: 2, triggerCharacter: "(", isRetrigger: false });

    await semanticCase("actual-overload-winner", header + "choose(/*caret*/'text');", (result) => {
      assert.equal(result.signatures.length, 2);
      assert.match(parameterText(result.signatures[0], 0, negotiated), /^value: number$/);
      assert.match(parameterText(result.signatures[1], 0, negotiated), /^value: string$/);
      assert.equal(result.activeSignature, 1, "Actual checker ordinal, even with identical return types");
      assertActive(result, 0, negotiated, 1);
      assert.ok(!result.signatures.some((signature) => /value: any/.test(signature.label)), "Implementation overload remains hidden");
    });

    await semanticCase("generic-instantiation", header + "identity<number>(/*caret*/1);", (result) => {
      assert.match(parameterText(result.signatures[0], 0, negotiated), /^value: number$/);
      assert.match(result.signatures[0].label, /number/);
      assert.equal(result.activeSignature, 0);
    });
    await semanticCase("generic-type-argument-comma", header +
      "function combine<A, B>(left: A, right: B): A { return left; }\r\n" +
      "combine<number, string>(1, /*caret*/'x');", (result) => {
      assert.equal(result.signatures[0].parameters.length, 2);
      assert.match(parameterText(result.signatures[0], 1, negotiated), /^right: string$/);
      assertActive(result, 1, negotiated);
    });
    await semanticCase("invalid-overload-candidates", header + "choose(/*caret*/true);", (result) => {
      assert.equal(result.signatures.length, 2);
      assertNoWinner(result);
    });

    const nested = header + "pair(\r\n  identity<number>(/*caret*/1),\r\n  `a,b ${identity<string>('x,y')}`\r\n);";
    const previous = await semanticCase("nested-innermost", nested, (result) => {
      assert.equal(result.signatures[0].parameters.length, 1);
      assert.match(parameterText(result.signatures[0], 0, negotiated), /^value: number$/);
      assertActive(result, 0, negotiated);
    });
    await semanticCase("outer-commas-and-retrigger", header +
      "pair(\r\n  identity<number>(1), /* a, ignored comment */\r\n  /*caret*/`a,b ${identity<string>('x,y')}`\r\n);", (result) => {
      assert.equal(result.signatures[0].parameters.length, 2);
      assertActive(result, 1, negotiated);
    }, { triggerKind: 3, isRetrigger: true, ...(previous ? { activeSignatureHelp: previous } : {}) });

    await semanticCase("utf16-label-offsets", header + "utf16('😀', /*caret*/1);", (result) => {
      const signature = result.signatures[0];
      assert.equal(parameterText(signature, 0, negotiated), 'emoji: "😀"');
      assert.equal(parameterText(signature, 1, negotiated), "count: number");
      if (negotiated) {
        const expected = signature.label.indexOf("count: number");
        assert.ok(signature.parameters[1].label[0] === expected, "Offsets count UTF-16 units, including the earlier surrogate pair");
      }
      assertActive(result, 1, negotiated);
    }, { triggerKind: 2, triggerCharacter: ",", isRetrigger: true });

    await semanticCase("rest-parameter", header + "many(1, 'a', /*caret*/'b');", (result) => {
      assert.equal(result.signatures[0].parameters.length, 2);
      assert.match(parameterText(result.signatures[0], 1, negotiated), /^\.\.\.tail: Array<string>$/);
      assertActive(result, 1, negotiated);
    });
    await semanticCase("optional-call", header + "pair?.(1, /*caret*/'x');", (result) => assertActive(result, 1, negotiated));
    await semanticCase("completed-string-argument-interior", header + "pair(1, 'a,/*caret*/b');",
      (result) => assertActive(result, 1, negotiated));
    await semanticCase("completed-template-argument-interior", header + "pair(1, `a,/*caret*/b`);",
      (result) => assertActive(result, 1, negotiated));
    await semanticCase("ambient-constructor", header + "new Build(/*caret*/'text');", (result) => {
      assert.equal(result.signatures.length, 2);
      assert.equal(result.activeSignature, 1);
      assert.ok(result.signatures.every((signature) => signature.label.startsWith("new ")));
    });
    await semanticCase("private-call", "class C { #method(value: number): number { return value; } " +
      "inspect(): number { return this.#method(/*caret*/1); } }", (result) => {
      assert.equal(result.signatures.length, 1);
      assert.match(parameterText(result.signatures[0], 0, negotiated), /^value: number$/);
      assert.equal(result.activeSignature, 0);
    });

    for (const [name, suffix, index, count] of [
      ["unfinished-call", "pair(/*caret*/", 0, 1],
      ["unfinished-second-argument", "pair(1,   /*caret*/", 1, 1],
      ["recovery-hole", "pair(1, , /*caret*/", 2, 1],
      ["unfinished-overloads", "choose(/*caret*/", 0, 2],
      ["unfinished-constructor", "new Build(/*caret*/", 0, 2],
      ["unfinished-nested", "pair(1, identity<string>(/*caret*/", 0, 1],
    ]) {
      await semanticCase(name, header + suffix, (result) => {
        assert.equal(result.signatures.length, count);
        assertNoWinner(result);
        // A non-rest signature has no parameter at an out-of-range raw argument index.
        if (index < result.signatures[0].parameters.length) assertActive(result, index, negotiated);
        else assert.ok(result.activeParameter === undefined || result.activeParameter === null);
      });
    }

    for (const [name, suffix] of [
      ["comment-interior", "pair(1, /* comment, /*caret*/ ignored */ 'x');"],
      ["string-with-faux-call", "const text = 'pair(,/*caret*/';"],
      ["template-with-faux-call", "const text = `pair(,/*caret*/`;"],
      ["unfinished-string", "pair(1, 'unfinished/*caret*/"],
      ["after-call-close", "pair(1, 'x')/*caret*/;"],
      ["no-new-delimiters", "new Build/*caret*/;"],
      ["unknown-callee", "missing(/*caret*/);"],
      // The outer call rejects its missing required argument before checking the inner
      // invocation. Signature help must not perform a new semantic check to invent it.
      ["unvisited-inner-after-outer-arity-failure", "pair(identity<number>(/*caret*/"],
    ]) {
      await setSource(header + suffix);
      assert.equal(await help(), null, "Refused context: " + name);
      checkedCases.push(name);
    }

    // The built-in decorator path remains independent of ordinary semantic signatures.
    await setSource('@DotNetType(/*caret*/"System.String")\r\ndeclare class NetString {}\r\n');
    const decorator = await help({ triggerKind: 2, triggerCharacter: "(", isRetrigger: false });
    assert.equal(decorator.signatures[0].label, "DotNetType(typeName: string)");
    assert.equal(decorator.activeSignature, 0);
    assert.equal(decorator.activeParameter, 0);

    // CLR signatures are included only when captured by the checker; this smoke does not
    // promise support for every reflected overload or synthesize a signature from reflection.
    await setSource("import { StringBuilder as SB } from 'dotnet:System.Text.StringBuilder';\r\n" +
      "const builder = new SB();\r\nbuilder.append(/*caret*/'x');\r\n");
    const clr = await help();
    if (mode === "interop-only") assert.equal(clr, null);
    else if (clr !== null) { assertHelp(clr, negotiated); clrCall = "captured-candidates"; }

    if (mode === "full") {
      await setSource(header + "imported(/*caret*/1);");
      const disk = assertHelp(await help(), negotiated);
      assert.match(parameterText(disk.signatures[0], 0, negotiated), /^diskValue: number$/);
      const concurrent = await Promise.all(Array.from({ length: 4 }, () => help()));
      concurrent.forEach((result) => assert.deepEqual(result, disk, "Concurrent unchanged requests reuse equivalent checked facts"));
      const dirtyStart = client.notifications.length;
      client.notify("textDocument/didOpen", {
        textDocument: { uri: uri("dep.ts"), languageId: "typescript", version: 1, text: dirtyDependency },
      });
      assert.deepEqual(await diagnostics(client, dirtyStart, "dep.ts", 1), []);
      const dirty = assertHelp(await help(), negotiated);
      assert.match(parameterText(dirty.signatures[0], 0, negotiated), /^dirtyValue: string$/);
      assertNoWinner(dirty); // The unchanged number argument is now invalid; retain candidates only.
      const closeStart = client.notifications.length;
      client.notify("textDocument/didClose", { textDocument: { uri: uri("dep.ts") } });
      await diagnostics(client, closeStart, "main.ts", version);
      const restored = assertHelp(await help(), negotiated);
      assert.match(parameterText(restored.signatures[0], 0, negotiated), /^diskValue: number$/);
      assert.equal(restored.activeSignature, 0);

      // Wire cancellation has an unavoidable completion race. A peer and a fresh request
      // must survive; deterministic service cancellation assertions live in unit tests.
      await setSource(header + "pair(1, /*caret*/");
      const cancelled = client.beginRequest("textDocument/signatureHelp", params());
      const peer = help();
      // Keep cleanup failures from turning a useful primary assertion into an unhandled rejection.
      void peer.catch(() => {});
      const cancelSent = cancelled.cancel();
      const response = await cancelled.response;
      if (response.error) {
        assert.equal(response.error.code, -32800);
        cancellationOutcome = "request-cancelled";
      } else if (response.result === null) cancellationOutcome = "no-result-during-cancellation";
      else {
        assertNoWinner(assertHelp(response.result, negotiated));
        cancellationOutcome = "completed-before-cancel";
      }
      assert.ok(cancelSent, "Actual $/cancelRequest notification was sent");
      assertActive(assertHelp(await peer, negotiated), 1, negotiated);
      assertActive(assertHelp(await help(), negotiated), 1, negotiated);
    }
    report.clients.push({ mode, labelOffsetSupport: negotiated, activeParameterSupport: negotiated,
      initializedWithoutHoverOrCompletion: true, checkedCases, decoratorSignature: true,
      ordinarySignatures: mode === "full", dirtyDependencyAndClose: mode === "full",
      cancellationOutcome, freshAfterCancellation: mode === "full", clrCall });
  } catch (error) {
    failure = error;
    error.message = `${mode} negotiated=${negotiated} version ${version} (${currentText.slice(-140)}): ${error.message}; ` +
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
