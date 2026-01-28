# SharpTS Node.js Module Support Status

This document tracks Node.js module and API implementation status in SharpTS.

**Last Updated:** 2026-01-27 (Expanded util module with types helpers, deprecate, callbackify, inherits, TextEncoder/TextDecoder)

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
| `crypto` | ⚠️ | Hash, HMAC, Cipher, PBKDF2, scrypt, HKDF, RSA encrypt/decrypt, Sign/Verify, DH/ECDH, KeyPair, KeyObject |
| `url` | ✅ | WHATWG URL + legacy parse/format/resolve |
| `querystring` | ✅ | parse, stringify, escape, unescape |
| `assert` | ✅ | Full testing utilities |
| `child_process` | ⚠️ | Synchronous only (`execSync`, `spawnSync`) |
| `util` | ⚠️ | format, inspect, types helpers, deprecate, callbackify, inherits, TextEncoder/TextDecoder |
| `readline` | ⚠️ | Basic synchronous I/O |
| `events` | ✅ | EventEmitter with on/off/once/emit/removeListener |
| `stream` | ❌ | No Readable/Writable/Transform |
| `buffer` | ✅ | Full Buffer class with multi-byte LE/BE, float/double, BigInt, search, swap |
| `http` / `https` | ❌ | No network server/client |
| `net` | ❌ | No TCP/IPC sockets |
| `dns` | ❌ | No DNS resolution |
| `zlib` | ✅ | gzip, deflate, deflateRaw, brotli, zstd (sync APIs) |
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
| `truncateSync` | ✅ | Truncate file to specified length |
| **Directory Operations** | | |
| `mkdirSync` | ✅ | Supports `recursive` option |
| `rmdirSync` | ✅ | |
| `readdirSync` | ✅ | Supports `recursive` and `withFileTypes` options |
| `mkdtempSync` | ✅ | Create unique temp directory |
| `opendirSync` | ✅ | Returns Dir object with readSync/closeSync |
| **File Info** | | |
| `statSync` | ✅ | Returns Stats object |
| `lstatSync` | ✅ | Stats without following symlinks |
| `accessSync` | ✅ | Check file access permissions |
| `realpathSync` | ✅ | Resolve canonical path |
| **File Descriptor APIs** | | |
| `openSync` | ✅ | Open file, returns fd (flags: r, w, a, r+, w+, a+, etc.) |
| `closeSync` | ✅ | Close file descriptor |
| `readSync` | ✅ | Read into Buffer at offset/position |
| `writeSync` | ✅ | Write Buffer or string to fd |
| `fstatSync` | ✅ | Stats for open file descriptor |
| `ftruncateSync` | ✅ | Truncate open file descriptor |
| **Links** | | |
| `linkSync` | ✅ | Create hard link (cross-platform) |
| `symlinkSync` | ✅ | Create symbolic link |
| `readlinkSync` | ✅ | Read symbolic link target |
| **Permissions** | | |
| `chmodSync` | ✅ | Change file mode/permissions |
| `chownSync` | ✅ | Change file owner (Unix only) |
| `lchownSync` | ✅ | Change symlink owner (Unix only) |
| `utimesSync` | ✅ | Update file access/modification times |
| **Async APIs** | | |
| `readFile` | ❌ | Use `readFileSync` |
| `writeFile` | ❌ | Use `writeFileSync` |
| `fs/promises` | ❌ | Use sync versions |
| **Advanced** | | |
| `createReadStream` | ❌ | No stream support |
| `createWriteStream` | ❌ | No stream support |
| `watch` / `watchFile` | ❌ | |
| **Error Codes** | ✅ | ENOENT, EACCES, EEXIST, EISDIR, ENOTDIR, ENOTEMPTY, EBADF, EXDEV, etc. |

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
| `posix` | ✅ | POSIX-style path methods (always uses `/`) |
| `win32` | ✅ | Windows-style path methods (always uses `\`) |

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
| `networkInterfaces` | ✅ | Returns network interface information |
| `loadavg` | ✅ | Returns [0, 0, 0] on Windows (Node.js behavior) |

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
| **Hashing** | | |
| `createHash` | ✅ | md5, sha1, sha256, sha384, sha512 |
| `createHmac` | ✅ | md5, sha1, sha256, sha384, sha512 with string/Buffer keys |
| **Random** | | |
| `randomBytes` | ✅ | |
| `randomUUID` | ✅ | |
| `randomInt` | ✅ | |
| **Cipher** | | |
| `createCipheriv` | ✅ | AES-128/192/256-CBC and AES-128/192/256-GCM |
| `createDecipheriv` | ✅ | AES-128/192/256-CBC and AES-128/192/256-GCM |
| `createCipher` / `createDecipher` | ❌ | Deprecated in Node.js, use iv variants |
| **Key Derivation** | | |
| `pbkdf2Sync` | ✅ | sha1, sha256, sha384, sha512 (not md5) |
| `scryptSync` | ✅ | With N/cost, r/blockSize, p/parallelization options |
| `pbkdf2` / `scrypt` | ❌ | Async versions - use sync versions |
| **Comparison** | | |
| `timingSafeEqual` | ✅ | Constant-time buffer comparison (prevents timing attacks) |
| **Signing** | | |
| `createSign` / `createVerify` | ✅ | RSA and EC keys; SHA1/256/384/512; hex/base64/Buffer output |
| **Key Generation** | | |
| `generateKeyPairSync` | ✅ | RSA (2048/4096) and EC (P-256/P-384/P-521); PEM format |
| `generateKeyPair` | ❌ | Async version - use sync version |
| **Diffie-Hellman** | | |
| `createDiffieHellman` | ✅ | With prime length or explicit prime/generator |
| `getDiffieHellman` | ✅ | Predefined groups: modp1, modp2, modp5, modp14-18 |
| `createECDH` | ✅ | P-256 (prime256v1), P-384 (secp384r1), P-521 (secp521r1) |
| **Discovery** | | |
| `getHashes` | ✅ | Returns array of supported hash algorithms |
| `getCiphers` | ✅ | Returns array of supported cipher algorithms |
| **RSA Encryption** | | |
| `publicEncrypt` | ✅ | RSA-OAEP encryption (SHA-1 default) |
| `privateDecrypt` | ✅ | RSA-OAEP decryption |
| `privateEncrypt` | ✅ | RSA PKCS#1 v1.5 signing primitive |
| `publicDecrypt` | ✅ | RSA PKCS#1 v1.5 verification primitive |
| **HKDF** | | |
| `hkdfSync` | ✅ | HKDF key derivation (RFC 5869); sha256, sha384, sha512 |
| `hkdf` | ❌ | Async version - use sync version |
| **KeyObject** | | |
| `createSecretKey` | ✅ | Create symmetric KeyObject from Buffer |
| `createPublicKey` | ✅ | Create public KeyObject from PEM |
| `createPrivateKey` | ✅ | Create private KeyObject from PEM |
| `KeyObject.type` | ✅ | 'secret', 'public', or 'private' |
| `KeyObject.asymmetricKeyType` | ✅ | 'rsa' or 'ec' (undefined for secret) |
| `KeyObject.asymmetricKeyDetails` | ✅ | modulusLength/publicExponent for RSA, namedCurve for EC |
| `KeyObject.symmetricKeySize` | ✅ | Byte length (secret keys only) |
| `KeyObject.export()` | ✅ | Export to PEM string or Buffer |

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
| `Buffer` | ✅ | Full Buffer class available globally |
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
| `EventEmitter` class | ✅ | Full implementation |
| `on()` / `addListener()` | ✅ | |
| `once()` | ✅ | |
| `emit()` | ✅ | |
| `removeListener()` / `off()` | ✅ | |
| `removeAllListeners()` | ✅ | |
| `listenerCount()` | ✅ | |
| `listeners()` | ✅ | |
| `eventNames()` | ✅ | |
| `prependListener()` | ✅ | |
| `prependOnceListener()` | ✅ | |
| `defaultMaxListeners` | ✅ | Static property |

---

## 13. ZLIB (Compression)

| Feature | Status | Notes |
|---------|--------|-------|
| **Gzip** | | |
| `gzipSync` | ✅ | Compress using gzip |
| `gunzipSync` | ✅ | Decompress gzip data |
| **Deflate** | | |
| `deflateSync` | ✅ | Compress with zlib header |
| `inflateSync` | ✅ | Decompress zlib data |
| `deflateRawSync` | ✅ | Compress without header |
| `inflateRawSync` | ✅ | Decompress raw deflate |
| **Brotli** | | |
| `brotliCompressSync` | ✅ | Brotli compression |
| `brotliDecompressSync` | ✅ | Brotli decompression |
| **Zstd** | | |
| `zstdCompressSync` | ✅ | Zstandard compression |
| `zstdDecompressSync` | ✅ | Zstandard decompression |
| **Utilities** | | |
| `unzipSync` | ✅ | Auto-detect and decompress |
| `constants` | ✅ | Compression constants object |
| **Options** | | |
| `level` | ✅ | Compression level (0-9) |
| `chunkSize` | ✅ | Buffer size for streaming |
| `maxOutputLength` | ✅ | Maximum output size limit |
| `windowBits` | ⚠️ | Not directly supported in .NET |
| `memLevel` | ⚠️ | Not directly supported in .NET |
| `strategy` | ⚠️ | Not directly supported in .NET |
| **Async APIs** | | |
| `gzip` / `gunzip` | ❌ | Use sync versions |
| `deflate` / `inflate` | ❌ | Use sync versions |
| `brotliCompress` / `brotliDecompress` | ❌ | Use sync versions |
| **Streaming APIs** | | |
| `createGzip` / `createGunzip` | ❌ | No stream support |
| `createDeflate` / `createInflate` | ❌ | No stream support |

---

## 14. BUFFER

| Feature | Status | Notes |
|---------|--------|-------|
| **Static Methods** | | |
| `Buffer.from()` | ✅ | From string, array, or Buffer |
| `Buffer.alloc()` | ✅ | Zero-filled allocation |
| `Buffer.allocUnsafe()` | ✅ | Uninitialized allocation |
| `Buffer.concat()` | ✅ | Concatenate multiple buffers |
| `Buffer.isBuffer()` | ✅ | Type check |
| `Buffer.byteLength()` | ✅ | String byte length |
| `Buffer.compare()` | ✅ | Static comparison |
| `Buffer.isEncoding()` | ✅ | Encoding validation |
| **Instance Properties** | | |
| `length` | ✅ | Buffer byte length |
| **Instance Methods** | | |
| `toString()` | ✅ | With encoding support |
| `slice()` | ✅ | Create view/copy |
| `copy()` | ✅ | Copy to target buffer |
| `compare()` | ✅ | Compare with other buffer |
| `equals()` | ✅ | Equality check |
| `fill()` | ✅ | Fill with value/string |
| `write()` | ✅ | Write string at offset |
| `readUInt8()` | ✅ | Read unsigned byte |
| `writeUInt8()` | ✅ | Write unsigned byte |
| `toJSON()` | ✅ | Serialize to {type, data} |
| **Multi-byte Reads** | | |
| `readUInt16LE/BE()` | ✅ | Unsigned 16-bit |
| `readUInt32LE/BE()` | ✅ | Unsigned 32-bit |
| `readInt8()` | ✅ | Signed 8-bit |
| `readInt16LE/BE()` | ✅ | Signed 16-bit |
| `readInt32LE/BE()` | ✅ | Signed 32-bit |
| `readBigInt64LE/BE()` | ✅ | Signed 64-bit BigInt |
| `readBigUInt64LE/BE()` | ✅ | Unsigned 64-bit BigInt |
| `readFloatLE/BE()` | ✅ | 32-bit float |
| `readDoubleLE/BE()` | ✅ | 64-bit double |
| **Multi-byte Writes** | | |
| `writeUInt16LE/BE()` | ✅ | Unsigned 16-bit |
| `writeUInt32LE/BE()` | ✅ | Unsigned 32-bit |
| `writeInt8()` | ✅ | Signed 8-bit |
| `writeInt16LE/BE()` | ✅ | Signed 16-bit |
| `writeInt32LE/BE()` | ✅ | Signed 32-bit |
| `writeBigInt64LE/BE()` | ✅ | Signed 64-bit BigInt |
| `writeBigUInt64LE/BE()` | ✅ | Unsigned 64-bit BigInt |
| `writeFloatLE/BE()` | ✅ | 32-bit float |
| `writeDoubleLE/BE()` | ✅ | 64-bit double |
| **Search & Swap** | | |
| `indexOf()` | ✅ | Find first occurrence |
| `includes()` | ✅ | Check if value exists |
| `swap16/32/64()` | ✅ | Byte order swapping |

---

## Summary

SharpTS provides comprehensive support for file system operations (sync), including file descriptor APIs, directory utilities, hard/symbolic links, and permissions. Also includes path manipulation, OS information, process management, basic crypto, URL parsing, binary data handling via Buffer, and EventEmitter for event-driven patterns. The module system supports both ES modules and CommonJS import syntax.

**Key Gaps:**
- No Stream classes (limits file/network streaming)
- No async fs operations (sync-only workaround)
- No network modules (http, net, dns)

**Recommended Workarounds:**
- Use `*Sync` versions of fs methods
- Use ES module syntax instead of `require()`

---

## Recommended Next Steps

Priority features to implement for broader Node.js compatibility:

1. **Async fs APIs** - `fs.promises` or callback-based (higher effort)
2. **Streams API** - Needed for large file handling (higher effort)
3. **http module** - Basic HTTP server/client (higher effort)
