# SharpTS Examples

This is the canonical runnable SharpTS cookbook, from basic utilities to npm, GUI, hosting, and
advanced interoperability with C#. Documentation pages link here instead of maintaining duplicate
snippets that can drift from executable source.

## Quick Start

All examples can be run using the SharpTS interpreter:

```bash
sharpts samples/<example-name>.ts [arguments]
```

Or compiled ahead-of-time to .NET assemblies:

```bash
sharpts --compile samples/<example-name>.ts
dotnet samples/<example-name>.dll [arguments]
```

On Unix-like systems, examples with a SharpTS shebang can also be run directly
after they have been marked executable:

```bash
chmod +x samples/text-stats.ts
./samples/text-stats.ts README.md --top 5
```

## Examples Overview

### Executable Text Statistics (`text-stats.ts`)

**What it does:** Summarizes a text file and lists its most frequent words. Because the file starts
with `#!/usr/bin/env sharpts`, it behaves like a Python or shell script on Unix-like systems.

**Usage:**
```bash
chmod +x samples/text-stats.ts

# Show the five most common words with at least four characters
./samples/text-stats.ts README.md --top 5 --min-length 4

# Keep differently-cased words separate
./samples/text-stats.ts README.md --case-sensitive
```

The `sharpts` executable must be available on `PATH`. You can also invoke the example portably as
`sharpts samples/text-stats.ts README.md --top 5`.

**Demonstrates:**
- Unix shebang execution with `#!/usr/bin/env sharpts`
- Executable TypeScript scripts and normal command-line parameters
- `fs` and `path` module usage
- Argument validation and process exit codes
- Word-frequency counting with `Map` and deterministic sorting

---

### Desktop Calculator (`Calculator/`)

**What it does:** Builds a complete retained Avalonia desktop calculator from typed TSX.

**Demonstrates:** Function components, reducer/effect/memo/callback hooks, natural children,
keyboard input, focus refs, direct styling, interpreted/compiled guest parity, and packaged SDK
consumption. See the [calculator guide](Calculator/README.md) for local build and run commands.

---

### SharpPaint Desktop Editor (`SharpPaint/`)

**What it does:** Builds a layered Paint.NET-inspired desktop editor with functional drawing,
eraser compositing, shapes, undo/redo, zoom, portable project files, and PNG import/export.

**Demonstrates:** Native pointer capture, retained logical-coordinate drawing, isolated layer
compositing, filesystem and dialog services, drag/drop, complex reducer state, interpreted/compiled
guest parity, and a documented capability-gap matrix. See the
[SharpPaint guide](SharpPaint/README.md) for commands and known gaps.

---

### 1. File Hasher (`file-hasher.ts`)

**What it does:** Generates multiple checksums (MD5, SHA1, SHA256, SHA512) for any file.

**Usage:**
```bash
sharpts samples/file-hasher.ts <filepath>

# Example
sharpts samples/file-hasher.ts README.md
```

**Demonstrates:**
- `crypto` module: `createHash()`, `.update()`, `.digest()`
- `fs` module: `readFileSync()`, `existsSync()`, `statSync()`
- `path` module: `basename()`, `resolve()`
- Command-line argument processing via `process.argv`
- String manipulation and formatting
- For-of loops with arrays

**Key Features:**
- Displays file size in human-readable format (B, KB, MB, GB)
- Validates file existence and type
- Computes four hash algorithms in one pass
- Clean tabular output

---

### 2. File Organizer (`file-organizer.ts`)

**What it does:** Automatically organizes files in a directory by moving them into categorized folders based on file extension (images, documents, code, archives, etc.).

**Usage:**
```bash
sharpts samples/file-organizer.ts <directory> [--dry-run]

# Example - preview changes without moving files
sharpts samples/file-organizer.ts ~/Downloads --dry-run

# Example - actually organize files
sharpts samples/file-organizer.ts ~/Downloads
```

