# Execution Modes: Interpreted vs Compiled

SharpTS offers two execution modes for running TypeScript code: **interpreted** (default) and **compiled** (ahead-of-time IL compilation). This guide helps you understand when to use each mode.

> **Note for Contributors**: This guide uses the `sharpts` command assuming SharpTS is installed as a global .NET tool (`dotnet tool install -g SharpTS`). If you're developing from source, replace `sharpts` with `dotnet run --` in all commands.

## Quick Comparison

| Aspect | Interpreted | Compiled |
|--------|-------------|----------|
| Command | `sharpts file.ts` | `sharpts --compile file.ts` |
| Output | Console output | `.dll` executable |
| Startup | Instant | Requires build step |
| Execution speed | Slower | Faster |
| REPL support | Yes | No |
| Distribution | Requires SharpTS | Standalone `.dll` |
| Best for | Development | Production |

---

## Interpreted Mode

Interpreted mode executes TypeScript directly using a tree-walking interpreter. This is the default when you run SharpTS.

### Usage

```bash
# Run a file
sharpts myapp.ts

# Start interactive REPL
sharpts
```

### How It Works

1. **Lexer** tokenizes your source code
2. **Parser** builds an Abstract Syntax Tree (AST)
3. **Type Checker** validates types statically
4. **Interpreter** walks the AST and executes each node

### When to Use Interpreted Mode

- **Development and debugging** - Immediate feedback without compilation
- **Prototyping** - Quick iteration on ideas
- **Learning** - Interactive REPL for experimenting
- **Small scripts** - One-off automation tasks
- **Testing language features** - Try out syntax without build overhead

### Advantages

- **No build step** - Changes run immediately
- **Interactive REPL** - Test code snippets interactively
- **Better error messages** - Direct correlation between errors and source
- **Simpler workflow** - Edit, save, run

### Limitations

- **Slower execution** - Each statement is interpreted at runtime
- **No distributable output** - Cannot share as standalone executable

---

## Compiled Mode (IL Compilation)

Compiled mode generates a .NET IL assembly that runs natively on the .NET runtime. The compiled output is significantly faster and can be distributed independently.

### Usage

```bash
# Basic compilation
sharpts --compile myapp.ts
# Output: myapp.dll, myapp.runtimeconfig.json

# Custom output path
sharpts --compile myapp.ts -o build/app.dll

# Run the compiled output
dotnet myapp.dll
```

### How It Works

1. **Lexer** and **Parser** - Same as interpreted mode
2. **Type Checker** - Same static validation
3. **Dead Code Analyzer** - Identifies unreachable code
4. **IL Compiler** - Generates .NET IL bytecode in multiple phases:
   - Emit runtime support types
   - Analyze closures and variable captures
   - Define classes and functions
   - Generate method bodies
   - Create entry point

### Output Files

Compilation produces:
- `<name>.dll` - The executable .NET assembly
- `<name>.runtimeconfig.json` - Runtime configuration for .NET

### When to Use Compiled Mode

- **Production deployment** - Faster execution in production
- **Distribution** - Share your application with others
- **Performance-critical code** - Computation-heavy applications
- **CI/CD pipelines** - Build once, deploy anywhere
- **.NET integration** - Use compiled output from other .NET projects

### Advantages

- **Faster execution** - Native IL runs at near-.NET speeds
- **Standalone distribution** - Share `.dll` files without SharpTS
- **Dead code elimination** - Unused code is not emitted
- **IL verification** - Optionally verify correctness with `--verify`
- **.NET ecosystem** - Interop with other .NET libraries

### Limitations

- **Requires build step** - Must recompile after changes
- **No REPL** - Cannot compile interactive input
- **Larger output** - Runtime support types embedded in assembly

---

## Feature Support

Both modes support the full SharpTS language. The key differences are in execution characteristics, not language features.

| Feature | Interpreted | Compiled |
|---------|-------------|----------|
| Variables and types | Runtime type checking | Static IL with type metadata |
| Functions and closures | Environment chain capture | Display classes with field captures |
| Classes and inheritance | Dynamic SharpTSClass objects with composition | Real .NET types with native inheritance |
| Interfaces | Type-erased (compile-time only) | Type-erased (compile-time only) |
| Async/await | Direct Task delegation | Native IAsyncStateMachine structs |
| Generators | Eager evaluation with YieldException | Lazy IEnumerator state machines |
| Modules (import/export) | Dynamic late-bound loading | Static types with topological init |
| Decorators | Full execution (Stage 2 & 3) | .NET attributes (no runtime execution) |
| Enums | Runtime objects with dynamic lookup | Compile-time metadata (zero-cost) |
| Template literals | StringBuilder concatenation | Array building + concat helper |
| Spread/rest operators | Single-pass inline expansion | Two-phase with runtime helpers |
| Optional chaining (?.) | Short-circuit evaluation | IL conditional branches |
| Nullish coalescing (??) | Null/undefined checks at runtime | IL null checks with branch optimization |

