const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const vscode = require("vscode");

// The licensed C# adapter is hosted by its supported VS Code client. This
// extension only observes protocol traffic and uses public VS Code debug APIs.
exports.run = async function run() {
  const config = JSON.parse(await fs.readFile(process.env.SHARPTS_COMPILED_DEBUGGER_CONFIG, "utf8"));
  const transcript = [];
  const observations = [];
  const disposables = [];
  let session;
  let eventCursor = 0;
  const samePath = (left, right) => process.platform === "win32"
    ? path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase()
    : path.resolve(left) === path.resolve(right);
  const result = { status: "running", editor: `VS Code ${vscode.version}`,
    platform: `${process.platform}-${process.arch}`, observations };
  async function save() {
    await fs.writeFile(config.result, JSON.stringify(result, null, 2) + "\n");
    await fs.writeFile(config.transcript, JSON.stringify(transcript, null, 2) + "\n");
  }
  async function until(description, query, predicate, timeout = 20_000) {
    const deadline = Date.now() + timeout;
    let value;
    do {
      value = await query();
      if (predicate(value)) return value;
      await new Promise(resolve => setTimeout(resolve, 25));
    } while (Date.now() < deadline);
    assert.fail(`${description}: ${JSON.stringify(value)}`);
  }
  async function nextEvent(name) {
    return until(`debug adapter event ${name}`, () => {
      for (; eventCursor < transcript.length; eventCursor++) {
        const entry = transcript[eventCursor];
        if (entry.direction === "adapter" && entry.message.type === "event") {
          if (entry.message.event === name) { eventCursor++; return entry.message; }
          if (entry.message.event === "terminated" && name !== "terminated")
            assert.fail(`debuggee terminated before ${name}`);
        }
      }
      return null;
    }, Boolean);
  }
  async function stoppedAt(marker) {
    const stop = await nextEvent("stopped");
    assert.ok(stop.body.threadId, "a source stop must identify its thread");
    const stack = await session.customRequest("stackTrace", {
      threadId: stop.body.threadId, startFrame: 0, levels: 20,
    });
    const frame = stack.stackFrames[0];
    assert.ok(frame?.source?.path, `source frame at ${marker}`);
    assert.ok(samePath(frame.source.path, config.markers[marker].path),
      `${marker} source: ${frame.source.path}`);
    assert.equal(frame.line, config.markers[marker].line, `${marker} source line`);
    observations.push({ marker, reason: stop.body.reason, threadId: stop.body.threadId,
      frame: { name: frame.name, path: frame.source.path, line: frame.line },
      stack: stack.stackFrames.map(item => ({ name: item.name, path: item.source?.path, line: item.line })) });
    await save();
    return { threadId: stop.body.threadId, frame };
  }
  try {
    const csharp = vscode.extensions.getExtension("ms-dotnettools.csharp");
    assert.ok(csharp, "the real C# extension must be installed");
    await csharp.activate();
    result.csharpVersion = csharp.packageJSON.version;
    assert.equal(result.csharpVersion, config.extensionVersions["ms-dotnettools.csharp"]);
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
    const breakpoints = Object.entries(config.markers).filter(([marker]) => marker.startsWith("break:"))
      .map(([, location]) => new vscode.SourceBreakpoint(
        new vscode.Location(vscode.Uri.file(location.path), new vscode.Position(location.line - 1, 0))));
    vscode.debug.addBreakpoints(breakpoints);
    const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(config.entry));
    const launched = await vscode.debug.startDebugging(folder, {
      type: "coreclr", request: "launch", name: "SharpTS compiled debugger acceptance",
      program: config.assembly, cwd: config.workspace, console: "internalConsole",
      stopAtEntry: false, justMyCode: true, requireExactSource: true,
      targetArchitecture: process.arch === "arm64" ? "arm64" : "x86_64",
      logging: { moduleLoad: true, exceptions: true, engineLogging: true },
    });
    assert.equal(launched, true, "the supported C# adapter must launch the compiled assembly");
    let stop = await stoppedAt("break:top-level");
    await session.customRequest("continue", { threadId: stop.threadId });
    stop = await stoppedAt("break:function");
    assert.match(stop.frame.name, /exercise/);
    await session.customRequest("next", { threadId: stop.threadId });
    stop = await stoppedAt("step:brace-body");
    const scopes = await session.customRequest("scopes", { frameId: stop.frame.id });
    const variables = [];
    for (const scope of scopes.scopes) {
      if (!scope.expensive && scope.variablesReference) {
        variables.push(...(await session.customRequest("variables", {
          variablesReference: scope.variablesReference,
        })).variables);
      }
    }
    assert.ok(variables.some(variable => variable.evaluateName === "total" || variable.name === "total"),
      "source-named local is visible");
    assert.ok(variables.some(variable => variable.evaluateName === "__this" || variable.name === "__this"),
      "source-named parameter is visible");
    const evaluation = await session.customRequest("evaluate", {
      expression: "total", frameId: stop.frame.id, context: "watch",
    });
    assert.equal(Number(evaluation.result), 0, "watch resolves the source-named local");
    result.locals = variables.map(({ name, value, type }) => ({ name, value, type }));
    result.watch = evaluation.result;
    const parameterEvaluation = await session.customRequest("evaluate", {
      expression: "__this", frameId: stop.frame.id, context: "watch",
    });
    assert.equal(Number(parameterEvaluation.result), 2, "watch resolves the source-named parameter");
    result.parameterWatch = parameterEvaluation.result;
    for (const marker of ["step:loop-header", "step:loop-body", "step:loop-header", "step:loop-body"]) {
      await session.customRequest("next", { threadId: stop.threadId });
      stop = await stoppedAt(marker);
    }
    await session.customRequest("continue", { threadId: stop.threadId });
    stop = await stoppedAt("break:imported");
    assert.ok(observations.at(-1).stack.some(frame => /exercise/.test(frame.name)),
      "imported function has its calling TypeScript frame");
    for (const marker of ["break:catch", "break:class", "break:generator", "break:async"]) {
      await session.customRequest("continue", { threadId: stop.threadId });
      stop = await stoppedAt(marker);
      if (marker === "break:async") {
        assert.doesNotMatch(stop.frame.name, /\$asyncCore\$/, "async breakpoint is not in a suspension-free fast path");
        assert.ok(transcript.some(entry => entry.direction === "adapter" &&
          entry.message.type === "event" && entry.message.event === "output" &&
          entry.message.body.category === "stdout" && entry.message.body.output.includes("done 11")),
        "the async function suspended before its post-await breakpoint");
        result.asyncResumptionVerified = true;
      }
    }
    await session.customRequest("continue", { threadId: stop.threadId });
    const exited = await nextEvent("exited");
    assert.equal(exited.body.exitCode, 0, "the debuggee exits successfully");
    await nextEvent("terminated");
    const breakpointResponses = transcript.filter(entry => entry.direction === "adapter" &&
      entry.message.type === "response" && entry.message.command === "setBreakpoints");
    const verified = breakpointResponses.flatMap(entry => entry.message.body?.breakpoints ?? []);
    // Pending breakpoints become verified when the module is loaded.
    verified.push(...transcript.filter(entry => entry.direction === "adapter" &&
      entry.message.type === "event" && entry.message.event === "breakpoint")
      .map(entry => entry.message.body.breakpoint));
    for (const [marker, location] of Object.entries(config.markers).filter(([marker]) => marker.startsWith("break:")))
      assert.ok(verified.some(item => item.verified && item.line === location.line &&
        item.source?.path && samePath(item.source.path, location.path)), `${marker} breakpoint binds`);
    result.verifiedBreakpoints = verified.filter(item => item.verified);
    result.output = transcript.filter(entry => entry.direction === "adapter" &&
      entry.message.type === "event" && entry.message.event === "output" &&
      entry.message.body.category === "stdout").map(entry => entry.message.body.output).join("");
    assert.equal(result.output.replaceAll("\r\n", "\n").trim(), config.expectedOutput.trim());
    result.status = "passed";
  } catch (error) {
    result.status = "failed";
    result.error = error.stack ?? String(error);
    throw error;
  } finally {
    await save();
    if (session) await vscode.debug.stopDebugging(session);
    for (const disposable of disposables) disposable.dispose();
  }
};