**Demonstrates:**
- `fs` module: `readdirSync()`, `statSync()`, `mkdirSync()`, `renameSync()`, `existsSync()`
- `path` module: `join()`, `extname()`, `basename()`
- Object literals for mapping data
- Property access syntax with bracket notation
- Conditional logic and directory creation
- Safe "dry run" pattern for file operations

**Key Features:**
- Pre-configured categories for common file types
- Creates destination folders automatically
- Dry-run mode to preview changes
- Falls back to generic categorization for unknown extensions
- Skips files without extensions
- Summary statistics

---

### 3. Password Generator (`password-generator.ts`)

**What it does:** Interactive password generator with customizable character sets and cryptographically secure randomness.

**Usage:**
```bash
sharpts samples/password-generator.ts [length]

# Example - interactive mode
sharpts samples/password-generator.ts

# Example - specify length upfront
sharpts samples/password-generator.ts 24
```

**Demonstrates:**
- `crypto` module: `randomBytes()`, `randomInt()`
- `readline` module: `questionSync()` for user input
- String concatenation and character manipulation
- Interactive CLI with yes/no questions
- Input validation and error handling
- Mathematical calculations (entropy calculation using `Math.log()`)

**Key Features:**
- Generates 5 password options at once
- Customizable character sets (lowercase, uppercase, digits, symbols)
- Calculates password entropy in bits
- Validates password length (4-128 characters)
- Uses cryptographically secure random number generation

---

### 4. System Info (`system-info.ts`)

**What it does:** Displays comprehensive system information including OS details, memory usage, CPU info, and process metrics.

**Usage:**
```bash
sharpts samples/system-info.ts
```

**Demonstrates:**
- `os` module: `platform()`, `arch()`, `hostname()`, `cpus()`, `totalmem()`, `freemem()`, `homedir()`, `tmpdir()`, `userInfo()`, `type()`, `release()`
- `process` module: `pid`, `version`, `cwd()`, `env`, `argv`, `uptime()`, `memoryUsage()`
- Number formatting and calculations
- Working with arrays (CPU cores)
- Accessing object properties
- Environment variable access
- String truncation for display

**Key Features:**
- Memory statistics with percentage calculation
- CPU information (cores, model, speed)
- Process uptime formatting (hours, minutes, seconds)
- Selective environment variable display
- Human-readable memory sizes (GB and MB)

---

### 5. URL Toolkit (`url-toolkit.ts`)

**What it does:** Parse, build, and manipulate URLs with an interactive command-line interface.

**Usage:**
```bash
# Parse a URL from command line
sharpts samples/url-toolkit.ts "https://example.com/path?key=value"

# Interactive mode
sharpts samples/url-toolkit.ts
```

**Interactive Commands:**
- `parse <url>` - Parse and display URL components
- `encode <string>` - URL encode a string
- `decode <string>` - URL decode a string
- `resolve <base> <rel>` - Resolve a relative URL against a base URL
- `build` - Build a URL interactively
- `quit` - Exit

**Demonstrates:**
- `url` module: `parse()`, `resolve()`
- `querystring` module: `parse()`, `stringify()`, `escape()`, `unescape()`
- `readline` module: Interactive input loops
- String methods: `startsWith()`, `substring()`, `trim()`, `split()`
- Object key iteration with `Object.keys()`
- While loops and interactive REPL pattern
- Optional/nullable handling with `||` operator

**Key Features:**
- Full URL parsing (protocol, host, port, pathname, query, hash)
- Query string parameter extraction
- Interactive URL builder
- URL encoding/decoding utilities
- Relative URL resolution

---

### 6. Source Analyzer (`SourceAnalyzer/source-analyzer.ts`)

**What it does:** Comprehensive source code analysis tool that scans directories recursively and generates statistics about code files.

