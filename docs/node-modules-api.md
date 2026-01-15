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

File system operations. **Note: Only synchronous APIs are supported.**

### File Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `existsSync` | `existsSync(path)` | Check if path exists |
| `readFileSync` | `readFileSync(path, encoding?)` | Read file contents |
| `writeFileSync` | `writeFileSync(path, data, encoding?)` | Write to file |
| `appendFileSync` | `appendFileSync(path, data, encoding?)` | Append to file |
| `copyFileSync` | `copyFileSync(src, dest)` | Copy file |
| `renameSync` | `renameSync(oldPath, newPath)` | Rename/move file |
| `unlinkSync` | `unlinkSync(path)` | Delete file |

### Directory Operations

| Method | Signature | Description |
|--------|-----------|-------------|
| `mkdirSync` | `mkdirSync(path)` | Create directory |
| `rmdirSync` | `rmdirSync(path, options?)` | Remove directory |
| `readdirSync` | `readdirSync(path)` | List directory contents |

### File Information

| Method | Signature | Description |
|--------|-----------|-------------|
| `statSync` | `statSync(path)` | Get file/directory stats |
| `lstatSync` | `lstatSync(path)` | Get stats (symlink-aware) |
| `accessSync` | `accessSync(path, mode?)` | Check file accessibility |

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
| `hrtime` | `hrtime(time?)` | High-resolution time |
| `uptime` | `uptime()` | Process uptime in seconds |
| `memoryUsage` | `memoryUsage()` | Memory usage statistics |

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

## Notes

### Synchronous APIs Only

The `fs` module only supports synchronous operations (`*Sync` methods). Async methods like `readFile()`, `writeFile()`, etc. are not available.

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
