# Node.js Built-in Modules API Guide

SharpTS provides implementations of common Node.js built-in modules. This guide documents the supported APIs for TypeScript developers familiar with Node.js.

## Import Syntax

All three import styles are supported:

```typescript
// Default import (recommended for most modules)
import fs from 'fs';
import os from 'os';

// Named imports (for specific functions)
import { readFileSync, writeFileSync } from 'fs';
import { createHash, randomUUID } from 'crypto';

// Namespace import
import * as path from 'path';

// Mixed imports
import path, { join, resolve } from 'path';
```

---

## assert

Assertion testing utilities for validating code behavior.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `ok` | `ok(value, message?)` | Assert value is truthy |
| `strictEqual` | `strictEqual(actual, expected, message?)` | Assert strict equality (`===`) |
| `notStrictEqual` | `notStrictEqual(actual, expected, message?)` | Assert strict inequality (`!==`) |
| `equal` | `equal(actual, expected, message?)` | Assert loose equality (`==`) |
| `notEqual` | `notEqual(actual, expected, message?)` | Assert loose inequality (`!=`) |
| `deepStrictEqual` | `deepStrictEqual(actual, expected, message?)` | Assert deep strict equality |
| `notDeepStrictEqual` | `notDeepStrictEqual(actual, expected, message?)` | Assert deep inequality |
| `throws` | `throws(fn, message?)` | Assert function throws |
| `doesNotThrow` | `doesNotThrow(fn, message?)` | Assert function doesn't throw |
| `fail` | `fail(message?)` | Always throws assertion error |

### Example

```typescript
import { strictEqual, deepStrictEqual, throws } from 'assert';

strictEqual(1 + 1, 2);
strictEqual('hello'.length, 5);

deepStrictEqual({ a: 1, b: 2 }, { a: 1, b: 2 });

throws(() => {
  throw new Error('expected error');
});
```

### AssertionError

All assertions throw `AssertionError` on failure with properties:
- `message` - Error message
- `actual` - Actual value
- `expected` - Expected value
- `operator` - Assertion operator name

---

## child_process

Execute external processes and shell commands.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `execSync` | `execSync(command, options?)` | Execute shell command synchronously |
| `spawnSync` | `spawnSync(command, args?, options?)` | Spawn process synchronously |
| `execFileSync` | `execFileSync(file, args?, options?)` | Execute file synchronously without shell |
| `exec` | `exec(command, options?, callback?)` | Execute shell command asynchronously |
| `execFile` | `execFile(file, args?, options?, callback?)` | Execute file asynchronously without shell |
| `spawn` | `spawn(command, args?, options?)` | Spawn a child process, returns `ChildProcess` |
| `fork` | `fork(modulePath, args?, options?)` | Spawn a Node child with IPC channel |

### execSync Options

```typescript
{
  cwd?: string,      // Working directory
  timeout?: number,  // Timeout in milliseconds
  env?: object       // Environment variables
}
```

### spawnSync Options

```typescript
{
  cwd?: string,    // Working directory
  shell?: boolean, // Run in shell
  env?: object     // Environment variables
}
```

### spawnSync Return Value

```typescript
{
  stdout: string,      // Standard output
  stderr: string,      // Standard error
  status: number|null, // Exit code (null on success)
  signal: string|null, // Signal if killed
  error: string|null   // Error message if failed
}
```

### Example

```typescript
import { execSync, spawnSync } from 'child_process';

// Execute shell command
const output = execSync('echo hello');
console.log(output); // "hello"

// Execute with options
const result = execSync('ls -la', { cwd: '/tmp' });

// Spawn process with arguments
const spawn = spawnSync('git', ['status'], { cwd: '/my/repo' });
console.log(spawn.stdout);
```

---

## crypto

Cryptographic functions for hashing and random number generation.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `createHash` | `createHash(algorithm)` | Create a Hash object |
| `randomBytes` | `randomBytes(size)` | Generate secure random bytes |
| `randomUUID` | `randomUUID()` | Generate random UUID v4 |
| `randomInt` | `randomInt(max)` or `randomInt(min, max)` | Generate random integer |

### Supported Hash Algorithms

- `md5`
- `sha1`
- `sha256`
- `sha384`
- `sha512`

### Hash Object Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `update` | `update(data)` | Add data to hash (chainable) |
| `digest` | `digest(encoding?)` | Finalize and return digest |

Digest encodings: `'hex'`, `'base64'`, or omit for raw bytes.

### Example