### Implementation Differences

While both modes support the same features, the implementation differs:

- **Closures**: Interpreter uses environment chain (linked list of scopes); compiler generates display classes with field captures for only referenced variables
- **Async/await**: Interpreter delegates directly to .NET Task system; compiler generates `IAsyncStateMachine` structs (same mechanism as C#)
- **Generators**: Interpreter uses eager evaluation (collects all yields on first `next()`); compiler generates lazy `IEnumerator` state machines
- **Classes**: Interpreter uses dynamic `SharpTSClass` objects with composition-based inheritance; compiler emits real .NET types with native type hierarchy
- **Enums**: Interpreter creates runtime objects with dictionary lookup; compiler evaluates to compile-time metadata (zero runtime overhead)
- **Decorators**: Interpreter executes decorator functions with full TypeScript semantics; compiler maps to .NET attributes (no runtime execution)
- **Modules**: Interpreter resolves imports dynamically at runtime; compiler generates static types initialized in topological dependency order

These differences are internal optimizations. For most features, your code's behavior is identical between modes. However, **decorators** lose runtime execution capability when compiled (they become static .NET attributes), and **generators** change from eager to lazy evaluation.

---

## Command Reference

### Interpreted Mode

```bash
# Run a TypeScript file
sharpts <file.ts>

# Start REPL
sharpts

# Decorator options (Stage 3 enabled by default)
sharpts <file.ts> --experimentalDecorators    # Legacy (Stage 2) decorators
sharpts <file.ts> --noDecorators              # Disable decorators
sharpts <file.ts> --emitDecoratorMetadata     # Emit type metadata
```

### Compiled Mode

```bash
sharpts --compile <file.ts> [options]
sharpts -c <file.ts> [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `-o <path>` | Set output path (default: `<input>.dll`) |
| `--preserveConstEnums` | Keep const enum declarations in output |
| `--ref-asm` | Emit reference-assembly-compatible output |
| `--sdk-path <path>` | Explicit path to .NET SDK reference assemblies |
| `--verify` | Verify emitted IL using Microsoft.ILVerification |
| `--experimentalDecorators` | Use Legacy (Stage 2) decorators instead of Stage 3 |
| `--noDecorators` | Disable decorator support |
| `--emitDecoratorMetadata` | Emit design-time type metadata |
| `--pack` | Generate NuGet package after compilation |
| `--push <source>` | Push package to NuGet feed (implies `--pack`) |
| `--api-key <key>` | API key for NuGet package push |
| `--package-id <id>` | Override package ID (default: from package.json) |
| `--version <ver>` | Override package version (default: from package.json) |

---

## Common Workflows

### Development Workflow

During development, use interpreted mode for quick iteration:

```bash
# Edit your code
code myapp.ts

# Run immediately
sharpts myapp.ts

# Or use REPL for testing snippets
sharpts
> let x = 5 * 10
> console.log(x)
50
```

### Production Workflow

For production deployment, compile your application:

```bash
# Compile with verification
sharpts --compile myapp.ts --verify

# Test the compiled output
dotnet myapp.dll

# Deploy: copy myapp.dll and myapp.runtimeconfig.json
```

### CI/CD Integration

In CI/CD pipelines, compile and verify:

```bash
# Build step
sharpts --compile src/app.ts -o dist/app.dll --verify

# Test step
dotnet dist/app.dll --run-tests

# Deploy step
cp dist/app.dll dist/app.runtimeconfig.json /deploy/
```

### Mixed Development

Use interpreted mode during development, then compile for release:

```bash
# Development: fast iteration
sharpts myapp.ts

# Before release: compile and verify
sharpts --compile myapp.ts --verify

# Distribution
dotnet myapp.dll
```

---

## Troubleshooting

### Interpreted Mode Issues

**"Error: Cannot find module"**
- Check that import paths are correct relative to the file
- Ensure `.ts` extension is included in imports

**Type errors preventing execution**
- Fix type errors first; interpretation won't proceed with type errors
- Use `any` type temporarily if needed during prototyping

### Compiled Mode Issues

**"Cannot verify IL - SDK reference assemblies not found"**
- Install .NET SDK or specify path with `--sdk-path`
- IL verification is optional; remove `--verify` flag if not needed

**Runtime errors in compiled output**
- Compile with `--verify` to catch IL issues early
- Check that all dependencies are available at runtime

**Missing runtime config**
- Ensure `<name>.runtimeconfig.json` is in same directory as `.dll`
- This file is generated automatically during compilation
