# SharpTS Node.js Module Support Status

This document tracks Node.js module and API implementation status in SharpTS.

**Last Updated:** 2026-01-24 (Added `__dirname`, `__filename`, and `crypto.createHmac` support)

## Legend
- ✅ Implemented
- ⚠️ Partially Implemented
- ❌ Not Implemented

---

## 1. CORE NODE.JS MODULES

| Module | Status | Notes |
|--------|--------|-------|
| `fs` | ⚠️ | Synchronous APIs only (`*Sync` methods) |
| `path` | ✅ | Full API |
| `os` | ✅ | Full API |
| `process` | ✅ | Properties + methods, available as module and global |
| `crypto` | ⚠️ | Hash + HMAC + random; no cipher/sign/verify |
| `url` | ✅ | WHATWG URL + legacy parse/format/resolve |
| `querystring` | ✅ | parse, stringify, escape, unescape |
| `assert` | ✅ | Full testing utilities |
| `child_process` | ⚠️ | Synchronous only (`execSync`, `spawnSync`) |
| `util` | ⚠️ | format, inspect, types helpers |
| `readline` | ⚠️ | Basic synchronous I/O |
| `events` | ❌ | EventEmitter not implemented |
| `stream` | ❌ | No Readable/Writable/Transform |
| `buffer` | ❌ | No dedicated Buffer class |
| `http` / `https` | ❌ | No network server/client |
| `net` | ❌ | No TCP/IPC sockets |
| `dns` | ❌ | No DNS resolution |
| `zlib` | ❌ | No compression |
| `worker_threads` | ❌ | No worker support |
| `cluster` | ❌ | No cluster support |

---

## 2. FILE SYSTEM (`fs`)

| Feature | Status | Notes |
|---------|--------|-------|
| **File Operations** | | |
| `existsSync` | ✅ | |
| `readFileSync` | ✅ | Supports encoding option |
| `writeFileSync` | ✅ | |
| `appendFileSync` | ✅ | |
| `copyFileSync` | ✅ | |
| `renameSync` | ✅ | |
| `unlinkSync` | ✅ | |
| **Directory Operations** | | |
| `mkdirSync` | ✅ | Supports `recursive` option |
| `rmdirSync` | ✅ | |
| `readdirSync` | ✅ | |
| **File Info** | | |
| `statSync` | ✅ | Returns Stats object |
| `lstatSync` | ✅ | |
| `accessSync` | ✅ | |
| **Async APIs** | | |
| `readFile` | ❌ | Use `readFileSync` |
| `writeFile` | ❌ | Use `writeFileSync` |
| `fs/promises` | ❌ | Use sync versions |
| **Advanced** | | |
| `createReadStream` | ❌ | No stream support |
| `createWriteStream` | ❌ | No stream support |
| `watch` / `watchFile` | ❌ | |
| `chmod` / `chown` | ❌ | |
| **Error Codes** | ✅ | ENOENT, EACCES, EEXIST, EISDIR, ENOTDIR, ENOTEMPTY, etc. |

---

## 3. PATH (`path`)

| Feature | Status | Notes |
|---------|--------|-------|
| `join` | ✅ | |
| `resolve` | ✅ | |
| `basename` | ✅ | |
| `dirname` | ✅ | |
| `extname` | ✅ | |
| `normalize` | ✅ | |
| `isAbsolute` | ✅ | |
| `relative` | ✅ | |
| `parse` | ✅ | Returns { root, dir, base, ext, name } |
| `format` | ✅ | |
| `sep` | ✅ | Platform path separator |
| `delimiter` | ✅ | Platform path list delimiter |
| `posix` / `win32` | ❌ | No platform-specific variants |

---

## 4. OS (`os`)

| Feature | Status | Notes |
|---------|--------|-------|
| `platform` | ✅ | |
| `arch` | ✅ | |
| `hostname` | ✅ | |
| `homedir` | ✅ | |
| `tmpdir` | ✅ | |
| `type` | ✅ | |
| `release` | ✅ | |
| `cpus` | ✅ | Returns CPU info array |
| `totalmem` | ✅ | |
| `freemem` | ✅ | |
| `userInfo` | ✅ | |
| `EOL` | ✅ | Platform line ending |
| `networkInterfaces` | ❌ | |
| `loadavg` | ❌ | |

---

## 5. PROCESS

| Feature | Status | Notes |
|---------|--------|-------|
| **Properties** | | |
| `platform` | ✅ | |
| `arch` | ✅ | |
| `pid` | ✅ | |
| `version` | ✅ | |
| `env` | ✅ | Environment variables |
| `argv` | ✅ | Command-line arguments |
| `exitCode` | ✅ | |
| `stdin` | ✅ | Basic input support |
| `stdout` | ✅ | write() method |
| `stderr` | ✅ | write() method |
| **Methods** | | |
| `cwd` | ✅ | |
| `chdir` | ✅ | |
| `exit` | ✅ | |
| `hrtime` | ✅ | High-resolution time |
| `uptime` | ✅ | |
| `memoryUsage` | ✅ | |
| **Events** | | |
| `on('exit')` | ❌ | No EventEmitter |
| `on('uncaughtException')` | ❌ | No EventEmitter |

---

## 6. CRYPTO

| Feature | Status | Notes |
|---------|--------|-------|
| `createHash` | ✅ | md5, sha1, sha256, sha384, sha512 |
| `createHmac` | ✅ | md5, sha1, sha256, sha384, sha512 with string/Buffer keys |
| `randomBytes` | ✅ | |
| `randomUUID` | ✅ | |
| `randomInt` | ✅ | |
| `createCipher` / `createDecipher` | ❌ | |
| `createSign` / `createVerify` | ❌ | |
| `pbkdf2` / `scrypt` | ❌ | |
| `generateKeyPair` | ❌ | |