```typescript
import { createHash, randomBytes, randomUUID, randomInt } from 'crypto';

// Create SHA-256 hash
const hash = createHash('sha256')
  .update('hello')
  .update('world')
  .digest('hex');
console.log(hash); // "936a185caaa266bb9cbe981e9e05cb78cd732b0b3280eb944412bb6f8f8f07af"

// Generate random bytes
const bytes = randomBytes(16);

// Generate UUID
const uuid = randomUUID();
console.log(uuid); // "550e8400-e29b-41d4-a716-446655440000"

// Random integers
const n = randomInt(100);        // 0-99
const m = randomInt(10, 20);     // 10-19
```

---

## fs

File system operations. Synchronous, callback-style async, and promise-based APIs are all supported. For promise-based APIs see the `fs/promises` section below.

### File Operations (sync)

| Method | Signature | Description |
|--------|-----------|-------------|
| `existsSync` | `existsSync(path)` | Check if path exists |
| `readFileSync` | `readFileSync(path, encoding?)` | Read file contents |
| `writeFileSync` | `writeFileSync(path, data, encoding?)` | Write to file |
| `appendFileSync` | `appendFileSync(path, data, encoding?)` | Append to file |
| `copyFileSync` | `copyFileSync(src, dest)` | Copy file |
| `renameSync` | `renameSync(oldPath, newPath)` | Rename/move file |
| `unlinkSync` | `unlinkSync(path)` | Delete file |
| `truncateSync` | `truncateSync(path, len?)` | Truncate file to length |
| `openSync` | `openSync(path, flags, mode?)` | Open file, return fd |
| `closeSync` | `closeSync(fd)` | Close file descriptor |
| `readSync` | `readSync(fd, buffer, offset, length, position?)` | Read from fd |
| `writeSync` | `writeSync(fd, buffer, ...)` | Write to fd |
| `ftruncateSync` | `ftruncateSync(fd, len?)` | Truncate via fd |
| `fstatSync` | `fstatSync(fd)` | Stat via fd |

### Directory Operations (sync)

| Method | Signature | Description |
|--------|-----------|-------------|
| `mkdirSync` | `mkdirSync(path, options?)` | Create directory |
| `mkdtempSync` | `mkdtempSync(prefix)` | Create unique temp directory |
| `rmdirSync` | `rmdirSync(path, options?)` | Remove directory |
| `readdirSync` | `readdirSync(path, options?)` | List directory contents |
| `opendirSync` | `opendirSync(path)` | Open directory handle |

### File Information / Permissions (sync)

| Method | Signature | Description |
|--------|-----------|-------------|
| `statSync` | `statSync(path)` | Get file/directory stats |
| `lstatSync` | `lstatSync(path)` | Get stats (symlink-aware) |
| `accessSync` | `accessSync(path, mode?)` | Check file accessibility |
| `realpathSync` | `realpathSync(path)` | Resolve canonical path |
| `readlinkSync` | `readlinkSync(path)` | Read symlink target |
| `symlinkSync` | `symlinkSync(target, path, type?)` | Create symlink |
| `linkSync` | `linkSync(existing, new)` | Create hard link |
| `chmodSync` | `chmodSync(path, mode)` | Change permissions |
| `chownSync` | `chownSync(path, uid, gid)` | Change ownership |
| `lchownSync` | `lchownSync(path, uid, gid)` | Change ownership (symlink-aware) |
| `utimesSync` | `utimesSync(path, atime, mtime)` | Update access/modification times |

### Callback-style Async

Most file/directory sync methods have callback-style counterparts with the trailing `(err, result) => …` pattern: `readFile`, `writeFile`, `appendFile`, `copyFile`, `rename`, `unlink`, `mkdir`, `readdir`, `stat`, `lstat`, `access`, `chmod`, `chown`. Use `fs/promises` for `Promise`-returning equivalents.

### Stat Object Properties

```typescript
{
  isDirectory: boolean,
  isFile: boolean,
  size: number
}
```

### rmdirSync Options

```typescript
{
  recursive?: boolean  // Remove directory and contents
}
```

### Example

```typescript
import fs from 'fs';

// Read and write files
const content = fs.readFileSync('input.txt', 'utf8');
fs.writeFileSync('output.txt', content.toUpperCase());

// Check existence
if (fs.existsSync('config.json')) {
  const config = fs.readFileSync('config.json', 'utf8');
}

// Directory operations
fs.mkdirSync('new-folder');
const files = fs.readdirSync('.');
console.log(files);

// File stats
const stats = fs.statSync('myfile.txt');
if (stats.isFile) {
  console.log(`Size: ${stats.size} bytes`);
}

// Remove directory recursively
fs.rmdirSync('old-folder', { recursive: true });
```

### Error Codes

Node.js-compatible error codes are thrown:
- `ENOENT` - File/directory not found
- `EACCES` - Permission denied
- `EEXIST` - File already exists
- `EISDIR` - Is a directory (expected file)
- `ENOTDIR` - Not a directory
- `ENOTEMPTY` - Directory not empty