**Usage:**
```bash
sharpts samples/SourceAnalyzer/source-analyzer.ts [directory] [--help]

# Example - analyze current directory
sharpts samples/SourceAnalyzer/source-analyzer.ts

# Example - analyze specific directory
sharpts samples/SourceAnalyzer/source-analyzer.ts ./src

# Example - show help
sharpts samples/SourceAnalyzer/source-analyzer.ts --help
```

**Demonstrates:**
- TypeScript interfaces for type safety
- Complex directory traversal with recursion
- File filtering and pattern matching
- Multi-line string processing
- Advanced function detection heuristics
- Table formatting with padded strings
- Modular code organization with logical sections
- Process exit codes with `process.exit()`
- Path manipulation: `isAbsolute()`, `join()`

**Key Features:**
- Supports multiple file types (.ts, .tsx, .js, .jsx, .css, .html, .json)
- Auto-excludes common directories (node_modules, .git, dist, build, obj, bin)
- Counts total lines, non-empty lines, and functions
- Function detection for multiple patterns (function keyword, arrow functions, class methods)
- Formatted table output with summary statistics
- Handles Windows reserved filenames safely

---

### 7. Interop Example (`Interop/`)

**What it does:** Demonstrates how to consume SharpTS-compiled TypeScript assemblies from C# applications using runtime reflection.

This is a more complex example with its own subdirectory structure and build process.

**Structure:**
```
Interop/
├── TypeScript/
│   └── Library.ts        # TypeScript source with example classes
├── CompiledTS/           # Generated assemblies
│   ├── Library.dll       # Compiled TypeScript (generated)
│   └── SharpTS.dll       # Runtime dependency (copied)
├── Program.cs            # C# consumer demonstrating interop
├── build.ps1             # Automated build script
├── README.md             # Detailed interop documentation
└── SharpTS.Example.Interop.csproj
```

**Build and Run:**
```powershell
# From samples/Interop directory
.\build.ps1
```

**TypeScript Features Demonstrated:**
- Classes with constructors and methods
- Instance and static members
- Property accessors
- Class inheritance with `extends`
- Method overriding
- Top-level functions
- Arrays and collections

**C# Interop Patterns:**
- Loading compiled TypeScript assemblies with `Assembly.LoadFrom()`
- Type discovery via `Assembly.GetType()`
- Instance creation with `Activator.CreateInstance()`
- Property access via `PropertyInfo` (PascalCase naming)
- Method invocation with `MethodInfo.Invoke()`
- Static member access with `BindingFlags.Static`
- Working with inheritance hierarchies
- Accessing top-level functions via `$Program` class

**Type Mapping:**
| TypeScript | .NET Runtime |
|------------|--------------|
| `number`   | `double`     |
| `string`   | `string`     |
| `boolean`  | `bool`       |
| `T[]`      | `List<object>` |

See the [interop example guide](Interop/README.md) for detailed documentation.

---

### 8. Benchmark (`benchmark.ts`)

**What it does:** Times four small CPU-bound workloads using `perf_hooks`, then runs two phases under `performance.mark()` / `performance.measure()`. Output shows ms and ops/sec per workload and a grand-total time.

**Usage:**
```bash
sharpts samples/benchmark.ts                                 # interpreted
sharpts --compile samples/benchmark.ts -o b.dll && dotnet b.dll   # compiled
```

**Demonstrates:**
- `perf_hooks` module: `performance.now()`, `performance.mark()`, `performance.measure()`, `performance.getEntriesByType()`, `performance.timeOrigin`
- Warm-up loops, iteration scaling, ops/sec reporting
- Same-file comparison between tree-walking interpretation and compiled IL; use the benchmark suite
  for maintained cross-runtime measurements

**Key Features:**
- Runs identically in both modes; the output differs only in absolute timings
- Ships iteration counts tuned so interpreted completes in a few seconds — compiled is effectively instant

---

### 9. .NET Types (`dotnet-types.ts`)

