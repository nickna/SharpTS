const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const vscode = require("vscode");

// Exercise the licensed adapter only through its supported VS Code client.
exports.run = async function run() {
  const config = JSON.parse(await fs.readFile(process.env.SHARPTS_COMPILED_DEBUGGER_CONFIG, "utf8"));
  const transcript = [];
  const result = { status: "running", editor: `VS Code ${vscode.version}`,
    platform: `${process.platform}-${process.arch}`, observations: [] };
  const disposables = [];
  let session;
  let cursor = 0;
  const normalizePath = value => process.platform === "win32"
    ? path.resolve(value).toLowerCase() : path.resolve(value);
  const samePath = (left, right) => normalizePath(left) === normalizePath(right);
  const save = async () => {
    await fs.writeFile(config.result, JSON.stringify(result, null, 2) + "\n");
    await fs.writeFile(config.transcript, JSON.stringify(transcript, null, 2) + "\n");
  };
  async function nextEvent(name) {
    const deadline = Date.now() + 30_000;
    while (Date.now() < deadline) {
      for (; cursor < transcript.length; cursor++) {
        const message = transcript[cursor].message;
        if (transcript[cursor].direction !== "adapter" || message.type !== "event") continue;
        if (message.event === name) { cursor++; return message.body; }
        if (message.event === "terminated" && name !== "terminated")
          assert.fail(`debuggee terminated before ${name}`);
      }
      await new Promise(resolve => setTimeout(resolve, 25));
    }
    assert.fail(`timed out waiting for ${name}`);
  }
  try {
    const csharp = vscode.extensions.getExtension("ms-dotnettools.csharp");
    assert.ok(csharp, "the C# debugger extension must be installed");
    await csharp.activate();
    result.csharpVersion = csharp.packageJSON.version;
    disposables.push(vscode.debug.registerDebugAdapterTrackerFactory("coreclr", {
      createDebugAdapterTracker(current) {
        session = current;
        return {
          onWillReceiveMessage(message) { transcript.push({ direction: "client", message }); },
          onDidSendMessage(message) { transcript.push({ direction: "adapter", message }); },
          onError(error) { transcript.push({ direction: "error", message: String(error) }); },
        };
      },
    }));
    vscode.debug.addBreakpoints(Object.values(config.markers).map(location => new vscode.SourceBreakpoint(
      new vscode.Location(vscode.Uri.file(location.path), new vscode.Position(location.line - 1, 0)))));
    assert.equal(await vscode.debug.startDebugging(undefined, {
      type: "coreclr", request: "launch", name: "SharpTS hoisted locals",
      program: config.assembly, cwd: path.dirname(config.assembly), console: "internalConsole",
      justMyCode: true, requireExactSource: true, stopAtEntry: false,
      logging: { engineLogging: true },
    }), true);
    const expectations = config.expectations ?? Object.keys(config.markers).map(marker => ({ marker }));
    for (const expectation of expectations) {
      const stopped = await nextEvent("stopped");
      const stack = await session.customRequest("stackTrace", { threadId: stopped.threadId, levels: 20 });
      const frame = stack.stackFrames[0];
      const location = config.markers[expectation.marker];
      assert.ok(frame?.source?.path && samePath(frame.source.path, location.path), expectation.marker);
      assert.equal(frame.line, location.line, `${expectation.marker} source line`);
      const scopes = await session.customRequest("scopes", { frameId: frame.id });
      const variables = [];
      for (const scope of scopes.scopes) {
        if (!scope.expensive && scope.variablesReference)
          variables.push(...(await session.customRequest("variables", { variablesReference: scope.variablesReference })).variables);
      }
      const watches = {};
      const expressions = [...new Set([...(expectation.probe ?? []),
        ...Object.keys(expectation.values ?? {}), ...(expectation.absent ?? [])])];
      for (const expression of expressions)
        watches[expression] = await session.customRequest("evaluate", { expression, frameId: frame.id, context: "watch" });
      result.observations.push({ marker: expectation.marker, frame: frame.name, variables, watches });
      await save();
      if (!config.observeOnly) {
        assert.doesNotMatch(frame.name, /Unknown function/, "recognized source state-machine frame");
        assert.ok(!variables.some(variable => /Internal error|<Unknown>/.test(variable.value + variable.name)),
          "Locals must not fail in the expression evaluator");
        assert.ok(!variables.some(variable => !variable.name || /<>|__bs\d|__local_|_destruct|__awaiter|\$scratch/.test(variable.name)),
          "compiler scaffolding must not be projected as user variables");
        for (const [expression, expected] of Object.entries(expectation.values ?? {})) {
          const watch = watches[expression];
          assert.ok(!watch.presentationHint?.attributes?.includes("failedEvaluation"), `${expression} evaluates`);
          assert.equal(watch.result.replace(/\s+\{.*\}$/, ""), String(expected), `${expectation.marker}: ${expression}`);
          const locals = variables.filter(variable =>
            (variable.evaluateName ?? variable.name.replace(/ \[.*\]$/, "")) === expression);
          assert.equal(locals.length, 1, `${expectation.marker}: ${expression} appears once in Locals`);
          assert.equal(locals[0].value.replace(/\s+\{.*\}$/, ""), String(expected),
            `${expectation.marker}: ${expression} has the expected Locals value`);
        }
        for (const expression of expectation.absent ?? []) {
          assert.ok(watches[expression].presentationHint?.attributes?.includes("failedEvaluation"),
            `${expectation.marker}: ${expression} is out of scope`);
          assert.ok(!variables.some(variable => variable.evaluateName === expression ||
            variable.name.replace(/ \[.*\]$/, "") === expression), `${expression} stays out of Locals`);
        }
        for (const name of expectation.hiddenLocals ?? [])
          assert.ok(!variables.some(variable => variable.evaluateName === name ||
            variable.name.replace(/ \[.*\]$/, "") === name), `${name} stays out of source Locals`);
      }
      await session.customRequest("continue", { threadId: stopped.threadId });
    }
    assert.equal((await nextEvent("exited")).exitCode, 0);
    await nextEvent("terminated");
    result.output = transcript.filter(entry => entry.direction === "adapter" &&
      entry.message.type === "event" && entry.message.event === "output" &&
      entry.message.body.category === "stdout").map(entry => entry.message.body.output).join("").replaceAll("\r\n", "\n");
    assert.equal(result.output, config.expectedOutput, "debug execution preserves normal output");
    result.status = "passed";
    await save();
  } catch (error) {
    result.status = "failed";
    result.error = String(error.stack ?? error);
    await save();
    throw error;
  } finally {
    for (const disposable of disposables) disposable.dispose();
    if (session) await vscode.debug.stopDebugging(session);
  }
};
