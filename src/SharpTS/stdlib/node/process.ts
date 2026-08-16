// Node.js 'process' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/process.html.
//
// Heavy lifting (platform detection, argv construction, stdio singletons,
// events, signals, diagnostics, nextTick dispatch) stays in C# behind
// `primitive:process`. This file is a thin, Node-shape facade: every export
// forwards to its matching primitive. Divergence from Node semantics lives in
// the primitive, not here.
//
// The default export is `processObject` — the SAME live object as the bare
// global `process` (epic #1078/#1079). `import process from 'process'` is
// therefore identical to the global: full EventEmitter surface, settable
// exitCode/title, live IPC state. Named exports are convenience bindings;
// data-property named exports are snapshots taken at module-init time.

import {
    processObject as __processObject,
    // Properties (data)
    platform as __platform,
    arch as __arch,
    pid as __pid,
    ppid as __ppid,
    version as __version,
    versions as __versions,
    env as __env,
    argv as __argv,
    argv0 as __argv0,
    execPath as __execPath,
    execArgv as __execArgv,
    exitCode as __exitCode,
    title as __title,
    config as __config,
    release as __release,
    features as __features,
    debugPort as __debugPort,
    allowedNodeEnvironmentFlags as __allowedNodeEnvironmentFlags,
    stdin as __stdin,
    stdout as __stdout,
    stderr as __stderr,
    report as __report,
    // IPC (forked child / cluster worker only; undefined otherwise)
    connected as __connected,
    channel as __channel,
    send as __send,
    disconnect as __disconnect,
    // POSIX identity (undefined on Windows, like Node)
    getuid as __getuid,
    geteuid as __geteuid,
    getgid as __getgid,
    getegid as __getegid,
    getgroups as __getgroups,
    setuid as __setuid,
    setgid as __setgid,
    // Methods
    cwd as __cwd,
    chdir as __chdir,
    exit as __exit,
    hrtime as __hrtime,
    uptime as __uptime,
    memoryUsage as __memoryUsage,
    nextTick as __nextTick,
    kill as __kill,
    abort as __abort,
    umask as __umask,
    cpuUsage as __cpuUsage,
    resourceUsage as __resourceUsage,
    availableMemory as __availableMemory,
    constrainedMemory as __constrainedMemory,
    getActiveResourcesInfo as __getActiveResourcesInfo,
    emitWarning as __emitWarning,
    setSourceMapsEnabled as __setSourceMapsEnabled,
    // EventEmitter surface
    on as __on,
    addListener as __addListener,
    once as __once,
    off as __off,
    removeListener as __removeListener,
    emit as __emit,
    removeAllListeners as __removeAllListeners,
    listeners as __listeners,
    rawListeners as __rawListeners,
    listenerCount as __listenerCount,
    eventNames as __eventNames,
    prependListener as __prependListener,
    prependOnceListener as __prependOnceListener,
    setMaxListeners as __setMaxListeners,
    getMaxListeners as __getMaxListeners,
} from 'primitive:process';

/** The operating system platform (e.g. 'win32', 'linux', 'darwin'). */
export const platform: string = __platform;

/** The CPU architecture (e.g. 'x64', 'arm64'). */
export const arch: string = __arch;

/** The PID of the process. */
export const pid: number = __pid;

/** The PID of the parent process. */
export const ppid: number = __ppid;

/** The emulated Node.js version string (e.g. 'v24.15.0'). */
export const version: string = __version;

/** Version strings of Node (emulated), SharpTS and the .NET runtime. */
export const versions: any = __versions;

/** Environment variables as a string-keyed object. */
export const env: any = __env;

/** Command-line arguments: [runtime_path, script_path, ...userArgs]. */
export const argv: string[] = __argv;

/** The original value of argv[0]. */
export const argv0: string = __argv0;

/** The absolute path of the executable that started the process. */
export const execPath: string = __execPath;

/** Runtime-specific command-line options (SharpTS accepts none: always []). */
export const execArgv: string[] = __execArgv;

/** The current process exit code at module-init time (0 by default). */
export const exitCode: number = __exitCode;

/** The process title at module-init time (live get/set via the default export). */
export const title: string = __title;

/** Build-configuration object (minimal SharpTS shape). */
export const config: any = __config;

/** Release metadata ({ name: 'node', ... }). */
export const release: any = __release;

/** Feature flags of this runtime build. */
export const features: any = __features;

/** The debugger port (Node default 9229; SharpTS has no inspector). */
export const debugPort: number = __debugPort;

/** Set of allowed NODE_OPTIONS flags (empty — SharpTS honors none). */
export const allowedNodeEnvironmentFlags: any = __allowedNodeEnvironmentFlags;

/** Readable stream connected to standard input. */
export const stdin: any = __stdin;

/** Writable stream connected to standard output. */
export const stdout: any = __stdout;