---

## os

Operating system information and utilities.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `platform` | `platform()` | Get OS platform |
| `arch` | `arch()` | Get CPU architecture |
| `hostname` | `hostname()` | Get machine hostname |
| `homedir` | `homedir()` | Get user home directory |
| `tmpdir` | `tmpdir()` | Get temp directory path |
| `type` | `type()` | Get OS type |
| `release` | `release()` | Get OS release version |
| `cpus` | `cpus()` | Get CPU information |
| `totalmem` | `totalmem()` | Get total system memory |
| `freemem` | `freemem()` | Get free system memory |
| `userInfo` | `userInfo()` | Get current user info |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `EOL` | `string` | End-of-line character |

### Platform Values

- `'win32'` - Windows
- `'linux'` - Linux
- `'darwin'` - macOS

### Architecture Values

- `'x64'` - 64-bit x86
- `'ia32'` - 32-bit x86
- `'arm64'` - 64-bit ARM
- `'arm'` - 32-bit ARM

### cpus() Return Value

```typescript
[
  { model: string, speed: number },
  // ...
]
```

### userInfo() Return Value

```typescript
{
  username: string,
  uid: number,
  gid: number,
  shell: string,
  homedir: string
}
```

### Example

```typescript
import os from 'os';

console.log(`Platform: ${os.platform()}`);  // "win32", "linux", "darwin"
console.log(`Architecture: ${os.arch()}`);  // "x64"
console.log(`Hostname: ${os.hostname()}`);
console.log(`Home: ${os.homedir()}`);
console.log(`Temp: ${os.tmpdir()}`);

// Memory info
const totalGB = os.totalmem() / (1024 * 1024 * 1024);
const freeGB = os.freemem() / (1024 * 1024 * 1024);
console.log(`Memory: ${freeGB.toFixed(1)}GB free of ${totalGB.toFixed(1)}GB`);

// CPU info
const cpus = os.cpus();
console.log(`CPUs: ${cpus.length} cores`);
```

---

## path

File path manipulation utilities.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `join` | `join(...parts)` | Join path segments |
| `resolve` | `resolve(...parts)` | Resolve to absolute path |
| `basename` | `basename(path, ext?)` | Get filename |
| `dirname` | `dirname(path)` | Get directory name |
| `extname` | `extname(path)` | Get file extension |
| `normalize` | `normalize(path)` | Normalize path |
| `isAbsolute` | `isAbsolute(path)` | Check if path is absolute |
| `relative` | `relative(from, to)` | Get relative path |
| `parse` | `parse(path)` | Parse path to components |
| `format` | `format(pathObj)` | Build path from components |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `sep` | `string` | Path separator (`/` or `\\`) |
| `delimiter` | `string` | Path list delimiter (`:` or `;`) |

### parse() Return Value

```typescript
{
  root: string,  // "/" or "C:\\"
  dir: string,   // Directory path
  base: string,  // Filename with extension
  name: string,  // Filename without extension
  ext: string    // Extension including dot
}
```

### Example

```typescript
import path from 'path';

// Join paths
const fullPath = path.join('/users', 'john', 'documents', 'file.txt');
// "/users/john/documents/file.txt"

// Resolve to absolute
const absolute = path.resolve('./src', '../lib', 'utils.ts');

// Extract parts
console.log(path.dirname('/a/b/c.txt'));   // "/a/b"
console.log(path.basename('/a/b/c.txt'));  // "c.txt"
console.log(path.extname('/a/b/c.txt'));   // ".txt"

// Remove extension
console.log(path.basename('file.ts', '.ts')); // "file"

// Parse path
const parsed = path.parse('/home/user/file.txt');
// { root: "/", dir: "/home/user", base: "file.txt", name: "file", ext: ".txt" }

// Build path
const built = path.format({ dir: '/home/user', base: 'file.txt' });
// "/home/user/file.txt"

// Relative path
console.log(path.relative('/a/b/c', '/a/d/e')); // "../../d/e"
```

---

## process

Process information and control.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `platform` | `string` | OS platform |
| `arch` | `string` | CPU architecture |
| `pid` | `number` | Process ID |
| `version` | `string` | Node.js version string |
| `env` | `object` | Environment variables |
| `argv` | `string[]` | Command-line arguments |
| `exitCode` | `number` | Current exit code |
| `stdin` | `Stream` | Standard input |
| `stdout` | `Stream` | Standard output |
| `stderr` | `Stream` | Standard error |

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `cwd` | `cwd()` | Get current working directory |
| `chdir` | `chdir(path)` | Change working directory |
| `exit` | `exit(code?)` | Exit process |
| `kill` | `kill(pid, signal?)` | Signal a process; signal `0` performs an existence check |
| `hrtime` | `hrtime(time?)` | High-resolution time |
| `uptime` | `uptime()` | Process uptime in seconds |
| `memoryUsage` | `memoryUsage()` | Memory usage statistics |

