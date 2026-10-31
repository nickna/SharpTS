const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const vscode = require("vscode");

// The native adapter runs inside its supported VS Code client. All control and
// observation use public editor debug APIs and the actual adapter's DAP traffic.
exports.run = async function run() {
  const config = JSON.parse(await fs.readFile(process.env.SHARPTS_NATIVE_DEBUGGER_CONFIG, "utf8"));
  const transcript = [];
  const observations = [];
  const disposables = [];
  let session;
  let eventCursor = 0;
  const samePath = (left, right) => path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase();
  const result = { status: "running", editor: `VS Code ${vscode.version}`, platform: `${process.platform}-${process.arch}`,
    adapter: "cppvsdbg", observations };
  async function save() {
    await fs.writeFile(config.result, JSON.stringify(result, null, 2) + "\n");
    await fs.writeFile(config.transcript, JSON.stringify(transcript, null, 2) + "\n");
  }
  async function until(description, query, predicate, timeout = 30_000) {
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
    return until(`native adapter event ${name}`, () => {
      for (; eventCursor < transcript.length; eventCursor++) {
        const entry = transcript[eventCursor];
        if (entry.direction !== "adapter" || entry.message.type !== "event") continue;
        if (entry.message.event === name) { eventCursor++; return entry.message; }
        if (entry.message.event === "terminated" && name !== "terminated")
          assert.fail(`native debuggee terminated before ${name}`);
      }
      return null;
    }, Boolean);
  }
  async function readStop() {
    const stop = await nextEvent("stopped");
    assert.ok(stop.body.threadId, "native source stop identifies its thread");
    const stack = await session.customRequest("stackTrace", { threadId: stop.body.threadId, startFrame: 0, levels: 30 });
    const frame = stack.stackFrames[0];
    return { stop, stack, frame, threadId: stop.body.threadId };
  }
  async function stoppedAt(marker, observed, reasons = [marker.startsWith("step:") ? "step" : "breakpoint"]) {
    const { stop, stack, frame, threadId } = observed ?? await readStop();
    assert.ok(frame?.source?.path, `${marker} has an actual source frame`);
    assert.ok(samePath(frame.source.path, config.markers[marker].path), `${marker} source is ${frame.source.path}`);
    assert.equal(frame.line, config.markers[marker].line, `${marker} source line`);
    assert.ok(reasons.includes(stop.body.reason), `${marker} stop reason is ${stop.body.reason}`);
    observations.push({ marker, reason: stop.body.reason, threadId: stop.body.threadId,
      frame: { name: frame.name, path: frame.source.path, line: frame.line },
      stack: stack.stackFrames.map(item => ({ name: item.name, path: item.source?.path, line: item.line })) });
    await save();
    return { threadId, frame, stack };
  }
  try {
    const cpp = vscode.extensions.getExtension("ms-vscode.cpptools");
    assert.ok(cpp, "the official Microsoft C++ extension is staged in the isolated profile");
    await cpp.activate();
    assert.equal(cpp.packageJSON.version, config.cppVersion);
    result.cppVersion = cpp.packageJSON.version;
    for (const marker of Object.values(config.markers)) await vscode.workspace.openTextDocument(marker.path);
    disposables.push(vscode.debug.registerDebugAdapterTrackerFactory("cppvsdbg", {
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
      .map(([, location]) => new vscode.SourceBreakpoint(new vscode.Location(
        vscode.Uri.file(location.path), new vscode.Position(location.line - 1, 0))));
    vscode.debug.addBreakpoints(breakpoints);
    const folder = vscode.workspace.getWorkspaceFolder(vscode.Uri.file(config.entry));
    const launched = await vscode.debug.startDebugging(folder, {
      type: "cppvsdbg", request: "launch", name: "SharpTS native guest debugger acceptance",
      program: config.program, cwd: config.workspace, args: ["40"],
      console: "internalConsole", externalConsole: false, stopAtEntry: false,
      requireExactSource: true, symbolSearchPath: path.dirname(config.nativePdb),
      symbolOptions: { searchPaths: [path.dirname(config.nativePdb)], searchMicrosoftSymbolServer: false },
      logging: { moduleLoad: true, exceptions: true, programOutput: true, engineLogging: true, trace: true, traceResponse: true },
    });
    assert.equal(launched, true, "the native adapter launches the published executable");
    let stop = await stoppedAt("break:entry");
    await session.customRequest("next", { threadId: stop.threadId });
    stop = await stoppedAt("step:call");
    await session.customRequest("stepIn", { threadId: stop.threadId });
    const firstStepIn = await readStop();
    result.stepInDirectToTypeScript = Boolean(firstStepIn.frame?.source?.path &&
      samePath(firstStepIn.frame.source.path, config.markers["break:helper"].path) &&
      firstStepIn.frame.line === config.markers["break:helper"].line);
    if (result.stepInDirectToTypeScript) stop = await stoppedAt("break:helper", firstStepIn, ["step", "breakpoint"]);
    else {
      assert.ok(firstStepIn.stack.stackFrames.some(frame => frame.source?.path &&
        samePath(frame.source.path, config.entry) && frame.line === config.markers["step:call"].line),
      "native StepIn retains the calling TypeScript frame");
      observations.push({ marker: "native:step-in", reason: firstStepIn.stop.body.reason,
        threadId: firstStepIn.threadId, frame: firstStepIn.frame, stack: firstStepIn.stack.stackFrames });
      await save();
      await session.customRequest("continue", { threadId: firstStepIn.threadId });
      stop = await stoppedAt("break:helper");
    }
    assert.ok(stop.stack.stackFrames.slice(1).some(frame => frame.source?.path &&
      samePath(frame.source.path, config.entry) && frame.line === config.markers["step:call"].line),
    "imported helper has its actual TypeScript caller frame");
    // One native source line can bind multiple instruction addresses. Remove
    // the helper breakpoint after its proven hit so it cannot interrupt next.
    const removalStart = transcript.length;
    vscode.debug.removeBreakpoints(breakpoints.filter(breakpoint =>
      samePath(breakpoint.location.uri.fsPath, config.markers["break:helper"].path)));
    await until("helper breakpoint removal acknowledged", () => {
      const request = transcript.slice(removalStart).find(entry => entry.direction === "client" &&
        entry.message.command === "setBreakpoints" && entry.message.arguments.source?.path &&
        samePath(entry.message.arguments.source.path, config.markers["break:helper"].path) &&
        entry.message.arguments.breakpoints?.length === 0);
      return request && transcript.slice(removalStart).some(entry => entry.direction === "adapter" &&
        entry.message.type === "response" && entry.message.request_seq === request.message.seq && entry.message.success);
    }, Boolean);
    await session.customRequest("next", { threadId: stop.threadId });
    stop = await stoppedAt("step:return");
    // Record the native engine's real presentation without assuming managed
    // debugger projection or availability of optimized/hoisted source locals.
    try {
      const scopes = await session.customRequest("scopes", { frameId: stop.frame.id });
      result.locals = [];
      for (const scope of scopes.scopes) if (!scope.expensive && scope.variablesReference)
        result.locals.push(...(await session.customRequest("variables", { variablesReference: scope.variablesReference })).variables);
      result.watch = {};
      for (const expression of ["value", "result"]) {
        try { result.watch[expression] = await session.customRequest("evaluate", {
          expression, frameId: stop.frame.id, context: "watch" }); }
        catch (error) { result.watch[expression] = { unavailable: String(error) }; }
      }
    } catch (error) { result.localsUnavailable = String(error); }
    await save();
    await session.customRequest("continue", { threadId: stop.threadId });
    stop = await stoppedAt("break:output");
    await session.customRequest("continue", { threadId: stop.threadId });
    const exited = await nextEvent("exited");
    assert.equal(exited.body.exitCode, 0);
    await nextEvent("terminated");
    const verified = transcript.filter(entry => entry.direction === "adapter" && entry.message.type === "response" &&
      entry.message.command === "setBreakpoints").flatMap(entry => entry.message.body?.breakpoints ?? []);
    verified.push(...transcript.filter(entry => entry.direction === "adapter" && entry.message.type === "event" &&
      entry.message.event === "breakpoint").map(entry => entry.message.body.breakpoint));
    for (const [marker, location] of Object.entries(config.markers).filter(([marker]) => marker.startsWith("break:")))
      assert.ok(verified.some(item => item.verified && item.line === location.line && item.source?.path &&
        samePath(item.source.path, location.path)), `${marker} native source breakpoint verified`);
    result.verifiedBreakpoints = verified.filter(item => item.verified);
    result.output = transcript.filter(entry => entry.direction === "adapter" && entry.message.type === "event" &&
      entry.message.event === "output" && entry.message.body.category === "stdout")
      .map(entry => entry.message.body.output).join("");
    assert.equal(result.output.replaceAll("\r\n", "\n"), config.expectedOutput);
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