**What it does:** Demonstrates the *inbound* direction of .NET interop — TypeScript consuming .NET types via the `@DotNetType` decorator. Shows `StringBuilder`, `Guid.newGuid()`, `Convert.toInt32` with an `@DotNetOverload` hint, `Task.Run` with a TypeScript closure as a .NET `Action`, and `AppDomain.ProcessExit` with a DOM-style `addEventListener`.

**Usage:**
```bash
sharpts samples/dotnet-types.ts                                     # interpreted
sharpts --compile samples/dotnet-types.ts -o d.dll && dotnet d.dll  # compiled .NET DLL
```

This example does not cause the compiler to copy `SharpTS.dll`; its required support is emitted into
the output. Programs that use soft-dependent runtime features or external .NET assemblies must also
deploy those dependencies. See the linked .NET Types guide for the mode-specific boundaries.

**Demonstrates:**
- `@DotNetType("System.Text.StringBuilder")` — instance methods and properties
- `@DotNetType("System.Guid")` — static methods
- `@DotNetOverload("int")` — pinning the `Convert.ToInt32(int)` overload so `3.7 → 3` instead of the default `ToInt32(double)` `3.7 → 4`
- Delegates — a TS arrow function passed as `System.Action` to `Task.Run`
- Events — subscribing to `System.AppDomain.ProcessExit` via `addEventListener`; the handler fires at shutdown

See [dotnet-types.md](../docs/dotnet-types.md) for the full surface of the decorator and related features like `@DotNetOverload` and the threading contract for delegate callbacks.

---

### 10. npm Package: `uuid` (`NpmUuid/npm-uuid.ts`)