### Host process-control policy

An embedding host can set the process-wide AppContext switch
`SharpTS.RestrictProcessControl` to `true`. While enabled, `process.kill()` permits
self-signals (including signal `0`) but rejects every other PID with an `EPERM`
error before probing or signaling that process. Interpreted and compiled execution
honor the same switch.

This switch constrains the Node-compatible `process.kill()` facade; it is not a
standalone sandbox boundary. A host treating guest code as untrusted must also
restrict arbitrary `dotnet:` interop and other process-launch/control surfaces such
as `child_process`, because those APIs can bypass or mutate process-wide AppContext
policy.

### Example

```typescript
import process from 'process';

// Environment
console.log(`Platform: ${process.platform}`);
console.log(`PID: ${process.pid}`);
console.log(`CWD: ${process.cwd()}`);

// Environment variables
const home = process.env.HOME || process.env.USERPROFILE;
console.log(`Home: ${home}`);

// Command-line arguments
process.argv.forEach((arg, index) => {
  console.log(`argv[${index}]: ${arg}`);
});

// Change directory
process.chdir('/tmp');

// Timing
const start = process.hrtime();
// ... some operation ...
const elapsed = process.hrtime(start);
console.log(`Took ${elapsed[0]}s ${elapsed[1]}ns`);

// Memory
const mem = process.memoryUsage();
console.log(`Heap used: ${mem.heapUsed}`);
```

---

## querystring

URL query string parsing and serialization.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `parse` | `parse(str, sep?, eq?, options?)` | Parse query string to object |
| `stringify` | `stringify(obj, sep?, eq?, options?)` | Convert object to query string |
| `escape` | `escape(str)` | Percent-encode string |
| `unescape` | `unescape(str)` | Percent-decode string |
| `decode` | - | Alias for `parse` |
| `encode` | - | Alias for `stringify` |

### Example

```typescript
import querystring from 'querystring';

// Parse query string
const parsed = querystring.parse('name=john&age=30&hobby=coding&hobby=gaming');
// { name: "john", age: "30", hobby: ["coding", "gaming"] }

// Stringify object
const qs = querystring.stringify({ name: 'john', tags: ['a', 'b'] });
// "name=john&tags=a&tags=b"

// Custom separators
const custom = querystring.parse('name:john;age:30', ';', ':');
// { name: "john", age: "30" }

// Escape/unescape
const escaped = querystring.escape('hello world');  // "hello%20world"
const decoded = querystring.unescape('hello%20world'); // "hello world"
```

---

## readline

User input handling for interactive applications.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `questionSync` | `questionSync(query)` | Prompt user synchronously |
| `createInterface` | `createInterface(options?)` | Create readline interface |

### Interface Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `question` | `question(query, callback)` | Ask question with callback |
| `close` | `close()` | Close the interface |
| `prompt` | `prompt()` | Display prompt character |

### Example

```typescript
import readline from 'readline';

// Simple synchronous prompt
const name = readline.questionSync('What is your name? ');
console.log(`Hello, ${name}!`);

// Using interface
const rl = readline.createInterface();

rl.question('Enter a number: ', (answer) => {
  console.log(`You entered: ${answer}`);
  rl.close();
});
```

---

## url

URL parsing and manipulation.

### Classes

#### URL (WHATWG URL API)

```typescript
new URL(urlString, baseUrl?)
```

**Properties:**
- `href` - Full URL string
- `protocol` - Protocol with colon (e.g., `'https:'`)
- `host` - Host with port
- `hostname` - Host without port
- `port` - Port number as string
- `pathname` - Path portion
- `search` - Query string with `?`
- `hash` - Fragment with `#`
- `origin` - Protocol + host
- `username` - Username portion
- `password` - Password portion
- `searchParams` - URLSearchParams object

#### URLSearchParams

```typescript
new URLSearchParams(init?)
```

**Methods:**
- `get(name)` - Get first value for name
- `getAll(name)` - Get all values for name
- `has(name)` - Check if name exists
- `set(name, value)` - Set value (replaces existing)
- `append(name, value)` - Append value
- `delete(name)` - Remove all values for name
- `keys()` - Get all keys
- `values()` - Get all values
- `size` - Number of parameters

### Legacy Functions

| Function | Signature | Description |
|----------|-----------|-------------|
| `parse` | `parse(urlString, parseQueryString?, slashesDenoteHost?)` | Parse URL string |
| `format` | `format(urlObject)` | Format URL object to string |
| `resolve` | `resolve(from, to)` | Resolve relative URL |