---

## 7. URL

| Feature | Status | Notes |
|---------|--------|-------|
| **WHATWG URL API** | | |
| `URL` class | ✅ | Full property access |
| `URLSearchParams` | ✅ | get, set, has, append, delete, keys, values, size |
| **Legacy API** | | |
| `parse` | ✅ | |
| `format` | ✅ | |
| `resolve` | ✅ | |

---

## 8. CHILD PROCESS

| Feature | Status | Notes |
|---------|--------|-------|
| `execSync` | ✅ | With cwd, timeout, env, shell options |
| `spawnSync` | ✅ | With cwd, timeout, env options |
| `exec` | ❌ | No async support |
| `spawn` | ❌ | No async support |
| `fork` | ❌ | |
| Process events | ❌ | No EventEmitter |

---

## 9. MODULE SYSTEM

| Feature | Status | Notes |
|---------|--------|-------|
| **ES Modules** | | |
| `import { x } from './file'` | ✅ | Named imports |
| `import x from './file'` | ✅ | Default imports |
| `import * as x from './file'` | ✅ | Namespace imports |
| `export { x }` | ✅ | Named exports |
| `export default x` | ✅ | Default exports |
| `export * from './file'` | ✅ | Re-exports |
| `import type { T }` | ✅ | Type-only imports |
| `import('./file')` | ✅ | Dynamic imports |
| `import.meta.url` | ✅ | Module URL (file:// format) |
| `import.meta.dirname` | ✅ | Directory of current module |
| `import.meta.filename` | ✅ | Full path of current module |
| **CommonJS Interop** | | |
| `import x = require('path')` | ✅ | CommonJS import syntax |
| `export =` | ✅ | CommonJS export syntax |
| `require()` function | ❌ | Not as global function |
| `module.exports` | ❌ | Not manipulable |
| `exports` shorthand | ❌ | |
| **Resolution** | | |
| Relative paths | ✅ | `./foo`, `../bar` |
| Bare specifiers | ✅ | `node_modules` lookup |
| Directory index | ✅ | Looks for `index.ts` |
| Extension inference | ✅ | Adds `.ts` automatically |
| Circular detection | ✅ | With error reporting |
| `/// <reference>` | ✅ | Triple-slash references |
| `package.json` exports | ❌ | |
| Conditional exports | ❌ | |

---

## 10. GLOBALS

| Feature | Status | Notes |
|---------|--------|-------|
| `globalThis` | ✅ | ES2020 global reference |
| `process` | ✅ | Available globally |
| `console` | ✅ | `console.log` and variants |
| `setTimeout` / `clearTimeout` | ✅ | |
| `setInterval` / `clearInterval` | ✅ | |
| `__dirname` | ✅ | Directory of current module |
| `__filename` | ✅ | Full path of current module |
| `require` | ❌ | Use `import` syntax |
| `module` | ❌ | |
| `exports` | ❌ | |
| `Buffer` | ❌ | Use arrays or typed arrays |
| `global` | ⚠️ | Use `globalThis` |

---

## 11. STREAMS

| Feature | Status | Notes |
|---------|--------|-------|
| `Readable` class | ❌ | |
| `Writable` class | ❌ | |
| `Transform` class | ❌ | |
| `Duplex` class | ❌ | |
| `pipe()` method | ❌ | |
| `process.stdout.write()` | ✅ | Basic only |
| `process.stderr.write()` | ✅ | Basic only |
| `process.stdin` events | ❌ | No event-based input |

---

## 12. EVENTS

| Feature | Status | Notes |
|---------|--------|-------|
| `EventEmitter` class | ❌ | |
| `on()` / `addListener()` | ❌ | |
| `once()` | ❌ | |
| `emit()` | ❌ | |
| `removeListener()` | ❌ | |
| `removeAllListeners()` | ❌ | |

---

## 13. BUFFER

| Feature | Status | Notes |
|---------|--------|-------|
| `Buffer.from()` | ❌ | Use arrays |
| `Buffer.alloc()` | ❌ | |
| `Buffer.allocUnsafe()` | ❌ | |
| `Buffer.concat()` | ❌ | |
| `Buffer.isBuffer()` | ❌ | |
| Buffer instance methods | ❌ | |

---

## Summary

SharpTS provides solid support for file system operations (sync), path manipulation, OS information, process management, basic crypto, and URL parsing. The module system supports both ES modules and CommonJS import syntax.

**Key Gaps:**
- No EventEmitter pattern (blocks event-based APIs)
- No Stream classes (limits file/network streaming)
- No async fs operations (sync-only workaround)
- No Buffer class (arrays used as workaround)
- No network modules (http, net, dns)

**Recommended Workarounds:**
- Use `*Sync` versions of fs methods
- Use arrays instead of Buffer for byte data
- Use ES module syntax instead of `require()`

---

## Recommended Next Steps

Priority features to implement for broader Node.js compatibility:

1. **Buffer class** - Essential for binary data handling (`Buffer.from`, `Buffer.alloc`, `Buffer.toString`, `Buffer.concat`)
2. **EventEmitter** - Foundation for many Node APIs (medium effort)
3. **Async fs APIs** - `fs.promises` or callback-based (higher effort)
4. **Streams API** - Needed for large file handling (higher effort)
5. **http module** - Basic HTTP server/client (higher effort)
