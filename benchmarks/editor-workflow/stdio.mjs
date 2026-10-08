import assert from "node:assert/strict";
import { performance } from "node:perf_hooks";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { LspStdioClient } from "../../scripts/lib/lsp-stdio.mjs";

const [serverArgument, outputArgument] = process.argv.slice(2);
assert.ok(serverArgument && outputArgument, "usage: node stdio.mjs <server.dll> <service-results-directory>");
const server = path.resolve(serverArgument), output = path.resolve(outputArgument);
const reports = [];
const summary = (samples) => ({ count: samples.length,
  medianMilliseconds: samples.map((sample) => sample.milliseconds).sort((a, b) => a - b)[Math.floor(samples.length / 2)],
  p90Milliseconds: samples.map((sample) => sample.milliseconds).sort((a, b) => a - b)[Math.ceil(samples.length * .9) - 1],
  payloadBytes: [...new Set(samples.map((sample) => sample.payloadBytes))],
  fingerprints: [...new Set(samples.map((sample) => sample.fingerprint).filter(Boolean))], samples });
for (const name of ["small", "multi-project", "default-library"]) {
  const root = path.join(output, "fixtures", name);
  const main = path.join(root, name === "multi-project" ? "apps/one/src/importer0.ts" : "main.ts");
  const text = await fs.readFile(main, "utf8"), uri = pathToFileURL(main).href;
  const at = (marker, delta = 0) => {
    const offset = text.indexOf(marker) + marker.length + delta;
    assert.ok(offset >= marker.length, `missing marker ${marker}`);
    const before = text.slice(0, offset);
    return { line: before.split("\n").length - 1, character: offset - before.lastIndexOf("\n") - 1 };
  };
  const queries = [
    ["hover", "/*member*/", 1], ["completion", "/*member*/", 2], ["signatureHelp", "/*signature*/", 0],
    ["definition", "/*member*/", 1], ["references", "/*member*/", 1],
    ["prepareRename", "/*lexical*/", 1], ["prepareRename", "/*private*/", 2],
  ];
  const startup = [], first = [], warm = [], resultVariants = new Map();
  for (let processIndex = 0; processIndex < 3; processIndex++) {
    const started = performance.now();
    const client = new LspStdioClient("dotnet", [server, "--language-features", "full", "--diagnostics", "all"],
      { cwd: root, timeoutMs: 30_000 });
    try {
      const initialized = await client.request("initialize", { processId: process.pid,
        rootUri: pathToFileURL(root).href, workspaceFolders: [{ uri: pathToFileURL(root).href, name }],
        capabilities: { workspace: { workspaceEdit: { documentChanges: true } }, textDocument: {
          hover: { contentFormat: ["markdown"] }, rename: { prepareSupport: true },
          signatureHelp: { signatureInformation: { parameterInformation: { labelOffsetSupport: true }, activeParameterSupport: true } },
        } } });
      assert.equal(initialized.error, undefined);
      startup.push({ milliseconds: performance.now() - started, payloadBytes: Buffer.byteLength(JSON.stringify(initialized)) });
      client.notify("initialized", {});
      client.notify("textDocument/didOpen", { textDocument: { uri, languageId: "typescript", version: 1, text } });
      async function sequence() {
        const begin = performance.now();
        const results = [], payloads = [];
        for (const [method, marker, delta] of queries) {
          const response = await client.request(`textDocument/${method}`, {
            textDocument: { uri }, position: at(marker, delta), context: { includeDeclaration: true },
          });
          assert.equal(response.error, undefined, `${name} ${method}: ${JSON.stringify(response.error)}`);
          assert.ok(response.result, `${name} ${method}: result unavailable`);
          if (method === "hover") assert.equal(response.result.contents.value, "```typescript\nvalue: any\n```", `${name} process ${processIndex} hover`);
          if (method === "completion") assert.ok((Array.isArray(response.result) ? response.result : response.result.items)
            .some((item) => item.label === "value"), `${name} process ${processIndex} completion`);
          if (method === "signatureHelp") assert.ok(response.result.signatures?.length > 0);
          if (method === "definition") assert.equal((Array.isArray(response.result) ? response.result : [response.result]).length, 1);
          if (method === "references") assert.equal(response.result.length, name === "multi-project" ? 25 : 2);
          if (method === "prepareRename" && marker === "/*private*/")
            assert.equal(response.result.placeholder, "#serial");
          if (method === "prepareRename" && marker === "/*lexical*/") assert.ok(response.result.start && response.result.end);
          payloads.push(Buffer.byteLength(JSON.stringify(response)));
          results.push(response.result);
        }
        const milliseconds = performance.now() - begin;
        const normalized = JSON.stringify(results, (key, value) => {
          // OmniSharp stamps a random per-process routing ID on completion data.
          if (key === "$$__handler_id__$$") return "<handler>";
          if (key === "uri" && typeof value === "string" && value.startsWith("file:")) {
            const relative = path.relative(root, fileURLToPath(value));
            if (!relative.startsWith("..") && !path.isAbsolute(relative)) return `<fixture>/${relative.split(path.sep).join("/")}`;
          }
          return value;
        });
        const fingerprint = createHash("sha256").update(normalized).digest("hex");
        if (!resultVariants.has(fingerprint)) resultVariants.set(fingerprint, results);
        return { milliseconds, payloadBytes: payloads.reduce((a, b) => a + b, 0), perQueryPayloadBytes: payloads, fingerprint };
      }
      first.push(await sequence());
      if (processIndex === 0) for (let i = 0; i < 15; i++) warm.push(await sequence());
    } finally { await client.close(); }
  }
  if (resultVariants.size !== 1) await fs.writeFile(path.join(output, `stdio-${name}-variants.json`), JSON.stringify([...resultVariants], null, 2));
  assert.equal(new Set([...first, ...warm].map((sample) => sample.fingerprint)).size, 1, `${name}: wire result changed`);
  reports.push({ name, freshProcessInitialize: summary(startup), firstSequenceAfterInitialize: summary(first),
    unchangedWarmSequence: summary(warm) });
}
const report = { timestampUtc: new Date().toISOString(), node: process.version, platform: `${process.platform}-${process.arch}`,
  server, serverSha256: createHash("sha256").update(await fs.readFile(server)).digest("hex"),
  methodology: "Actual full-mode stdio using the same on-disk service fixtures. Three fresh processes per fixture; initialize includes spawn/startup separately from the first request sequence. Fifteen unchanged sequences in the first process. Byte counts are parsed JSON response bytes (including JSON-RPC fields, excluding Content-Length framing), not raw pipe framing or notifications. Fingerprints normalize fixture URIs and only OmniSharp's random per-process completion routing ID; semantic fields remain intact. No internal server counters are observed in this run. Diagnostic publication uses the standalone all preset and may share/contend for analysis, as in a real client.", reports };
await fs.writeFile(path.join(output, "stdio-results.json"), JSON.stringify(report, null, 2) + "\n");
console.log(path.join(output, "stdio-results.json"));