### Example

```typescript
import { URL, URLSearchParams } from 'url';

// Parse URL
const url = new URL('https://example.com:8080/path?query=value#hash');
console.log(url.hostname);  // "example.com"
console.log(url.port);      // "8080"
console.log(url.pathname);  // "/path"
console.log(url.search);    // "?query=value"

// Modify URL
url.pathname = '/new-path';
url.searchParams.set('foo', 'bar');
console.log(url.href);

// URLSearchParams
const params = new URLSearchParams('a=1&b=2&a=3');
console.log(params.get('a'));     // "1"
console.log(params.getAll('a'));  // ["1", "3"]
params.append('c', '4');
params.delete('b');

// Resolve relative URLs
import { resolve } from 'url';
const absolute = resolve('https://example.com/a/b', '../c');
// "https://example.com/a/c"
```

---

## util

Utility functions for formatting and type checking.

### Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `format` | `format(format, ...args)` | Format string with placeholders |
| `inspect` | `inspect(value, options?)` | Convert value to string representation |

### format() Placeholders

| Placeholder | Description |
|-------------|-------------|
| `%s` | String |
| `%d`, `%i` | Integer |
| `%f` | Float |
| `%j` | JSON |
| `%o`, `%O` | Object |
| `%%` | Literal `%` |

### inspect() Options

```typescript
{
  depth?: number  // Recursion depth (default: 2)
}
```

### types Object

Type checking utilities:

| Method | Signature | Description |
|--------|-----------|-------------|
| `isArray` | `isArray(value)` | Check if array |
| `isDate` | `isDate(value)` | Check if Date |
| `isFunction` | `isFunction(value)` | Check if function |
| `isNull` | `isNull(value)` | Check if null |
| `isUndefined` | `isUndefined(value)` | Check if undefined |

### Example

```typescript
import util from 'util';

// Format strings
const msg = util.format('Hello %s, you have %d messages', 'John', 5);
// "Hello John, you have 5 messages"

const json = util.format('Data: %j', { a: 1, b: 2 });
// "Data: {\"a\":1,\"b\":2}"

// Inspect objects
const obj = { nested: { deep: { value: 42 } } };
console.log(util.inspect(obj, { depth: 1 }));
// "{ nested: { deep: [Object] } }"

// Type checking
console.log(util.types.isArray([1, 2, 3]));  // true
console.log(util.types.isFunction(() => {})); // true
console.log(util.types.isNull(null));         // true
```

---

## fs/promises

Promise-based counterparts of the `fs` callback API. Every listed method returns a `Promise` and accepts the same arguments as the synchronous form (minus the error-first callback).

| Method | Description |
|--------|-------------|
| `readFile` | Read file contents |
| `writeFile` | Write file contents |
| `appendFile` | Append to file |
| `copyFile` | Copy file |
| `rename` | Rename/move |
| `unlink` | Delete file |
| `truncate` | Truncate to length |
| `mkdir` | Create directory |
| `mkdtemp` | Create temp directory |
| `rmdir` / `rm` | Remove directory/file |
| `readdir` | List directory contents |
| `stat` / `lstat` | File info |
| `access` | Check accessibility |
| `chmod` | Change permissions |
| `realpath` | Resolve canonical path |
| `readlink` / `symlink` / `link` | Symlink / hardlink ops |
| `utimes` | Update atime/mtime |
| `open` | Returns a `FileHandle` |

### Example

```typescript
import { readFile, writeFile } from 'fs/promises';

const content = await readFile('input.txt', 'utf8');
await writeFile('output.txt', content.toUpperCase());
```

---

## buffer

Binary data handling via the `Buffer` class. `Buffer` is also exposed as a global — `import` is only required when the consumer wants the named binding.

### Static Methods

| Method | Signature | Description |
|--------|-----------|-------------|
| `from` | `Buffer.from(source, encoding?)` | Create from string, array, or ArrayBuffer |
| `alloc` | `Buffer.alloc(size, fill?, encoding?)` | Allocate zero-filled buffer |
| `allocUnsafe` | `Buffer.allocUnsafe(size)` | Allocate without zeroing |
| `allocUnsafeSlow` | `Buffer.allocUnsafeSlow(size)` | Allocate without the pool |
| `isBuffer` | `Buffer.isBuffer(obj)` | Type guard |
| `isEncoding` | `Buffer.isEncoding(encoding)` | Encoding supported? |
| `byteLength` | `Buffer.byteLength(value, encoding?)` | Byte length of a string |
| `concat` | `Buffer.concat(list, totalLength?)` | Concatenate buffers |
| `compare` | `Buffer.compare(a, b)` | Sort-compatible compare |

