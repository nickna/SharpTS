# Execution Modes: Interpreted vs Compiled

SharpTS offers two execution modes for running TypeScript code: **interpreted** (default) and **compiled** (ahead-of-time IL compilation). This guide helps you understand when to use each mode.

## Quick Comparison

| Aspect | Interpreted | Compiled |
|--------|-------------|----------|
| Command | `dotnet run -- file.ts` | `dotnet run -- --compile file.ts` |
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
dotnet run -- myapp.ts

# Start interactive REPL
dotnet run
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
dotnet run -- --compile myapp.ts
# Output: myapp.dll, myapp.runtimeconfig.json

# Custom output path
dotnet run -- --compile myapp.ts -o build/app.dll

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
| Variables and types | Yes | Yes |
| Functions and closures | Yes | Yes |
| Classes and inheritance | Yes | Yes |
| Interfaces | Yes | Yes |
| Async/await | Yes | Yes (native state machines) |
| Generators | Yes | Yes (native state machines) |
| Modules (import/export) | Yes | Yes |
| Decorators | Yes | Yes |
| Enums | Yes | Yes |
| Template literals | Yes | Yes |
| Spread/rest operators | Yes | Yes |
| Optional chaining (?.) | Yes | Yes |
| Nullish coalescing (??) | Yes | Yes |

### Implementation Differences

While both modes support the same features, the implementation differs:

- **Closures**: Interpreter uses environment chain; compiler generates display classes
- **Async/await**: Interpreter uses exception-based unwinding; compiler generates IL state machines
- **Generators**: Interpreter walks tree with suspension; compiler generates IL state machines

These differences are internal and don't affect your code's behavior.

---

## Decision Guide

Use this flowchart to choose the right mode:

```
Start
  |
  v
Are you developing/debugging?
  |
  +-- Yes --> Use INTERPRETED mode
  |
  +-- No
       |
       v
     Do you need an interactive REPL?
       |
       +-- Yes --> Use INTERPRETED mode
       |
       +-- No
            |
            v
          Is this for production/distribution?
            |
            +-- Yes --> Use COMPILED mode
            |
            +-- No
                 |
                 v
               Is performance critical?
                 |
                 +-- Yes --> Use COMPILED mode
                 |
                 +-- No --> Either works (use interpreted for convenience)
```

### Recommendations by Use Case

| Use Case | Recommended Mode |
|----------|------------------|
| Learning TypeScript | Interpreted (REPL) |
| Building a CLI tool | Compiled |
| Web server | Compiled |
| One-off scripts | Interpreted |
| Testing new features | Interpreted |
| CI/CD builds | Compiled |
| Sharing with team | Compiled |
| Quick calculations | Interpreted (REPL) |

---

## Command Reference

### Interpreted Mode

```bash
# Run a TypeScript file
dotnet run -- <file.ts>

# Start REPL
dotnet run

# With decorator support
dotnet run -- <file.ts> --experimentalDecorators    # Stage 2 decorators
dotnet run -- <file.ts> --decorators                # Stage 3 decorators
dotnet run -- <file.ts> --emitDecoratorMetadata     # Emit type metadata
```

### Compiled Mode

```bash
dotnet run -- --compile <file.ts> [options]
dotnet run -- -c <file.ts> [options]
```

**Options:**

| Option | Description |
|--------|-------------|
| `-o <path>` | Set output path (default: `<input>.dll`) |
| `--preserveConstEnums` | Keep const enum declarations in output |
| `--ref-asm` | Emit reference-assembly-compatible output |
| `--sdk-path <path>` | Explicit path to .NET SDK reference assemblies |
| `--verify` | Verify emitted IL using Microsoft.ILVerification |
| `--experimentalDecorators` | Enable Stage 2 decorators |
| `--decorators` | Enable Stage 3 decorators |
| `--emitDecoratorMetadata` | Emit design-time type metadata |

---

## Common Workflows

### Development Workflow

During development, use interpreted mode for quick iteration:

```bash
# Edit your code
code myapp.ts

# Run immediately
dotnet run -- myapp.ts

# Or use REPL for testing snippets
dotnet run
> let x = 5 * 10
> console.log(x)
50
```

### Production Workflow

For production deployment, compile your application:

```bash
# Compile with verification
dotnet run -- --compile myapp.ts --verify

# Test the compiled output
dotnet myapp.dll

# Deploy: copy myapp.dll and myapp.runtimeconfig.json
```

### CI/CD Integration

In CI/CD pipelines, compile and verify:

```bash
# Build step
dotnet run -- --compile src/app.ts -o dist/app.dll --verify

# Test step
dotnet dist/app.dll --run-tests

# Deploy step
cp dist/app.dll dist/app.runtimeconfig.json /deploy/
```

### Mixed Development

Use interpreted mode during development, then compile for release:

```bash
# Development: fast iteration
dotnet run -- myapp.ts

# Before release: compile and verify
dotnet run -- --compile myapp.ts --verify

# Distribution
dotnet myapp.dll
```

---

## Performance Considerations

**Compiled mode is faster for:**
- Long-running applications
- Computation-heavy workloads
- Production servers
- Repeated execution

**Interpreted mode is faster for:**
- Single-run scripts (no compilation overhead)
- Very small programs
- Interactive exploration

**General guidance:**
- If your script runs for more than a few seconds, compilation pays off
- If you're running the same code multiple times, compile it once
- For quick one-off tasks, interpretation is more convenient

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
