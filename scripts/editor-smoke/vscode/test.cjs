const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const { pathToFileURL } = require("node:url");
const vscode = require("vscode");

// Invoked by VS Code's real extension-host test runner, without a test framework.
exports.run = async function run() {
  const config = JSON.parse(
    await fs.readFile(process.env.SHARPTS_EDITOR_SMOKE_CONFIG, "utf8"),
  );
  const { LspStdioClient } = await import(
    pathToFileURL(path.join(config.root, "scripts/lib/lsp-stdio.mjs")).href
  );
  const prettier = require(config.prettierPath);
  assert.equal(prettier.version, config.prettierVersion);
  const prettierExtension = vscode.extensions.getExtension(
    "esbenp.prettier-vscode",
  );
  assert.ok(prettierExtension, "isolated Prettier extension must be installed");
  assert.equal(prettierExtension.packageJSON.version, config.extensionVersion);
  await prettierExtension.activate();

  const client = new LspStdioClient(
    config.dotnet,
    [config.server, "--language-features", "interop-only", "--diagnostics", "off"],
    { cwd: config.workspace, timeoutMs: 10000 },
  );
  const registrations = [];
  let hoverRequests = 0;
  let lastHover;
  let opened;
  try {
    const initialize = await client.request("initialize", {
      processId: process.pid,
      rootUri: vscode.Uri.file(config.workspace).toString(),
      capabilities: {
        workspace: { configuration: false },
        textDocument: {
          synchronization: { dynamicRegistration: false, didSave: true },
          hover: {
            dynamicRegistration: false,
            contentFormat: ["markdown", "plaintext"],
          },
          formatting: { dynamicRegistration: false },
          rangeFormatting: { dynamicRegistration: false },
          onTypeFormatting: { dynamicRegistration: false },
        },
      },
    });
    assert.equal(initialize.error, undefined);
    const capabilities = initialize.result.capabilities;
    assert.ok(capabilities.hoverProvider, "SharpTS hover must be advertised");
    for (const name of [
      "documentFormattingProvider",
      "documentRangeFormattingProvider",
      "documentOnTypeFormattingProvider",
    ]) {
      assert.ok(!capabilities[name], `SharpTS must not own ${name}`);
    }
    client.notify("initialized", {});

    const uri = vscode.Uri.file(config.fixture);
    opened = await vscode.workspace.openTextDocument(uri);
    await vscode.window.showTextDocument(opened);
    client.notify("textDocument/didOpen", {
      textDocument: {
        uri: uri.toString(),
        languageId: opened.languageId,
        version: opened.version,
        text: opened.getText(),
      },
    });
    registrations.push(
      vscode.workspace.onDidChangeTextDocument((event) => {
        if (event.document.uri.toString() === uri.toString()) {
          client.notify("textDocument/didChange", {
            textDocument: { uri: uri.toString(), version: event.document.version },
            contentChanges: [{ text: event.document.getText() }],
          });
        }
      }),
      vscode.languages.registerHoverProvider(
        { language: "typescript", scheme: "file" },
        {
          async provideHover(document, position) {
            if (document.uri.toString() !== uri.toString()) return undefined;
            const reply = await client.request("textDocument/hover", {
              textDocument: { uri: uri.toString() },
              position: { line: position.line, character: position.character },
            });
            assert.equal(reply.error, undefined);
            lastHover = reply.result;
            hoverRequests++;
            if (!lastHover) return undefined;
            const contents = lastHover.contents;
            const text = typeof contents === "string" ? contents : contents.value;
            return new vscode.Hover(new vscode.MarkdownString(text));
          },
        },
      ),
    );

    async function assertSharpTsHover() {
      const countBefore = hoverRequests;
      // VS Code may log and swallow a provider exception. A previous successful
      // hover must never count as proof that this fresh request succeeded.
      lastHover = undefined;
      await vscode.commands.executeCommand(
        "vscode.executeHoverProvider",
        uri,
        new vscode.Position(0, 4),
      );
      assert.ok(hoverRequests > countBefore, "editor must obtain a fresh SharpTS response");
      assert.match(JSON.stringify(lastHover), /System\.Text\.StringBuilder/);
    }

    await assertSharpTsHover();
    const before = opened.getText();
    const expected = await prettier.format(before, {
      ...(await prettier.resolveConfig(config.fixture)),
      filepath: config.fixture,
    });
    assert.notEqual(before, expected, "fixture must require formatting");
    assert.equal(
      vscode.workspace.getConfiguration("editor", uri).get("defaultFormatter"),
      "esbenp.prettier-vscode",
    );
    assert.equal(
      vscode.workspace.getConfiguration("editor", uri).get("formatOnSave"),
      true,
    );

    // A clean document's save can bypass save participants. Make a real edit,
    // then let VS Code's save pipeline invoke the installed formatter itself.
    const dirtyEdit = new vscode.WorkspaceEdit();
    dirtyEdit.insert(uri, opened.positionAt(before.length), " ");
    assert.equal(await vscode.workspace.applyEdit(dirtyEdit), true);
    assert.equal(opened.isDirty, true);
    assert.equal(await opened.save(), true);
    assert.equal(opened.isDirty, false);
    assert.equal(opened.getText(), expected, "format-on-save must match pinned Prettier");
    assert.equal(await fs.readFile(config.fixture, "utf8"), expected);
    await assertSharpTsHover();
    const firstSavedVersion = opened.version;

    // A second dirty save proves the real save participant remains idempotent.
    const secondEdit = new vscode.WorkspaceEdit();
    secondEdit.insert(uri, opened.positionAt(expected.length), " ");
    assert.equal(await vscode.workspace.applyEdit(secondEdit), true);
    assert.equal(await opened.save(), true);
    assert.equal(opened.getText(), expected);
    assert.ok(opened.version > firstSavedVersion);
    await assertSharpTsHover();

    await fs.writeFile(
      config.result,
      JSON.stringify(
        {
          status: "passed",
          editor: `VS Code ${vscode.version}`,
          platform: `${process.platform}-${process.arch}`,
          formatterExtension: `esbenp.prettier-vscode ${prettierExtension.packageJSON.version}`,
          formatter: `Prettier ${prettier.version}`,
          serverMode: "interop-only",
          formatterOwner: "esbenp.prettier-vscode",
          sharpTsHoverRequests: hoverRequests,
          saves: 2,
          exactFormattedOutput: true,
          noServerFormattingCapability: true,
        },
        null,
        2,
      ) + "\n",
    );
  } finally {
    for (const registration of registrations) registration.dispose();
    try {
      if (opened) {
        client.notify("textDocument/didClose", {
          textDocument: { uri: opened.uri.toString() },
        });
      }
    } finally {
      await client.close();
    }
  }
};