### Instance Methods

`toString`, `write`, `slice`, `equals`, `compare`, `copy`, `fill`, `indexOf`, `includes`, `toJSON`, `swap16`, `swap32`, `swap64`. Typed read/write: `readInt8`, `readUInt8`, `readInt16BE/LE`, `readUInt16BE/LE`, `readInt32BE/LE`, `readUInt32BE/LE`, `readFloatBE/LE`, `readDoubleBE/LE`, `readBigInt64BE/LE`, `readBigUInt64BE/LE`, and the matching `write*` variants.

### Supported Encodings

`utf8`, `utf16le`/`ucs2`, `ascii`, `latin1`/`binary`, `base64`, `hex`.

### Example

```typescript
const buf = Buffer.from('hello', 'utf8');
console.log(buf.length);              // 5
console.log(buf.toString('hex'));     // "68656c6c6f"
const merged = Buffer.concat([buf, Buffer.from(' world')]);
```

---

## events

Event-driven programming via `EventEmitter`.

### EventEmitter

| Method | Description |
|--------|-------------|
| `on(name, listener)` / `addListener` | Subscribe |
| `once(name, listener)` | Subscribe for a single fire |
| `prependListener` / `prependOnceListener` | Subscribe at the front of the queue |
| `off(name, listener)` / `removeListener` | Unsubscribe |
| `removeAllListeners(name?)` | Remove all listeners for an event (or everything) |
| `emit(name, ...args)` | Dispatch to listeners — returns `true` if any ran |
| `listenerCount(name)` | Number of subscribers |
| `eventNames()` | Names with at least one listener |
| `setMaxListeners(n)` | Warn threshold |

### Example

```typescript
import { EventEmitter } from 'events';

const ee = new EventEmitter();
ee.on('tick', (n: number) => console.log(`tick ${n}`));
ee.emit('tick', 1);
```

---

## stream

Node.js streaming primitives.

### Exports

Classes: `Readable`, `Writable`, `Duplex`, `Transform`, `PassThrough`.

Helpers: `pipeline(...streams, cb?)`, `finished(stream, cb)`, `addAbortSignal(signal, stream)`.

The submodule `stream/promises` exposes promise-returning `pipeline` and `finished`.

### stream/web

WHATWG streams: `ReadableStream`, `WritableStream`, `TransformStream`, `ByteLengthQueuingStrategy`, `CountQueuingStrategy`.

### Example

```typescript
import { pipeline } from 'stream/promises';
import { createReadStream, createWriteStream } from 'fs';
import { createGzip } from 'zlib';

await pipeline(
  createReadStream('input.txt'),
  createGzip(),
  createWriteStream('input.txt.gz'),
);
```

---

## http / https

HTTP client and server.

### Methods

| Method | Description |
|--------|-------------|
| `createServer(requestListener?)` | Create an HTTP server |
| `request(options, callback?)` | Make an HTTP request |
| `get(options, callback?)` | `request` with method forced to `GET` |

### Constants / Objects

- `METHODS` — array of supported HTTP method names
- `STATUS_CODES` — `{ code: reason-phrase }` map
- `Agent` — connection pooling
- `globalAgent` — default shared agent

`https` exposes the same API with TLS transport.

### Server lifecycle and SharpTS connection probe

`server.close()` stops the server after active responses drain.
`server.closeAllConnections()` aborts the responses that are active when it is
called but leaves the listener open, matching Node's separation between the two
operations.

SharpTS additionally exposes `res.probeConnection()` as a best-effort extension for
long-running JSON endpoints. It commits a chunked response, writes one JSON-safe
whitespace byte (compiled mode also flushes it synchronously), and returns `false` if
that write observes a disconnected peer. It returns `false` after the response has
ended. Because it commits headers and changes the response body, it should be called
only when that behavior is acceptable; it is not part of Node's `ServerResponse` API.

### Example

```typescript
import http from 'http';

const server = http.createServer((req, res) => {
  res.writeHead(200, { 'Content-Type': 'text/plain' });
  res.end('Hello\n');
});
server.listen(3000);
```

---

## net

TCP networking.

| Symbol | Description |
|--------|-------------|
| `createServer(options?, connectionListener?)` | New TCP server |
| `createConnection(options, callback?)` / `connect` | New TCP connection |
| `Server` | TCP server class |
| `Socket` | TCP socket class |
| `isIP(input)` | 4, 6, or 0 |
| `isIPv4` / `isIPv6` | Version-specific checks |

### Example

```typescript
import net from 'net';

const server = net.createServer((socket) => {
  socket.write('hello\n');
  socket.end();
});
server.listen(7000);
```

---

## tls

TLS/SSL sockets on top of `net`.

