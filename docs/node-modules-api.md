# Node.js built-in modules API reference

SharpTS provides a maintained subset of Node-compatible built-in modules for TypeScript programs.
This is the user API reference. Implementation location and capability status belong only in
[STATUS.md](../STATUS.md#4-nodejs-built-in-modules); if an API is absent here, do not infer support
from an internal class with a similar name.

The reference is aligned with the embedded TypeScript declarations, `BuiltInModuleTypes`, and the
dual-mode tests under `tests/SharpTS.Tests/SharedTests/BuiltInModules`.

## Imports and conventions

Bare and `node:` specifiers resolve to the same module:

```typescript
import fs from "fs";
import { readFileSync } from "node:fs";
import * as path from "path";

const os = require("node:os");
```

Default, named, and namespace ESM imports are available. CommonJS `require()` returns the same
public exports. Most Node callbacks use the error-first `(error, value) => void` shape. Promise
subpaths expose the corresponding asynchronous operations as promises.

This is a compatible subset rather than a declaration that all APIs from a particular Node version
exist. Platform facilities follow the host OS and .NET runtime. Use feature tests when an
application depends on an advanced option.

## assert

`assert` exports the callable `ok` assertion plus `equal`, `notEqual`, `strictEqual`,
`notStrictEqual`, `deepEqual`, `notDeepEqual`, `deepStrictEqual`, `notDeepStrictEqual`, `throws`,
`doesNotThrow`, `rejects`, `doesNotReject`, `fail`, `match`, and `doesNotMatch`. `assert.strict`
selects strict comparisons. Failed assertions expose an `AssertionError`-shaped object with
`actual`, `expected`, and `operator` fields.

```typescript
import assert from "assert";

assert.strictEqual(2 + 2, 4);
await assert.rejects(Promise.reject(new Error("expected")));
```

## buffer

`buffer` exports `Buffer`, `SlowBuffer`, `atob`, `btoa`, `isUtf8`, `isAscii`, `transcode`, constants,
and size limits. The global `Buffer` is the same constructor.

Static APIs include `Buffer.from`, `alloc`, `allocUnsafe`, `concat`, `isBuffer`, `isEncoding`,
`byteLength`, and `compare`. Instances support string conversion, slice/subarray, copy, write, fill,
search/compare/equality, integer reads/writes, and `toJSON`. Maintained encodings include UTF-8,
UTF-16LE/UCS-2, ASCII/Latin-1/binary, hex, base64, and base64url where the operation accepts them.

## crypto

The maintained crypto surface includes:

- hashes/HMAC: `createHash`, `createHmac`, one-shot `hash`, `Hash.copy`, SHA-2, SHA-3 and supported
  SHAKE output lengths;
- randomness: `randomBytes`, `randomFill`, `randomFillSync`, `randomInt`, and `randomUUID`;
- symmetric crypto: `createCipheriv`, `createDecipheriv`, GCM tags, and cipher discovery;
- password/KDF APIs: PBKDF2, scrypt, HKDF, sync and callback forms;
- signatures and keys: `createSign`, `createVerify`, one-shot `sign`/`verify`, key generation/import,
  `KeyObject`, RSA encrypt/decrypt, Diffie-Hellman, and ECDH;
- prime utilities, timing-safe equality, algorithm discovery, constants, FIPS accessors, and
  `X509Certificate`.

```typescript
import { createHash, randomUUID } from "crypto";

const digest = createHash("sha256").update("hello").digest("hex");
console.log(digest, randomUUID());
```

Available algorithms are those accepted by the current declaration/runtime pair and the host .NET
cryptography provider. Unsupported EdDSA/X25519/X448 operations raise a clear error. Some advanced
key import/export and certificate helpers remain subject to the mode ceilings listed in status.

## fs

`fs` provides synchronous, callback, promise, stream, and watch operations. The maintained surface
includes file read/write/append/copy/rename/remove, directory creation/list/removal, stat/lstat,
access/realpath, links, permissions/timestamps, file descriptors, temporary directories,
`createReadStream`, `createWriteStream`, `watch`, `watchFile`, and `unwatchFile`.

```typescript
import fs from "fs";

const text = fs.readFileSync("input.txt", "utf8");
fs.writeFileSync("output.txt", text.toUpperCase());

const stat = fs.statSync("output.txt");
console.log(stat.isFile(), stat.size);
```

Filesystem errors carry Node-style codes such as `ENOENT`, `EACCES`, `EEXIST`, `EISDIR`,
`ENOTDIR`, and `ENOTEMPTY`. Path, permission, symlink, and ownership behavior remains
platform-dependent.

## fs/promises

`fs/promises` exposes promise-returning read/write/append/copy/rename/remove, directory,
stat/lstat/access/realpath, link, permission, and timestamp operations from the maintained `fs`
surface:

```typescript
import { readFile, writeFile } from "fs/promises";

const text = await readFile("input.txt", "utf8");
await writeFile("output.txt", text);
```

## path

`path` exports `join`, `resolve`, `dirname`, `basename`, `extname`, `normalize`, `isAbsolute`,
`relative`, `parse`, `format`, `sep`, and `delimiter`. `path.posix` and `path.win32` expose explicit
platform variants.

```typescript
import path from "path";

console.log(path.join("dist", "app.dll"));
console.log(path.parse("/tmp/archive.tar.gz").ext);
```

## os

`os` exports `platform`, `arch`, `cpus`, `hostname`, `homedir`, `tmpdir`, `type`, `release`,
`uptime`, `totalmem`, `freemem`, `networkInterfaces`, `loadavg`, `userInfo`, and `EOL`.

## process

The default/named `process` module and global `process` refer to the same live object. The public
surface includes:

- identity/configuration: `argv`, `execArgv`, `argv0`, `execPath`, `pid`, `ppid`, `platform`,
  `arch`, `version`, `versions`, `release`, `features`, `config`, and `title`;
- environment/directories: live `env`, `cwd`, `chdir`, `umask`, and supported POSIX identity APIs;
- lifecycle: `exit`, `abort`, `exitCode`, `beforeExit`, `exit`, warning and signal events;
- scheduling/timing: `nextTick`, `hrtime`, `hrtime.bigint`, CPU/resource/memory APIs;
- stdio: `stdin`, `stdout`, and `stderr` stream objects;
- process control and IPC: `kill`, `send`, `disconnect`, `connected`, and `channel` where applicable;
- reports, source-map toggles, active-resource inspection, and EventEmitter methods.

```typescript
import process from "process";

console.log(process.cwd(), process.platform);
process.nextTick(() => console.log("next tick"));
```

Hosts can restrict cross-process signaling. POSIX-only APIs are absent on Windows. See status for
the remaining compiled lifecycle/POSIX ceilings.

## events

`events` exports `EventEmitter` with `on`, `once`, `emit`, `off`, `removeListener`,
`removeAllListeners`, `listenerCount`, `listeners`, `prependListener`, `prependOnceListener`,
`setMaxListeners`, `getMaxListeners`, and `eventNames`, plus maintained static helpers.

## timers

`timers` exports `setTimeout`, `clearTimeout`, `setInterval`, `clearInterval`, `setImmediate`, and
`clearImmediate`; the same functions are globals. Callback arguments after the delay are forwarded.

## timers/promises

`timers/promises` exports promise-returning `setTimeout`, `setImmediate`, and async-iterable
`setInterval`. Delay/immediate options accept an abort signal where declared.

```typescript
import { setTimeout as delay } from "timers/promises";

await delay(100);
```

## async_hooks

`async_hooks` exposes `AsyncLocalStorage` with `run`, `getStore`, `enterWith`, `exit`, and `disable`.
Context flows through .NET asynchronous execution. Optional trailing callback arguments on `run`
and `exit` are a known facade gap; close over values instead.

## perf_hooks

`perf_hooks` exports `performance` (`now`, `timeOrigin`, marks, measures, entry queries and clears)
and `PerformanceObserver` for mark/measure entries.

## readline

`readline` exports `createInterface`. The returned interface supports EventEmitter methods,
`question`, `questionSync`, `prompt`, `pause`, `resume`, `write`, `setPrompt`, `getPrompt`, and
`close`.

## stream

`stream` exports `Readable`, `Writable`, `Duplex`, `Transform`, and `PassThrough`, plus `pipeline`,
`finished`, and `addAbortSignal`. Maintained behavior includes object mode, high-water marks,
backpressure/drain, common lifecycle events, `Readable.from`, stream predicates, and iterable
helpers such as `toArray`, `forEach`, `map`, and `filter`.

## stream/promises

`stream/promises` exposes promise-returning `pipeline` and `finished`.

## stream/web

`stream/web` exposes the maintained Web Streams constructors and strategies used by fetch and
stream interop. Use the declared constructors rather than Node internals.

## http and https

Both modules expose `createServer`, `request`, `get`, `Agent`, and `globalAgent`; `http` also exports
method/status-code tables. Servers support listen/address/close/drain behavior, request body
streaming, response headers/body writes, and the EventEmitter lifecycle. `https` uses TLS transport
with the corresponding credential options.

SharpTS additionally provides `ServerResponse.probeConnection()` for long-running responses. It
commits a chunked response and writes a JSON-safe whitespace byte; use it only when changing the
response in that way is acceptable.

## net

`net` exports `createServer`, `createConnection`/`connect`, `Server`, `Socket`, `BlockList`,
`SocketAddress`, `isIP`, `isIPv4`, `isIPv6`, and default auto-family accessors. TCP and supported IPC
socket paths expose EventEmitter and stream behavior, backpressure, half-close options, connection
limits, address information, and block rules.

## tls

`tls` exports `createServer`, `connect`, `createSecureContext`, `Server`, `TLSSocket`,
`DEFAULT_MIN_VERSION`, and `DEFAULT_MAX_VERSION`. Maintained options include credentials, CA,
protocol bounds, ALPN, and SNI.

## dgram

`dgram` exports `createSocket` and `Socket` for UDP4/UDP6 bind, send, connect/disconnect, address,
broadcast, TTL, multicast membership/interface, buffer sizing, and EventEmitter lifecycle.
Source-specific multicast has platform/family limitations documented by raised errors.

## dns and dns/promises

`dns` includes `lookup`, `lookupService`, `resolve`, record-specific resolvers, `reverse`, server and
result-order configuration, and `Resolver`. `dns/promises` and `dns.promises` expose promise forms.

`lookup` uses the OS resolver (including hosts-file policy); `resolve*` uses DNS queries. Resolver
errors expose Node-style codes. Cancellation and callback timing have the compiled deviation noted
in status.

## child_process

`child_process` exports `execSync`, `spawnSync`, `execFileSync`, `exec`, `spawn`, `execFile`, and
`fork`. `ChildProcess` exposes PID/status, stdio, events, `kill`, and IPC `send`/`disconnect` when
forked. Commands, shell syntax, environment, signals, and executable lookup follow the host OS.

Compiled `fork` requires the managed runtime and deployed TypeScript entry source.

## cluster

`cluster` exposes primary/worker identity, `fork`, `workers`, `worker`, `settings`,
`setupPrimary`/`setupMaster`, scheduling policy, disconnect, worker process/IPC methods, and cluster
events. SharpTS workers use an in-process thread model, so worker process identity and resource
isolation are not identical to Node processes.

## worker_threads

`worker_threads` exports `Worker`, `MessageChannel`, `MessagePort`, `BroadcastChannel`,
`isMainThread`, `parentPort`, `workerData`, `threadId`, environment-data helpers, transfer helpers,
and synchronous port receive. Workers share the parent's console; Node `resourceLimits` and
per-worker stdio options do not have equivalent isolation here.

## vm

`vm` exports context creation/testing, `runInContext`, `runInNewContext`, `runInThisContext`,
`compileFunction`, `measureMemory`, `Script`, `SourceTextModule`, `SyntheticModule`, and maintained
constants. Contexts are language environments, not security sandboxes.

## url

`url` exports WHATWG `URL` and `URLSearchParams` plus `fileURLToPath`, `pathToFileURL`, `format`,
and legacy `parse`.

## querystring

`querystring` exports `parse`, `stringify`, `escape`, and `unescape`.

## util

`util` includes `promisify`, `callbackify`, `deprecate`, `format`, `inspect`, the `types` predicate
object, `TextEncoder`, and `TextDecoder` where declared.

## zlib

`zlib` provides gzip, deflate, raw deflate, Brotli, Zstandard, and unzip one-shot sync/callback APIs
plus the corresponding transform factories, `crc32`, and constants/codes. Available algorithms
are constrained by the host .NET compression libraries; compiled Zstandard use retains its
`ZstdSharp` runtime dependency.

## string_decoder

`string_decoder` exports `StringDecoder`. `write` preserves incomplete multi-byte sequences across
chunks and `end` flushes the final sequence.

## tty

`tty.isatty(fd)` tests a file descriptor. Dedicated `ReadStream`/`WriteStream` constructors are not
part of the maintained surface; use `process.stdin`, `process.stdout`, and `process.stderr`.

## Error and compatibility notes

Host I/O failures carry Node-style error codes where the public declarations expose them. Stream
objects support only the methods declared for their concrete shape; do not assume every Node stream
internal is present. For a current module-level capability summary and documented backend/platform
ceilings, see [STATUS.md](../STATUS.md#4-nodejs-built-in-modules).