**What it does:** Consumes the real [`uuid`](https://www.npmjs.com/package/uuid) package from `node_modules`. Generates v4 UUIDs, validates strings, reads the `NIL` constant, parses a UUID to its 16-byte form, and does a 1000-iteration uniqueness check.

**Setup (one-time):**
```bash
cd samples/NpmUuid
npm install
```

**Usage:**
```bash
sharpts samples/NpmUuid/npm-uuid.ts                            # interpreted
sharpts --compile samples/NpmUuid/npm-uuid.ts -o u.dll && dotnet u.dll  # compiled
```

**Demonstrates:**
- Named ESM `import { v4, validate, ... } from 'uuid'` against a real npm package
- Multiple named exports backed by Babel-style accessor descriptors
- Working against `Uint8Array` return values

---

### 11. Web Server (`web-server.ts`)

**What it does:** A demonstration HTTP server with routing, static HTML pages, and dynamic JSON API endpoints. Showcases SharpTS's HTTP server capabilities.

**Usage:**
```bash
# Start server on default port 3000
sharpts samples/web-server.ts

# Start server on custom port
sharpts samples/web-server.ts 8080

# Show help
sharpts samples/web-server.ts --help
```

**Demonstrates:**
- `http` module: `createServer()`, server events (`on('listening')`, `on('error')`)
- Request properties: `method`, `url`, `headers`, `socket`
- Response methods: `writeHead()`, `end()`
- `url` module: `parse()` for URL parsing
- `querystring` module: `parse()` for query string parsing
- Content-Type headers (text/html, application/json)
- HTTP status codes (200, 404)
- Path parameter extraction from URLs

**Key Features:**
- Multiple route handlers with pattern matching
- Static HTML pages with inline CSS styling
- Dynamic JSON API endpoints
- Request logging to console
- XSS prevention via HTML escaping
- Personalized greetings with path parameters
- Echo endpoint showing request details

**Routes:**

| Route | Method | Response | Description |
|-------|--------|----------|-------------|
| `/` | GET | HTML | Home page with navigation links |
| `/about` | GET | HTML | About page with server info |
| `/api/time` | GET | JSON | Current server timestamp |
| `/api/echo` | GET | JSON | Echo request info (method, url, headers, query) |
| `/api/greet/:name` | GET | JSON | Personalized greeting with path parameter |
| `*` | ANY | HTML | 404 Not Found page |

---

## Feature Matrix

This table shows which SharpTS/TypeScript features each example demonstrates. Abbreviations: tx = text-stats, fh = file-hasher, fo = file-organizer, pg = password-generator, si = system-info, ut = url-toolkit, sa = source-analyzer, ip = interop, ws = web-server, bm = benchmark, dn = dotnet-types, nu = npm-uuid.

| Feature              | tx | fh | fo | pg | si | ut | sa | ip | ws | bm | dn | nu |
|----------------------|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|:--:|
| Unix shebang         | ✓  |    |    |    |    |    |    |    |    |    |    |    |
| Classes              |    |    |    |    |    |    | ✓  | ✓  |    |    | ✓  |    |
| Interfaces           | ✓  |    |    |    |    |    | ✓  |    |    | ✓  |    |    |
| Inheritance          |    |    |    |    |    |    |    | ✓  |    |    |    |    |
| For-of loops         | ✓  | ✓  | ✓  |    | ✓  | ✓  |    |    | ✓  | ✓  |    |    |
| While loops          |    |    |    |    |    | ✓  | ✓  |    |    |    |    |    |
| Object literals      | ✓  |    | ✓  |    |    | ✓  |    |    | ✓  | ✓  |    |    |
| Arrays               | ✓  | ✓  | ✓  |    | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  |    | ✓  |
| String manipulation  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  |    |    |    |
| Type annotations     | ✓  |    |    |    |    |    | ✓  | ✓  |    | ✓  | ✓  |    |
| Functions            | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  |
| Modules (import)     | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  |    | ✓  | ✓  |    |    |
| CommonJS (require)   |    |    |    |    |    |    |    |    |    |    |    | ✓  |
| CLI arguments        | ✓  | ✓  | ✓  | ✓  |    | ✓  | ✓  |    | ✓  |    |    |    |
| File I/O             | ✓  | ✓  | ✓  |    |    |    | ✓  |    |    |    |    |    |
| Crypto               |    | ✓  |    | ✓  |    |    |    |    |    |    |    |    |
| User input           |    |    |    | ✓  |    | ✓  |    |    |    |    |    |    |
| Process info         | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  | ✓  |    | ✓  |    |    |    |
| OS info              |    |    |    |    | ✓  |    |    |    |    |    |    |    |
| Path manipulation    | ✓  | ✓  | ✓  |    | ✓  |    | ✓  |    |    |    |    |    |
| URL parsing          |    |    |    |    |    | ✓  |    |    | ✓  |    |    |    |
| HTTP server          |    |    |    |    |    |    |    |    | ✓  |    |    |    |
| Outbound C# interop  |    |    |    |    |    |    |    | ✓  |    |    |    |    |
| Inbound .NET interop |    |    |    |    |    |    |    |    |    |    | ✓  |    |
| perf_hooks           |    |    |    |    |    |    |    |    |    | ✓  |    |    |
| npm package          |    |    |    |    |    |    |    |    |    |    |    | ✓  |

## Built-in Modules Used

SharpTS provides Node.js-compatible built-in modules:

**File System (`fs`)**
- `readFileSync()` - Read file contents
- `readdirSync()` - List directory entries
- `statSync()` - Get file/directory stats
- `existsSync()` - Check if path exists
- `mkdirSync()` - Create directory
- `renameSync()` - Move/rename files

**Path (`path`)**
- `join()` - Combine path segments
- `resolve()` - Resolve absolute path
- `basename()` - Get filename from path
- `extname()` - Get file extension
- `isAbsolute()` - Check if path is absolute

**Crypto (`crypto`)**
- `createHash()` - Create hash instance
- `randomBytes()` - Generate random bytes
- `randomInt()` - Generate random integer

**OS (`os`)**
- `platform()`, `arch()`, `type()`, `release()` - OS information
- `hostname()` - System hostname
- `cpus()` - CPU information
- `totalmem()`, `freemem()` - Memory information
- `homedir()`, `tmpdir()` - Directory paths
- `userInfo()` - Current user information

**Process (`process`)**
- `argv` - Command-line arguments
- `env` - Environment variables
- `cwd()` - Current working directory
- `pid` - Process ID
- `version` - Node version
- `uptime()` - Process uptime
- `memoryUsage()` - Process memory usage
- `exit()` - Exit with code

**URL (`url`)**
- `parse()` - Parse URL string
- `resolve()` - Resolve relative URLs

**Query String (`querystring`)**
- `parse()` - Parse query string
- `stringify()` - Build query string
- `escape()` - URL encode
- `unescape()` - URL decode

**Readline (`readline`)**
- `questionSync()` - Synchronous user input

**HTTP (`http`)**
- `createServer(handler)` - Create an HTTP server
- Server methods: `listen(port, callback?)`, `close(callback?)`
- Server events: `on('listening')`, `on('error')`, `on('request')`
- Server properties: `listening`, `address()`
- Request properties: `method`, `url`, `headers`, `socket`, `httpVersion`
- Response methods: `writeHead(status, headers?)`, `write(data)`, `end(data?)`, `setHeader(name, value)`
- Response properties: `statusCode`, `statusMessage`, `headersSent`

## Tips for Running Examples

**Interpreted mode** (faster for development):
```bash
sharpts samples/<example>.ts
```

**Compiled mode** (better performance, ahead-of-time .NET assembly):
```bash
# Compile
sharpts --compile samples/<example>.ts

# Run the compiled assembly
dotnet samples/<example>.dll
```

**View help for examples:**
Most examples display usage information when run without arguments:
```bash
sharpts samples/file-hasher.ts
```


## Learning Path

Recommended order for exploring examples:

1. **system-info.ts** - Start here to see basic built-in modules
2. **file-hasher.ts** - Learn file operations and crypto
3. **password-generator.ts** - Explore user input and randomness
4. **file-organizer.ts** - Practice file system manipulation
5. **url-toolkit.ts** - Interactive CLI patterns
6. **web-server.ts** - HTTP server with routing and APIs
7. **source-analyzer.ts** - Complex application with interfaces
8. **benchmark.ts** - `perf_hooks` and an interpreted-vs-compiled comparison
9. **NpmUuid/npm-uuid.ts** - Consuming a real npm package
10. **dotnet-types.ts** - Inbound interop: calling .NET BCL from TypeScript
11. **Interop/** - Outbound interop: consuming compiled TS from C#

## Creating Your Own Examples

When creating new examples:

1. Add a comment header explaining usage and demonstrated features
2. Include a `main()` function for organization
3. Use `process.argv.slice(2)` for command-line arguments
4. Provide helpful error messages
5. Show usage information when run without required arguments
6. Consider adding both interpreted and compiled usage examples

Example template:

```typescript
// My Example - Brief description
// Usage: sharpts samples/my-example.ts <args>
//
// Demonstrates: feature1, feature2, feature3

import module from 'module';
import process from 'process';

function main(): void {
    const args = process.argv.slice(2);

    if (args.length === 0) {
        console.log('My Example - Description');
        console.log('');
        console.log('Usage: sharpts samples/my-example.ts <args>');
        return;
    }

    // Your code here
}

main();
```

## Additional Resources

- **SharpTS README** (`../README.md`) - Project overview and build instructions
- [Documentation hub](../docs/README.md) - Task-oriented user and contributor guides
- [Architecture](../ARCHITECTURE.md) - Stable subsystem boundaries and invariants
- [Interop documentation](Interop/README.md) - C# interop details

## Contributing Examples

Have an interesting example? Consider:
- Demonstrating a specific SharpTS feature
- Solving a practical problem
- Showing idiomatic TypeScript patterns
- Highlighting interop capabilities

Keep examples focused, well-documented, and easy to run.