| Symbol | Description |
|--------|-------------|
| `createServer(options, connectionListener?)` | TLS server |
| `connect(options, callback?)` | Client connection |
| `createSecureContext(options)` | Reusable credential bundle |
| `Server`, `TLSSocket` | Classes |
| `DEFAULT_MIN_VERSION`, `DEFAULT_MAX_VERSION` | Default protocol bounds |

Options accept `key`, `cert`, `ca`, `host`, `port`, `minVersion`, `maxVersion`.

---

## dgram

UDP datagrams.

| Symbol | Description |
|--------|-------------|
| `createSocket(type, callback?)` | Create a socket (`'udp4'` or `'udp6'`) |
| `Socket` | Socket class — `bind`, `send`, `close`, EventEmitter interface |

### Example

```typescript
import dgram from 'dgram';

const sock = dgram.createSocket('udp4');
sock.on('message', (msg, rinfo) => console.log(`from ${rinfo.address}: ${msg}`));
sock.bind(41234);
```

---

## dns / dns/promises

DNS resolution.

> **`resolve*` vs `lookup`:** `resolve`/`resolve4`/`resolve6`/`resolveMx`/… use the DNS
> wire protocol — like Node's c-ares resolver — querying the configured DNS server
> (override with the `SHARPTS_DNS_SERVER` env var). They do **not** consult the OS hosts
> file. `lookup`/`lookupService` use the OS resolver (`getaddrinfo`), which does read the
> hosts file. Consequently `resolve4('localhost')` typically returns `ENOTFOUND`, whereas
> `lookup('localhost')` returns `127.0.0.1` — matching Node.

### Top-level Methods

`lookup`, `lookupService`, `resolve`, `resolve4`, `resolve6`, `resolveCaa`, `resolveCname`, `resolveMx`, `resolveNs`, `resolvePtr`, `resolveSoa`, `resolveSrv`, `resolveTxt`, `resolveAny`, `reverse`, `getServers`, `setServers`.

### Resolver

`Resolver` class provides a private instance with its own servers: `new Resolver()`, `resolver.resolve4(host, cb)`, `resolver.setServers([...])`, `resolver.cancel()`.

### Promise API

`import { resolve4 } from 'dns/promises'` — same surface, Promise-returning. Also available as `dns.promises`.

### Error Codes

Exposed as named constants: `NOTFOUND`, `SERVFAIL`, `REFUSED`, `TIMEOUT`, `NODATA`, `FORMERR`, `NOMEM`, `BADQUERY`, `BADNAME`, `BADFAMILY`, `BADRESP`, `CONNREFUSED`, `CANCELLED`, and more.

---

## zlib

Compression/decompression.

### Sync

`deflateSync`, `inflateSync`, `deflateRawSync`, `inflateRawSync`, `gzipSync`, `gunzipSync`, `unzipSync`, `brotliCompressSync`, `brotliDecompressSync`.

### Async (callback)

`deflate`, `inflate`, `deflateRaw`, `inflateRaw`, `gzip`, `gunzip`, `unzip`, `brotliCompress`, `brotliDecompress`.

### Stream Factories

`createDeflate`, `createInflate`, `createDeflateRaw`, `createInflateRaw`, `createGzip`, `createGunzip`, `createUnzip`, `createBrotliCompress`, `createBrotliDecompress`, `createZstdCompress`, `createZstdDecompress`.

### Example

```typescript
import { gzipSync, gunzipSync } from 'zlib';

const compressed = gzipSync(Buffer.from('hello world'));
const original = gunzipSync(compressed).toString();
```

---

## timers / timers/promises

Timer functions. Also available as globals.

| Method | Description |
|--------|-------------|
| `setTimeout(cb, ms, ...args)` | Run once after delay |
| `setInterval(cb, ms, ...args)` | Repeat every delay |
| `setImmediate(cb, ...args)` | Run on next tick |
| `clearTimeout(handle)` / `clearInterval` / `clearImmediate` | Cancel |

`timers/promises` exposes `setTimeout(ms, value?, options?)` and `setImmediate(value?, options?)` returning a `Promise`, both accepting `{ signal }` for cancellation via `AbortController`.

### Example

```typescript
import { setTimeout as delay } from 'timers/promises';

await delay(1000);
console.log('one second later');
```

---

## vm

Compile and execute code in a custom context.

| Method | Description |
|--------|-------------|
| `createContext(contextObject?)` | Wrap an object as a sandbox |
| `isContext(obj)` | Test for a context object |
| `runInContext(code, context, options?)` | Run `code` in an existing context |
| `runInNewContext(code, contextObject?, options?)` | Create a context and run |
| `runInThisContext(code, options?)` | Run in the current context |
| `compileFunction(code, params?, options?)` | Compile to a callable function |
| `Script` | `new Script(code, options?)` with `.runInContext()` / `.runInNewContext()` / `.runInThisContext()` |

