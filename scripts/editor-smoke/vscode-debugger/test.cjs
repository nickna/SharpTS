const assert = require("node:assert/strict");
const { createHash } = require("node:crypto");
const fs = require("node:fs/promises");
const syncFs = require("node:fs");
const path = require("node:path");
const vscode = require("vscode");

const delay = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
const samePath = (left, right) => typeof left === "string" && typeof right === "string"
  && (process.platform === "win32" ? path.resolve(left).toLowerCase() === path.resolve(right).toLowerCase()
    : path.resolve(left) === path.resolve(right));
async function until(description, query, predicate = Boolean, timeout = 20_000) {
  const deadline = Date.now() + timeout;
  let value;
  do {
    value = await query();
    if (predicate(value)) return value;
    await delay(30);
  } while (Date.now() < deadline);
  assert.fail(`${description}: ${JSON.stringify(value)}`);
}

exports.run = async function run() {
  const config = JSON.parse(await fs.readFile(process.env.SHARPTS_DEBUGGER_SMOKE_CONFIG, "utf8"));
  const sessions = [];
  const observations = [];
  const subscriptions = [];
  let extension;
  function pendingRequests(record) {
    return record.messages.filter((item) => item.direction === "client" && item.message.type === "request"
      && !record.messages.some((response) => response.direction === "adapter" && response.message.type === "response"
        && response.message.request_seq === item.message.seq)).map((item) => ({ seq: item.message.seq, command: item.message.command }));
  }
  // Track real VS Code traffic. Do not register an adapter descriptor, debug configuration
  // provider, command replacement, or spawn an adapter directly from this harness.
  subscriptions.push(vscode.debug.registerDebugAdapterTrackerFactory("sharpts-interpreter", {
    createDebugAdapterTracker(session) {
      const record = { session, messages: [], errors: [], ended: false, adapterStopped: false };
      sessions.push(record);
      const capture = (direction, message) => {
        record.messages.push({ direction, message });
        syncFs.appendFileSync(path.join(config.artifacts, "dap-transcript.ndjson"), JSON.stringify({
          session: session.name, direction, message,
        }) + "\n");
      };
      return {
        onWillReceiveMessage(message) { capture("client", message); },
        onDidSendMessage(message) { capture("adapter", message); },
        onError(error) {
          const details = { text: String(error), afterMessages: record.messages.length,
            debuggeeExitCode: record.messages.find((item) => item.direction === "adapter" && item.message.event === "exited")?.message.body.exitCode,
            terminated: record.messages.some((item) => item.direction === "adapter" && item.message.event === "terminated"),
            disconnectSucceeded: record.messages.some((item) => item.direction === "adapter"
              && item.message.command === "disconnect" && item.message.success === true),
            adapterStopped: record.adapterStopped, adapterExit: record.adapterExit, pendingRequests: pendingRequests(record) };
          record.errors.push(details);
          capture("tracker", { type: "error", ...details });
        },
        onExit(code, signal) {
          record.adapterExit = { code, signal };
          capture("tracker", { type: "exit", code, signal });
        },
        onWillStopSession() { record.adapterStopped = true; },
      };
    },
  }));
  subscriptions.push(vscode.debug.onDidTerminateDebugSession((session) => {
    for (const record of sessions.filter((item) => item.session.id === session.id)) record.ended = true;
  }));
  function event(record, name, from = 0, predicate = () => true) {
    return until(`session ${record.session.name}: ${name} event after ${from}`, () => {
      const index = record.messages.findIndex((item, i) => i >= from && item.direction === "adapter"
        && item.message.type === "event" && item.message.event === name && predicate(item.message.body));
      return index >= 0 ? { index, body: record.messages[index].message.body } : undefined;
    });
  }
  async function start(configuration, command = false) {
    const before = sessions.length;
    let timer;
    const launching = command ? vscode.commands.executeCommand("sharpts.debugInterpreter")
      : vscode.debug.startDebugging(vscode.workspace.workspaceFolders[0], configuration);
    const started = await Promise.race([launching, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error("VS Code debug launch exceeded 30 seconds; inspect dap-transcript.ndjson")), 30_000);
    })]).finally(() => clearTimeout(timer));
    if (!command) assert.equal(started, true);
    return until("shipping extension debug session", () => sessions[before]);
  }
  async function request(record, command, args = {}) {
    return record.session.customRequest(command, args);
  }
  async function resume(record, threadId, command = "continue") {
    const from = record.messages.length;
    await request(record, command, { threadId });
    return from;
  }
  async function stack(record, stop) {
    const response = await request(record, "stackTrace", { threadId: stop.body.threadId });
    assert.ok(response.stackFrames.length > 0, "a stop must expose source frames");
    return response.stackFrames;
  }
  async function inspect(record, frame, label) {
    const scopes = (await request(record, "scopes", { frameId: frame.id })).scopes;
    assert.ok(scopes.length > 0, "a source frame must expose scopes");
    const variables = [];
    for (const scope of scopes)
      variables.push({ scope: scope.name, variables: (await request(record, "variables", {
        variablesReference: scope.variablesReference,
      })).variables });
    observations.push({ label, frame, scopes: variables });
    return variables.flatMap((item) => item.variables);
  }
  async function evaluate(record, frame, expression, expected, context = "watch") {
    const result = await request(record, "evaluate", { frameId: frame.id, expression, context });
    assert.equal(result.result, expected, expression);
    observations.push({ label: "evaluate", expression, context, result: result.result });
    return result;
  }
  async function completed(record, expectedExit = 0) {
    const exited = await event(record, "exited");
    assert.equal(exited.body.exitCode, expectedExit);
    await event(record, "terminated");
    await until("VS Code terminates the debug session", () => record.ended);
    await until("the disconnected adapter exits", () => record.adapterExit !== undefined);
    assertRecordedErrors(record);
  }
  function expectedShutdownDiagnostic(record, error) {
    const naturalExit = record.adapterExit?.code === 0;
    const windowsClientKill = process.platform === "win32" && record.adapterExit?.code === 1 && error.adapterStopped;
    return error.text === "Error: read error" && error.terminated && error.disconnectSucceeded
      && Number.isInteger(error.debuggeeExitCode) && error.pendingRequests.length === 0
      && (naturalExit || windowsClientKill);
  }
  function assertRecordedErrors(record) {
    // VS Code's executable-adapter transport unconditionally reports stdout's `close`
    // event as Error("read error"), including after a successful disconnect. Accept only
    // that exact EOF notification after exited, terminated, and the disconnect acknowledgement.
    // On Windows VS Code then forcibly kills the adapter tree; its own teardown can yield
    // process code 1 even though the protocol shutdown completed successfully.
    const unexpected = record.errors.filter((error) => !expectedShutdownDiagnostic(record, error));
    assert.deepEqual(unexpected, [], `${record.session.name}: no in-session transport errors`);
  }
  function output(record) {
    return record.messages.filter((item) => item.direction === "adapter" && item.message.event === "output")
      .map((item) => item.message.body.output).join("");
  }
  const source = (name) => path.join(config.workspace, name);
  const breakpoint = (file, line) => new vscode.SourceBreakpoint(new vscode.Location(
    vscode.Uri.file(source(file)), new vscode.Position(line - 1, 0)));
  const launch = (name, additions = {}) => ({ type: "sharpts-interpreter", request: "launch", name,
    program: source("main.ts"), cwd: config.workspace, project: config.project,
    args: ["alpha", "beta"], env: { SHARPTS_DAP_ACCEPTANCE: "configured", SHARPTS_DAP_EXCEPTION: "none" },
    references: [], console: "internalConsole", justMyCode: true, diagnostics: "all", ...additions });
  try {
    extension = vscode.extensions.getExtension("sharpts.vscode-sharpts");
    assert.ok(extension, "the actual SharpTS extension must be present");
    await extension.activate();
    assert.equal(extension.isActive, true);
    if (config.mode === "development") assert.ok(samePath(extension.extensionPath, config.developmentExtension));
    else {
      assert.equal(extension.packageJSON.version, config.package.version);
      assert.ok(samePath(path.dirname(extension.extensionPath), config.extensions),
        "the VSIX run must use the isolated installed extension");
    }
    for (const [name, expected] of Object.entries(config.binaryHashes)) {
      const bytes = await fs.readFile(path.join(extension.extensionPath, "bin/server", name));
      assert.equal(createHash("sha256").update(bytes).digest("hex"), expected, `${name} must be the coordinated binary`);
    }
    const debuggerContribution = extension.packageJSON.contributes.debuggers.find((item) => item.type === "sharpts-interpreter");
    assert.ok(debuggerContribution?.initialConfigurations.length > 0);

    // The real command must save a dirty buffer and forward resource-scoped settings.
    const unrelated = await vscode.workspace.openTextDocument(vscode.Uri.file(source("library.ts")));
    const unrelatedEdit = new vscode.WorkspaceEdit();
    unrelatedEdit.insert(unrelated.uri, unrelated.positionAt(unrelated.getText().length), "// unrelated-dirty-buffer\n");
    assert.equal(await vscode.workspace.applyEdit(unrelatedEdit), true);
    assert.equal(unrelated.isDirty, true);
    const uri = vscode.Uri.file(source("command.ts"));
    const document = await vscode.workspace.openTextDocument(uri);
    await vscode.window.showTextDocument(document);
    const edit = new vscode.WorkspaceEdit();
    edit.insert(uri, document.positionAt(document.getText().length), "// dirty-editor-save-verified\n");
    assert.equal(await vscode.workspace.applyEdit(edit), true);
    assert.equal(document.isDirty, true);
    const commandBreakpoint = breakpoint("command.ts", 2);
    vscode.debug.addBreakpoints([commandBreakpoint]);
    const command = await start(undefined, true);
    let stopped = await event(command, "stopped");
    assert.equal(document.isDirty, false);
    assert.equal(unrelated.isDirty, true, "debugging the active document must not save unrelated documents");
    assert.match(await fs.readFile(source("command.ts"), "utf8"), /dirty-editor-save-verified/);
    const commandLaunch = command.messages.find((item) => item.direction === "client" && item.message.command === "launch").message.arguments;
    assert.ok(samePath(commandLaunch.program, source("command.ts")));
    assert.ok(samePath(commandLaunch.cwd, config.workspace));
    assert.ok(samePath(commandLaunch.project, config.project));
    assert.deepEqual(commandLaunch.references, config.references);
    let frames = await stack(command, stopped);
    await inspect(command, frames[0], "command-module-scope");
    await evaluate(command, frames[0], "savedValue + 2", "42");
    await resume(command, stopped.body.threadId);
    await completed(command);
    assert.match(output(command), /saved=42/);
    vscode.debug.removeBreakpoints([commandBreakpoint]);
    // Restore the unrelated buffer before checking breakpoints against committed source checksums.
    const restore = new vscode.WorkspaceEdit();
    restore.replace(unrelated.uri, new vscode.Range(unrelated.positionAt(0), unrelated.positionAt(unrelated.getText().length)),
      await fs.readFile(source("library.ts"), "utf8"));
    assert.equal(await vscode.workspace.applyEdit(restore), true);
    assert.equal(await unrelated.save(), true);

    // Exercise the committed multi-file acceptance workload using VS Code source breakpoints.
    const breakpoints = [breakpoint("main.ts", 5), breakpoint("main.ts", 9),
      breakpoint("library.ts", 11), breakpoint("library.ts", 17), breakpoint("library.ts", 23),
      breakpoint("worker.ts", 1)];
    vscode.debug.addBreakpoints(breakpoints);
    // Resolve ${workspaceFolder}, launch schema/defaults, and diagnostics through the actual launch.json.
    const normal = await start("SharpTS interpreter acceptance");
    stopped = await event(normal, "stopped");
    frames = await stack(normal, stopped);
    assert.ok(samePath(frames[0].source.path, source("main.ts")));
    assert.equal(frames[0].line, 5);
    const moduleVariables = await inspect(normal, frames[0], "class-module-scope");
    const counter = moduleVariables.find((item) => item.name === "counter");
    assert.ok(counter?.variablesReference > 0, "class instances must be expandable");
    const members = (await request(normal, "variables", { variablesReference: counter.variablesReference })).variables;
    assert.ok(members.some((item) => item.name === "value" && item.value === "1"));
    let from = await resume(normal, stopped.body.threadId, "stepIn");
    stopped = await event(normal, "stopped", from);
    frames = await stack(normal, stopped);
    assert.equal(frames[0].name, "increment");
    assert.ok(samePath(frames[0].source.path, source("library.ts")));
    assert.ok(frames.some((frame) => samePath(frame.source.path, source("main.ts"))), "stack must retain the importing caller");
    await inspect(normal, frames[0], "method-locals-arguments-this");
    await evaluate(normal, frames[0], "this.value", "1");
    await assert.rejects(() => request(normal, "evaluate", {
      frameId: frames[0].id, expression: "this.value = 99", context: "repl",
    }), /read-only|not allowed/i);
    from = await resume(normal, stopped.body.threadId, "next");
    stopped = await event(normal, "stopped", from);
    frames = await stack(normal, stopped);
    assert.equal(frames[0].line, 6);
    await evaluate(normal, frames[0], "this.value", "2", "repl");
    from = await resume(normal, stopped.body.threadId, "stepOut");
    stopped = await event(normal, "stopped", from);
    frames = await stack(normal, stopped);
    assert.ok(samePath(frames[0].source.path, source("main.ts")));
    const seen = new Set();
    from = await resume(normal, stopped.body.threadId);
    while (seen.size < 6) {
      stopped = await event(normal, "stopped", from, (body) => body.reason === "breakpoint");
      frames = await stack(normal, stopped);
      const top = frames[0];
      if (samePath(top.source.path, source("library.ts")) && top.line === 11) {
        assert.equal(top.name, "makeClosure");
        const variables = await inspect(normal, top, "function-arguments");
        assert.ok(variables.some((item) => item.name === "seed" && item.value === "10"));
        from = await resume(normal, stopped.body.threadId, "next");
        stopped = await event(normal, "stopped", from);
        frames = await stack(normal, stopped);
        await inspect(normal, frames[0], "closure-captured-value");
        await evaluate(normal, frames[0], "captured", "10", "hover");
        seen.add("function");
      } else if (samePath(top.source.path, source("main.ts")) && top.line === 10) {
        const value = await request(normal, "evaluate", { frameId: top.id, expression: "index", context: "watch" });
        assert.ok(["0", "1"].includes(value.result));
        await inspect(normal, top, `loop-${value.result}`);
        seen.add(`loop-${value.result}`);
      } else if (samePath(top.source.path, source("library.ts")) && top.line === 17) {
        assert.equal(top.name, "afterAwait");
        await inspect(normal, top, "async-resumption");
        await evaluate(normal, top, "value", "40");
        seen.add("async");
      } else if (samePath(top.source.path, source("library.ts")) && top.line === 23) {
        assert.equal(top.name, "values");
        await inspect(normal, top, "generator-resumption");
        await evaluate(normal, top, "resumed", "2");
        seen.add("generator");
      } else if (samePath(top.source.path, source("worker.ts")) && top.line === 1) {
        assert.ok(stopped.body.threadId > 1, "worker must expose a distinct debug thread");
        if (!stopped.body.allThreadsStopped)
          await event(normal, "stopped", stopped.index, (body) => body.allThreadsStopped);
        const threads = (await request(normal, "threads")).threads;
        assert.ok(threads.some((thread) => thread.id === stopped.body.threadId));
        from = await resume(normal, stopped.body.threadId, "next");
        stopped = await event(normal, "stopped", from, (body) => body.threadId > 1 && body.reason === "step");
        if (!stopped.body.allThreadsStopped)
          await event(normal, "stopped", stopped.index, (body) => body.allThreadsStopped);
        frames = await stack(normal, stopped);
        assert.ok(samePath(frames[0].source.path, source("worker.ts")));
        assert.equal(frames[0].line, 2);
        await inspect(normal, frames[0], "worker-scope");
        await evaluate(normal, frames[0], "workerValue + 1", "42");
        seen.add("worker");
      } else assert.fail(`unexpected acceptance stop: ${JSON.stringify({ stopped, frames })}`);
      from = await resume(normal, stopped.body.threadId);
    }
    await completed(normal);
    const text = output(normal);
    for (const line of ["class=2", "closure=15", "caught=acceptance", "finally=ran", "args=alpha,beta",
      "env=configured", "async=42", "yield=2", "promise=microtask", "timer=callback", "worker=42"])
      assert.ok(text.includes(line), `Debug Console output must include ${line}`);
    const breakpointResponses = normal.messages.filter((item) => item.direction === "adapter"
      && item.message.type === "response" && item.message.command === "setBreakpoints");
    assert.ok(breakpointResponses.some((item) => item.message.body.breakpoints.some((bp) => bp.verified
      && bp.line === 10 && samePath(bp.source.path, source("main.ts")))), "comment breakpoint must bind to line 10");
    const loaded = normal.messages.find((item) => item.direction === "adapter" && item.message.command === "initialize").message.body;
    assert.equal(loaded.supportsSetVariable, false);
    assert.equal(loaded.supportsSetExpression, false);
    assert.equal(loaded.supportsSingleThreadExecutionRequests, false);
    vscode.debug.removeBreakpoints(breakpoints);

    for (const [filter, mode, expectedExit, message] of [
      ["caught", "none", 0, "acceptance"], ["uncaught", "uncaught", 1, "uncaught acceptance"],
      ["unhandledRejection", "unhandled", 1, "unhandled acceptance"],
    ]) {
      const record = await start(launch(`exception-${filter}`, { stopOnEntry: true,
        env: { SHARPTS_DAP_ACCEPTANCE: "configured", SHARPTS_DAP_EXCEPTION: mode } }));
      stopped = await event(record, "stopped");
      assert.equal(stopped.body.reason, "entry");
      await request(record, "setExceptionBreakpoints", { filters: [filter] });
      from = await resume(record, stopped.body.threadId);
      stopped = await event(record, "stopped", from, (body) => body.reason === "exception");
      frames = await stack(record, stopped);
      assert.ok(samePath(frames[0].source.path, source("main.ts")));
      const exception = await request(record, "exceptionInfo", { threadId: stopped.body.threadId });
      assert.ok(exception.description.includes(message), JSON.stringify(exception));
      assert.equal(exception.breakMode, filter === "caught" ? "always" : "unhandled");
      observations.push({ label: filter, exception, frames });
      await request(record, "setExceptionBreakpoints", { filters: [] });
      await resume(record, stopped.body.threadId);
      await completed(record, expectedExit);
    }

    // Pause a running loop, terminate it, and immediately start a new session in the same window.
    const terminating = await start(launch("pause-and-terminate", {
      program: source("terminate.ts"), stopOnEntry: true,
    }));
    stopped = await event(terminating, "stopped");
    from = await resume(terminating, stopped.body.threadId);
    await request(terminating, "pause", { threadId: stopped.body.threadId });
    stopped = await event(terminating, "stopped", from, (body) => body.reason === "pause");
    frames = await stack(terminating, stopped);
    assert.ok(samePath(frames[0].source.path, source("terminate.ts")));
    await request(terminating, "terminate");
    await event(terminating, "terminated");
    await until("terminate request ends the VS Code session", () => terminating.ended);

    const restarting = await start(launch("restart-and-stop", {
      program: source("terminate.ts"), stopOnEntry: true,
    }));
    await event(restarting, "stopped", 0, (body) => body.reason === "entry");
    await until("paused session becomes the active VS Code session", () =>
      vscode.debug.activeDebugSession?.id === restarting.session.id);
    const beforeRestart = sessions.length;
    // supportsRestartRequest is false: VS Code must stop the old adapter and launch a new one.
    await vscode.commands.executeCommand("workbench.action.debug.restart");
    const restarted = await until("VS Code restart creates a fresh session", () => sessions[beforeRestart]);
    await until("restart stops the previous adapter", () => restarting.adapterStopped);
    await event(restarted, "stopped", 0, (body) => body.reason === "entry");
    assert.ok(restarted.messages.some((item) => item.direction === "client" && item.message.command === "initialize"));
    assert.ok(restarted.messages.some((item) => item.direction === "client" && item.message.command === "launch"));
    // VS Code can preserve the public DebugSession ID while replacing its adapter.
    observations.push({ label: "vscode-restart", previousAdapterStopped: restarting.adapterStopped,
      sessionIdPreserved: restarted.session.id === restarting.session.id });
    await vscode.debug.stopDebugging(restarted.session);
    await event(restarted, "terminated");
    await until("VS Code Stop ends the paused session", () => restarted.ended);

    const stale = await start(launch("changed-source-rejection", {
      program: source("command.ts"), stopOnEntry: true,
    }));
    await event(stale, "stopped", 0, (body) => body.reason === "entry");
    const original = await fs.readFile(source("command.ts"), "utf8");
    const changedBreakpoint = breakpoint("command.ts", 2);
    try {
      await fs.writeFile(source("command.ts"), original + "// changed-after-debug-launch\n");
      const beforeBinding = stale.messages.length;
      vscode.debug.addBreakpoints([changedBreakpoint]);
      const binding = await until("VS Code receives a stale-source breakpoint rejection", () =>
        stale.messages.slice(beforeBinding).find((item) => item.direction === "adapter"
          && item.message.type === "response" && item.message.command === "setBreakpoints"
          && item.message.body?.breakpoints?.some((bp) => samePath(bp.source?.path, source("command.ts"))
            && !bp.verified && /changed/i.test(bp.message))));
      observations.push({ label: "changed-source-rejection", breakpoints: binding.message.body.breakpoints });
    } finally {
      await fs.writeFile(source("command.ts"), original);
      vscode.debug.removeBreakpoints([changedBreakpoint]);
    }
    await vscode.debug.stopDebugging(stale.session);
    await event(stale, "terminated");
    await until("changed-source session ends", () => stale.ended);
    const relaunched = await start(launch("fresh-session-after-terminate", { program: source("command.ts") }));
    await completed(relaunched);
    assert.match(output(relaunched), /saved=42/);
    for (const record of sessions) {
      await until(`${record.session.name}: adapter process exits`, () => record.adapterExit !== undefined);
      assert.deepEqual(pendingRequests(record), [], `${record.session.name}: every DAP request must receive a correlated response`);
      assertRecordedErrors(record);
    }

    await fs.writeFile(config.result, JSON.stringify({ status: "passed", mode: config.mode,
      editor: `VS Code ${vscode.version}`, platform: `${process.platform}-${process.arch}`,
      extension: { version: extension.packageJSON.version, path: extension.extensionPath },
      sourceCommit: config.sourceCommit, binaryHashes: config.binaryHashes, package: config.package,
      adapterFactoriesRegisteredByHarness: 0, adaptersSpawnedByHarness: 0,
      presentationEvidence: "VS Code debug APIs and actual extension-host DAP traffic; no visual UI assertions",
      coverage: ["dirty-command-save", "project-and-reference-forwarding", "launch-json", "bound-source-breakpoints",
        "comment-line-snapping", "class-expansion", "method-and-function-scopes", "closure-values", "loop-stops",
        "watch-hover-repl", "read-only-evaluation", "step-in-next-out", "async-generator-resumption",
        "worker-thread-step", "debug-console-output", "caught-uncaught-unhandled-exceptions", "pause-terminate-relaunch",
        "vscode-restart-and-stop", "changed-source-rejection-and-relaunch"],
      sessions: sessions.map((record) => ({ name: record.session.name, ended: record.ended, adapterStopped: record.adapterStopped,
        stops: record.messages.filter((item) => item.message.event === "stopped").map((item) => item.message.body),
        output: output(record), errors: record.errors.filter((error) => !expectedShutdownDiagnostic(record, error)),
        expectedShutdownDiagnostics: record.errors.filter((error) => expectedShutdownDiagnostic(record, error)),
        adapterExit: record.adapterExit })), observations }, null, 2) + "\n");
  } catch (error) {
    await fs.writeFile(config.result, JSON.stringify({ status: "failed", mode: config.mode,
      error: error.stack ?? String(error), observations }, null, 2) + "\n");
    throw error;
  } finally {
    await fs.writeFile(path.join(config.artifacts, "dap-transcript.json"), JSON.stringify(sessions.map((record) => ({
      name: record.session.name, messages: record.messages, errors: record.errors, ended: record.ended,
      adapterStopped: record.adapterStopped,
      adapterExit: record.adapterExit,
    })), null, 2) + "\n");
    for (const record of sessions.filter((item) => !item.ended)) await vscode.debug.stopDebugging(record.session);
    for (const subscription of subscriptions) subscription.dispose();
    vscode.debug.removeBreakpoints(vscode.debug.breakpoints);
  }
};