/** Writable stream connected to standard error. */
export const stderr: any = __stderr;

/** Diagnostic report API: getReport()/writeReport() + config properties. */
export const report: any = __report;

/** True when an IPC channel to a parent process is open (forked child). */
export const connected: boolean = __connected;

/** The IPC channel control object while connected (undefined otherwise). */
export const channel: any = __channel;

/** Sends a message over the IPC channel (forked child; undefined otherwise). */
export const send: any = __send;

/** Closes the IPC channel (forked child; undefined otherwise). */
export const disconnect: any = __disconnect;

/** POSIX only (undefined on Windows): numeric user identity of the process. */
export const getuid: any = __getuid;
export const geteuid: any = __geteuid;
export const getgid: any = __getgid;
export const getegid: any = __getegid;
export const getgroups: any = __getgroups;
export const setuid: any = __setuid;
export const setgid: any = __setgid;

/** Returns the current working directory. */
export function cwd(): string { return __cwd(); }

/** Changes the current working directory. */
export function chdir(directory: string): void { __chdir(directory); }

/** Terminates the process with the given exit code (defaults to process.exitCode). */
export function exit(code?: number): void { __exit(code as any); }

/**
 * High-resolution timer: hrtime(prev?) returns a [seconds, nanoseconds] tuple;
 * hrtime.bigint() returns nanoseconds as a bigint.
 */
export const hrtime: any = __hrtime;

/** Returns the number of seconds the current process has been running. */
export function uptime(): number { return __uptime(); }

/**
 * Returns an object describing process memory usage in bytes;
 * memoryUsage.rss() returns just the resident set size.
 */
export const memoryUsage: any = __memoryUsage;

/** Sends a signal to a process (signal 0 tests for existence). */
export function kill(pid: number, signal?: any): boolean { return __kill(pid, signal); }

/** Aborts the process immediately (abnormal termination, no 'exit' event). */
export function abort(): void { __abort(); }

/** Gets, or sets and returns the previous, file-mode creation mask. */
export function umask(mask?: any): number { return __umask(mask); }

/** CPU time used by the process, { user, system } in microseconds. */
export function cpuUsage(previousValue?: any): { user: number; system: number } {
    return __cpuUsage(previousValue);
}

/** Resource usage of the process (Node shape; libuv counters report 0). */
export function resourceUsage(): any { return __resourceUsage(); }

/** Free memory (bytes) still available to the process. */
export function availableMemory(): number { return __availableMemory(); }

/** The memory limit imposed on the process, or 0 when unknown. */
export function constrainedMemory(): number { return __constrainedMemory(); }

/** Active resource types keeping the event loop alive (approximation). */
export function getActiveResourcesInfo(): string[] { return __getActiveResourcesInfo(); }

/** Emits a process warning: fires 'warning' and prints the default stderr line. */
export function emitWarning(warning: any, options?: any, code?: any, ctor?: any): void {
    __emitWarning(warning, options, code, ctor);
}

/** Enables/disables source-map support (accepted no-op in SharpTS). */
export function setSourceMapsEnabled(enabled: boolean): void { __setSourceMapsEnabled(enabled); }

// `nextTick` forwards its trailing `...args` straight to the primitive; the
// built-in module emitters expand a trailing `Expr.Spread` at runtime (see
// ProcessModuleEmitter.EmitArgsArray), so there is no arity ceiling.
export function nextTick(callback: any, ...args: any[]): void {
    __nextTick(callback, ...args);
}

/** Registers an event listener on the process (returns the process object). */
export function on(event: string, listener: any): any { return __on(event, listener); }
export function addListener(event: string, listener: any): any { return __addListener(event, listener); }
export function once(event: string, listener: any): any { return __once(event, listener); }
export function off(event: string, listener: any): any { return __off(event, listener); }
export function removeListener(event: string, listener: any): any { return __removeListener(event, listener); }
export function emit(event: string, ...args: any[]): boolean { return __emit(event, ...args); }
export function removeAllListeners(event?: string): any { return __removeAllListeners(event as any); }
export function listeners(event: string): any[] { return __listeners(event); }
export function rawListeners(event: string): any[] { return __rawListeners(event); }
export function listenerCount(event: string): number { return __listenerCount(event); }
export function eventNames(): string[] { return __eventNames(); }
export function prependListener(event: string, listener: any): any { return __prependListener(event, listener); }
export function prependOnceListener(event: string, listener: any): any { return __prependOnceListener(event, listener); }
export function setMaxListeners(n: number): any { return __setMaxListeners(n); }
export function getMaxListeners(): number { return __getMaxListeners(); }

// The default export IS the live global process object (identity + behavior:
// events registered here fire for the global and vice versa; exitCode/title
// assignment works). Node's `require('process') === process` equivalence.
export default __processObject;