Options include `timeout` for execution cap.

---

## worker_threads

Thread-based workers.

| Symbol | Description |
|--------|-------------|
| `Worker` | Spawn a worker; `postMessage`, `terminate`, `on('message')` |
| `MessageChannel`, `BroadcastChannel` | Structured messaging primitives |
| `isMainThread` | `true` in the main thread |
| `parentPort` | Message port to parent (in workers) |
| `workerData` | Data passed at construction |
| `threadId` | Current thread ID |
| `resourceLimits` | Per-worker limits |
| `getEnvironmentData` / `setEnvironmentData` | Shared env map |
| `SHARE_ENV` | Sentinel to inherit environment |
| `markAsUntransferable` / `moveMessagePortToContext` / `receiveMessageOnPort` | Advanced port ops |

---

## cluster

Fork worker processes that share server ports.

| Symbol | Description |
|--------|-------------|
| `fork(env?)` | Create a worker |
| `disconnect(callback?)` | Gracefully disconnect all workers |
| `isPrimary` / `isMaster` | `true` in the primary/master process |
| `isWorker` | `true` in worker processes |
| `worker` | Current worker (when `isWorker`) |
| `workers` | `{ id: Worker }` map (primary) |
| `settings` | Active fork settings |
| `setupPrimary(settings?)` / `setupMaster` | Configure default fork settings |

Extends `EventEmitter` (`on`, `once`, `emit`, `off`, `removeAllListeners`, `listenerCount`, `listeners`, `eventNames`).

---

## async_hooks

Asynchronous context tracking.

### AsyncLocalStorage

| Method | Description |
|--------|-------------|
| `run(store, callback, ...args)` | Execute within a store scope |
| `enterWith(store)` | Set store for the current async chain |
| `exit(callback, ...args)` | Run without any store |
| `getStore()` | Current store, or `undefined` |
| `disable()` | Drop all references |

### Example

```typescript
import { AsyncLocalStorage } from 'async_hooks';

const als = new AsyncLocalStorage<{ requestId: string }>();

als.run({ requestId: 'abc' }, () => {
  // Any `getStore()` call in this (and descendant) async task sees { requestId: 'abc' }
  handleRequest();
});
```

---

## perf_hooks

High-resolution performance timing.

### performance

| Member | Description |
|--------|-------------|
| `now()` | Monotonic time in ms since `timeOrigin` |
| `timeOrigin` | Wall-clock anchor (ms since epoch) |
| `mark(name, options?)` | Record a named timestamp |
| `measure(name, startMark?, endMark?)` | Record a duration between marks |
| `getEntries()` / `getEntriesByName` / `getEntriesByType` | Query recorded entries |
| `clearMarks(name?)` / `clearMeasures(name?)` | Drop entries |

### PerformanceObserver

Subscribe to mark/measure events synchronously:

```typescript
import { performance, PerformanceObserver } from 'perf_hooks';

const obs = new PerformanceObserver((list) => {
  for (const entry of list.getEntries()) console.log(entry.name, entry.duration);
});
obs.observe({ entryTypes: ['measure'] });

performance.mark('start');
// ... work ...
performance.mark('end');
performance.measure('work', 'start', 'end');
```

---

## string_decoder

Incremental decoding of `Buffer` chunks to strings, preserving multi-byte sequences across writes.

### StringDecoder

```typescript
new StringDecoder(encoding?)  // default 'utf8'
```

| Method | Description |
|--------|-------------|
| `write(buffer)` | Decode chunk, buffering any trailing incomplete sequence |
| `end(buffer?)` | Flush any buffered bytes and return the final string |

### Example

```typescript
import { StringDecoder } from 'string_decoder';

const decoder = new StringDecoder('utf8');
const a = decoder.write(Buffer.from([0xE2, 0x82]));       // "" — incomplete
const b = decoder.write(Buffer.from([0xAC]));             // "€"
```

---

## tty

Terminal detection.

| Method | Description |
|--------|-------------|
| `isatty(fd)` | Is the file descriptor a TTY? |

`ReadStream` / `WriteStream` classes are not currently implemented — use `process.stdout.isTTY` (a boolean) or `tty.isatty(1)` for the common case.

---

## Notes

### Error Handling

File system errors include Node.js-compatible error codes:

```typescript
try {
  fs.readFileSync('nonexistent.txt');
} catch (e) {
  if (e.code === 'ENOENT') {
    console.log('File not found');
  }
}
```

### Stream Objects

The `process.stdin`, `process.stdout`, and `process.stderr` properties are stream objects with standard stream methods.
