# SharpTS Examples

This directory contains practical TypeScript examples demonstrating SharpTS capabilities, from basic utilities to advanced interoperability with C#.

## Quick Start

All examples can be run using the SharpTS interpreter:

```bash
sharpts Examples/<example-name>.ts [arguments]
```

Or compiled ahead-of-time to .NET assemblies:

```bash
sharpts --compile Examples/<example-name>.ts
dotnet Examples/<example-name>.dll [arguments]
```

## Examples Overview

### 1. File Hasher (`file-hasher.ts`)

**What it does:** Generates multiple checksums (MD5, SHA1, SHA256, SHA512) for any file.

**Usage:**
```bash
sharpts Examples/file-hasher.ts <filepath>

# Example
sharpts Examples/file-hasher.ts README.md
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
sharpts Examples/file-organizer.ts <directory> [--dry-run]

# Example - preview changes without moving files
sharpts Examples/file-organizer.ts ~/Downloads --dry-run

# Example - actually organize files
sharpts Examples/file-organizer.ts ~/Downloads
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
sharpts Examples/password-generator.ts [length]

# Example - interactive mode
sharpts Examples/password-generator.ts

# Example - specify length upfront
sharpts Examples/password-generator.ts 24
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
sharpts Examples/system-info.ts
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
sharpts Examples/url-toolkit.ts "https://example.com/path?key=value"

# Interactive mode
sharpts Examples/url-toolkit.ts
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
sharpts Examples/SourceAnalyzer/source-analyzer.ts [directory] [--help]

# Example - analyze current directory
sharpts Examples/SourceAnalyzer/source-analyzer.ts

# Example - analyze specific directory
sharpts Examples/SourceAnalyzer/source-analyzer.ts ./src

# Example - show help
sharpts Examples/SourceAnalyzer/source-analyzer.ts --help
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
# From Examples/Interop directory
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

**See `Examples/Interop/README.md` for detailed documentation.**

---

## Feature Matrix

This table shows which SharpTS/TypeScript features each example demonstrates:

| Feature | file-hasher | file-organizer | password-generator | system-info | url-toolkit | source-analyzer | interop |
|---------|-------------|----------------|-----------------------|-------------|-------------|-----------------|---------|
| Classes | | | | | | ✓ | ✓ |
| Interfaces | | | | | | ✓ | |
| Inheritance | | | | | | | ✓ |
| For-of loops | ✓ | ✓ | | ✓ | ✓ | | |
| While loops | | | | | ✓ | ✓ | |
| Object literals | | ✓ | | | ✓ | | |
| Arrays | ✓ | ✓ | | ✓ | ✓ | ✓ | ✓ |
| String manipulation | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Type annotations | | | | | | ✓ | ✓ |
| Functions | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Modules (import) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| CLI arguments | ✓ | ✓ | ✓ | | ✓ | ✓ | |
| File I/O | ✓ | ✓ | | | | ✓ | |
| Crypto | ✓ | | ✓ | | | | |
| User input | | | ✓ | | ✓ | | |
| Process info | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | |
| OS info | | | | ✓ | | | |
| Path manipulation | ✓ | ✓ | | ✓ | | ✓ | |
| URL parsing | | | | | ✓ | | |
| C# interop | | | | | | | ✓ |

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

## Tips for Running Examples

**Interpreted mode** (faster for development):
```bash
sharpts Examples/<example>.ts
```

**Compiled mode** (better performance, standalone executable):
```bash
# Compile
sharpts --compile Examples/<example>.ts

# Run the compiled assembly
dotnet Examples/<example>.dll
```

**View help for examples:**
Most examples display usage information when run without arguments:
```bash
sharpts Examples/file-hasher.ts
```


## Learning Path

Recommended order for exploring examples:

1. **system-info.ts** - Start here to see basic built-in modules
2. **file-hasher.ts** - Learn file operations and crypto
3. **password-generator.ts** - Explore user input and randomness
4. **file-organizer.ts** - Practice file system manipulation
5. **url-toolkit.ts** - Interactive CLI patterns
6. **source-analyzer.ts** - Complex application with interfaces
7. **Interop/** - Advanced: C# interoperability

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
// Usage: sharpts Examples/my-example.ts <args>
//
// Demonstrates: feature1, feature2, feature3

import module from 'module';
import process from 'process';

function main(): void {
    const args = process.argv.slice(2);

    if (args.length === 0) {
        console.log('My Example - Description');
        console.log('');
        console.log('Usage: sharpts Examples/my-example.ts <args>');
        return;
    }

    // Your code here
}

main();
```

## Additional Resources

- **SharpTS README** (`../README.md`) - Project overview and build instructions
- **CLAUDE.md** (`../CLAUDE.md`) - Detailed architecture and development guide
- **Test Suite** (`../SharpTS.Tests/`) - Comprehensive feature tests
- **Interop Documentation** (`Interop/README.md`) - C# interop details

## Contributing Examples

Have an interesting example? Consider:
- Demonstrating a specific SharpTS feature
- Solving a practical problem
- Showing idiomatic TypeScript patterns
- Highlighting interop capabilities

Keep examples focused, well-documented, and easy to run.
