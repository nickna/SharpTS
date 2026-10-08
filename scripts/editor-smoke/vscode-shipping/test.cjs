const assert = require("node:assert/strict");
const { createHash } = require("node:crypto");
const fs = require("node:fs/promises");
const path = require("node:path");
const vscode = require("vscode");

// No provider registrations and no server process in this extension. The installed
// shipping extension is solely responsible for starting SharpTS's real client.
exports.run = async function run() {
  const config = JSON.parse(await fs.readFile(process.env.SHARPTS_SHIPPING_SMOKE_CONFIG, "utf8"));
  const extensions = {};
  for (const id of ["sharpts.vscode-sharpts", "vscode.typescript-language-features", "esbenp.prettier-vscode"]) {
    const extension = vscode.extensions.getExtension(id);
    assert.ok(extension, `installed extension ${id} must be available`);
    await extension.activate();
    assert.equal(extension.isActive, true, `${id} must activate`);
    extensions[id] = { version: extension.packageJSON.version, path: extension.extensionPath };
  }
  const shippingPath = extensions["sharpts.vscode-sharpts"].path;
  const hash = async (file) => createHash("sha256").update(await fs.readFile(file)).digest("hex");
  assert.equal(await hash(path.join(shippingPath, "bin/server/SharpTS.LanguageServer.dll")), config.package.serverSha256);
  assert.equal(await hash(path.join(shippingPath, "bin/server/SharpTS.dll")), config.package.coreSha256);
  assert.equal(extensions["sharpts.vscode-sharpts"].version, config.package.version);
  const prettier = require(config.prettierPath);
  assert.equal(prettier.version, config.prettierVersion);
  assert.equal(extensions["esbenp.prettier-vscode"].version, config.prettierExtensionVersion);
  const uri = vscode.Uri.file(config.fixture);
  const document = await vscode.workspace.openTextDocument(uri);
  await vscode.window.showTextDocument(document);
  assert.equal(document.languageId, "typescript");
  assert.equal(vscode.workspace.getConfiguration("sharpts").get("diagnostics"), "sharpts-only");
  assert.equal(vscode.workspace.getConfiguration("editor", uri).get("defaultFormatter"), "esbenp.prettier-vscode");
  assert.equal(vscode.workspace.getConfiguration("editor", uri).get("formatOnSave"), true);
  const observations = [];
  const hoverText = (items) => (items ?? []).flatMap((hover) => hover.contents)
    .map((content) => typeof content === "string" ? content : content.value);
  const samePath = (left, right) => process.platform === "win32"
    ? path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase()
    : path.resolve(left) === path.resolve(right);
  async function until(description, query, predicate) {
    const deadline = Date.now() + 15_000;
    let value;
    do {
      value = await query();
      if (predicate(value)) return value;
      await new Promise((resolve) => setTimeout(resolve, 100));
    } while (Date.now() < deadline);
    assert.fail(`${description}: ${JSON.stringify(value)}`);
  }
  async function assertProviders(phase) {
    const text = document.getText();
    const ordinaryOffset = text.lastIndexOf("ordinary") + 2;
    const definitions = await until("built-in TypeScript ordinary definition", () =>
      vscode.commands.executeCommand("vscode.executeDefinitionProvider", uri, document.positionAt(ordinaryOffset)),
      (items) => items?.some((item) => samePath((item.targetUri ?? item.uri).fsPath, config.dependency)));
    const definition = definitions.find((item) => samePath((item.targetUri ?? item.uri).fsPath, config.dependency));
    const hovers = await until("fresh shipping SharpTS CLR decorator hover", () =>
      vscode.commands.executeCommand("vscode.executeHoverProvider", uri, document.positionAt(text.indexOf("DotNetType") + 3)),
      (items) => hoverText(items).some((content) => content.includes("System.Text.StringBuilder")));
    const memberHovers = await until("fresh shipping SharpTS CLR member hover", () =>
      vscode.commands.executeCommand("vscode.executeHoverProvider", uri, document.positionAt(text.indexOf("Append") + 2)),
      (items) => hoverText(items).some((content) => content.includes("StringBuilder.Append")));
    observations.push({ phase, version: document.version,
      definition: { uri: (definition.targetUri ?? definition.uri).toString(),
        range: definition.targetSelectionRange ?? definition.range },
      clrHover: hoverText(hovers)
        .filter((content) => content.includes("System.Text.StringBuilder")),
      clrMemberHover: hoverText(memberHovers).filter((content) => content.includes("StringBuilder.Append")) });
  }
  await assertProviders("before-save");
  for (let cycle = 1; cycle <= 2; cycle++) {
    const before = document.getText();
    const edit = new vscode.WorkspaceEdit();
    edit.insert(uri, document.positionAt(before.length), " ");
    assert.equal(await vscode.workspace.applyEdit(edit), true);
    assert.equal(document.isDirty, true, "each save must run actual dirty-document save participants");
    const expected = await prettier.format(document.getText(), {
      ...(await prettier.resolveConfig(config.fixture)), filepath: config.fixture,
    });
    assert.notEqual(document.getText(), expected, "each dirty document must require formatting");
    assert.equal(await document.save(), true);
    assert.equal(document.isDirty, false);
    assert.equal(document.getText(), expected, `save ${cycle} must match pinned Prettier`);
    assert.equal(await fs.readFile(config.fixture, "utf8"), expected);
    await assertProviders(`after-save-${cycle}`);
  }
  await fs.writeFile(config.result, JSON.stringify({ status: "passed", editor: `VS Code ${vscode.version}`,
    platform: `${process.platform}-${process.arch}`, extensions, package: config.package,
    sourceCommit: config.sourceCommit, formatter: `Prettier ${prettier.version}`, saves: 2,
    exactFormattedOutput: true, serverMode: "interop-only", diagnostics: "sharpts-only",
    modeSource: "unchanged shipping extension activation arguments and default configuration",
    providersRegisteredByHarness: 0, serversSpawnedByHarness: 0, observations }, null, 2) + "\n");
};
