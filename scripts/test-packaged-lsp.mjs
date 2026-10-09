import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdir, readFile, realpath, stat, writeFile } from "node:fs/promises";
import { dirname, isAbsolute, join, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { setTimeout as delay } from "node:timers/promises";
import { LspStdioClient } from "./lib/lsp-stdio.mjs";

// The caller installs an exact local nupkg first. This command deliberately accepts
// only its generated executable shim, never a repository DLL or a global tool name.
const [shimArgument, outputArgument, packageArgument, packageVersion] = process.argv.slice(2);
assert.ok(shimArgument && outputArgument && packageArgument && packageVersion,
  "Usage: node scripts/test-packaged-lsp.mjs <installed-shim> <results> <nupkg> <version>");
assert.ok(isAbsolute(shimArgument), "Pass the exact absolute installed-tool shim path");
const shim = await realpath(shimArgument);
assert.ok((await stat(shim)).isFile(), "Installed shim must be an executable file");
assert.match(shim, /[/\\]sharpts-lsp(?:\.exe)?$/i, "Launch the installed sharpts-lsp command");
const toolDirectory = dirname(shim);
assert.ok((await stat(join(toolDirectory, ".store", "sharpts.languageserver", packageVersion))).isDirectory(),
  "Shim must belong to the isolated installation of this exact package version");
const output = resolve(outputArgument);
const packagePath = resolve(packageArgument);
const workspace = join(output, "workspace with spaces");
await mkdir(workspace, { recursive: true });
const source = "import { StringBuilder as SB } from 'dotnet:System.Text.StringBuilder';\r\n" +
  "const /*declaration*/local: number = 123;\r\n/* 😀 */ /*ordinary*/local;\r\n" +
  "const builder = new SB();\r\nbuilder./*clr*/append('x');\r\n";
const decorator = '@DotNetType("System.String")\ndeclare class NetString {}\n';
await writeFile(join(workspace, "tsconfig.json"), JSON.stringify({
  compilerOptions: { noLib: true, types: [], experimentalDecorators: true }, include: ["*.ts"],
}));
await writeFile(join(workspace, "main.ts"), source);
await writeFile(join(workspace, "decorator.ts"), decorator);
const uri = (name) => pathToFileURL(join(workspace, name)).href;
const positionAt = (offset) => {
  const prefix = source.slice(0, offset);
  return { line: prefix.split("\n").length - 1, character: offset - prefix.lastIndexOf("\n") - 1 };
};
const position = (marker) => ({ textDocument: { uri: uri("main.ts") },
  position: positionAt(source.indexOf(`/*${marker}*/`) + marker.length + 5) });
const present = (value) => value !== undefined && value !== null && value !== false;
const sameUri = (left, right) => process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
const forbiddenProviders = ["inlayHintProvider", "semanticTokensProvider", "foldingRangeProvider",
  "documentFormattingProvider", "documentRangeFormattingProvider", "documentOnTypeFormattingProvider"];
const forbiddenMethod = /^textDocument\/(?:inlayHint|semanticTokens(?:\/|$)|foldingRange|formatting|rangeFormatting|onTypeFormatting)/;
const report = { completed: false, workspace, node: process.version, package: {
  path: packagePath, version: packageVersion,
  sha256: createHash("sha256").update(await readFile(packagePath)).digest("hex"),
}, shim, isolatedToolDirectory: toolDirectory, clients: [] };

async function request(client, method, params) {
  const response = await client.request(method, params);
  assert.equal(response.error, undefined, `${method}: ${JSON.stringify(response.error)}`);
  return response.result;
}

async function diagnosticsAfter(client, start, predicate) {
  const deadline = performance.now() + 20_000;
  while (performance.now() < deadline) {
    const found = client.notifications.slice(start).find((notification) =>
      notification.method === "textDocument/publishDiagnostics" &&
      sameUri(notification.params.uri, uri("diagnostic.ts")) &&
      notification.params.version === 1 && predicate(notification.params.diagnostics));
    if (found) return found.params.diagnostics;
    await delay(25);
  }
  throw new Error(`No expected diagnostic-mode publication: ${JSON.stringify(client.notifications.slice(-12))}`);
}

try {
  for (const mode of ["full", "interop-only"]) {
    // Explicit all in interop-only proves that checking for diagnostics cannot
    // silently enable ordinary request providers. Coexistence's sharpts-only
    // startup recipe is exercised separately by all six child protocol suites.
    const args = ["--language-features", mode, "--diagnostics", "all"];
    const client = new LspStdioClient(shim, args, { cwd: workspace, timeoutMs: 30_000 });
    const result = { mode, command: [shim, ...args], cwd: workspace, cases: [] };
    report.clients.push(result);
    let failure;
    let phase = "initialize";
    try {
      const initialized = await request(client, "initialize", { processId: process.pid,
        rootUri: pathToFileURL(workspace).href,
        workspaceFolders: [{ uri: pathToFileURL(workspace).href, name: "installed-tool" }],
        capabilities: { textDocument: {
          // Feature objects are advertised; all their nested presentation options
          // are omitted. An entirely absent feature need not be advertised back.
          ...Object.fromEntries(["hover", "completion", "signatureHelp", "codeAction", "definition",
            "references", "rename", "documentSymbol"].map((feature) => [feature, {}])),
          publishDiagnostics: { versionSupport: true },
        } },
      });
      const capabilities = initialized.capabilities;
      result.capabilities = capabilities;
      for (const provider of ["hoverProvider", "completionProvider", "signatureHelpProvider", "codeActionProvider"])
        assert.ok(present(capabilities[provider]), `${mode}: retained ${provider}`);
      for (const provider of ["definitionProvider", "referencesProvider", "renameProvider", "documentSymbolProvider"])
        assert.equal(present(capabilities[provider]), mode === "full", `${mode}: ${provider}`);
      for (const provider of forbiddenProviders)
        assert.ok(!present(capabilities[provider]), `${mode}: unsupported ${provider}`);
      client.notify("initialized", {});
      for (const [name, text] of [["main.ts", source], ["decorator.ts", decorator]])
        client.notify("textDocument/didOpen", { textDocument: { uri: uri(name), languageId: "typescript", version: 1, text } });
      result.cases.push("initialize-with-empty-feature-capabilities");

      phase = "ordinary-feature-mode";
      const hover = await request(client, "textDocument/hover", position("ordinary"));
      const definition = await client.request("textDocument/definition", position("ordinary"));
      if (mode === "full") {
        assert.ok(hover && /number/.test(JSON.stringify(hover)), "Installed full tool returns ordinary semantic hover");
        assert.equal(definition.error, undefined);
        const locations = Array.isArray(definition.result) ? definition.result : [definition.result];
        assert.equal(locations.length, 1);
        assert.ok(sameUri(locations[0].uri, uri("main.ts")));
        const start = source.indexOf("/*declaration*/") + "/*declaration*/".length;
        assert.deepEqual(locations[0].range, { start: positionAt(start), end: positionAt(start + "local".length) });
      } else {
        assert.equal(hover, null, "Installed interop-only tool has no ordinary hover fallback");
        assert.equal(definition.error?.code, -32601, "Interop-only navigation remains unregistered");
      }
      result.cases.push("ordinary-feature-mode");

      phase = "retained-clr-and-decorator-interop";
      const clr = await request(client, "textDocument/hover", position("clr"));
      assert.ok(clr && /append/i.test(JSON.stringify(clr)), "Installed tool retains CLR usage hover");
      const decorated = await request(client, "textDocument/hover", {
        textDocument: { uri: uri("decorator.ts") }, position: { line: 0, character: 5 },
      });
      assert.ok(decorated && /System\.String/.test(JSON.stringify(decorated)), "Installed tool retains decorator hover");
      result.cases.push("retained-clr-and-decorator-interop");

      phase = "live-diagnostics-mode-keeps-initialized-feature-mode";
      let notificationStart = client.notifications.length;
      client.notify("textDocument/didOpen", { textDocument: { uri: uri("diagnostic.ts"),
        languageId: "typescript", version: 1, text: "const wrong: number = 'ordinary type error';\n" } });
      await diagnosticsAfter(client, notificationStart, (items) => items.some((item) => item.severity === 1));
      for (const diagnostics of ["off", "sharpts-only", "all"]) {
        notificationStart = client.notifications.length;
        client.notify("workspace/didChangeConfiguration", { settings: { sharpts: { diagnostics } } });
        await diagnosticsAfter(client, notificationStart, (items) => diagnostics === "all"
          ? items.some((item) => item.severity === 1) : items.length === 0);
      }
      const afterConfiguration = await request(client, "textDocument/hover", position("ordinary"));
      if (mode === "interop-only") {
        assert.equal(afterConfiguration, null, "diagnostics=all must not enable ordinary hover");
        const refused = await client.request("textDocument/definition", position("ordinary"));
        assert.equal(refused.error?.code, -32601, "Diagnostic changes cannot register ordinary navigation");
      } else assert.ok(afterConfiguration, "Standalone ordinary requests remain available after diagnostic changes");
      result.cases.push("explicit-all-and-live-diagnostics-mode-independent-of-feature-mode");

      phase = "unimplemented-methods-and-surviving-request";
      for (const method of ["inlayHint", "semanticTokens/full", "foldingRange", "formatting", "rangeFormatting", "onTypeFormatting"]) {
        const response = await client.request(`textDocument/${method}`, { ...position("ordinary"),
          range: { start: { line: 0, character: 0 }, end: { line: 0, character: 1 } },
          options: { tabSize: 2, insertSpaces: true }, ch: ";",
        });
        assert.equal(response.error?.code, -32601, `${mode}: ${method} is unregistered`);
      }
      assert.ok(!client.registeredMethods.some((method) => forbiddenMethod.test(method)),
        "Unsupported features must not appear through subsequent registration");
      if (mode === "interop-only")
        assert.ok(!client.registeredMethods.some((method) => /^textDocument\/(?:definition|references|rename|documentSymbol)$/.test(method)));
      assert.ok(await request(client, "textDocument/hover", position("clr")), "Server survives unregistered requests");
      result.registeredMethods = [...client.registeredMethods];
      result.cases.push("unimplemented-methods-and-surviving-request");
    } catch (error) {
      failure = error;
      await writeFile(join(output, "failure.json"), JSON.stringify({ mode, phase, message: error.message,
        stack: error.stack, notifications: client.notifications.slice(-12), report }, null, 2) + "\n");
      throw error;
    } finally {
      try { await client.close(); result.cases.push("shutdown-and-exit"); }
      catch (error) {
        if (!failure) throw error;
        console.error(`Cleanup after packaged-tool failure: ${error.message}`);
      }
    }
  }
  report.completed = true;
} finally {
  await writeFile(join(output, "result.json"), JSON.stringify(report, null, 2) + "\n");
}
console.log(JSON.stringify(report, null, 2));
