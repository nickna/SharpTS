# Contributing to the SharpTS Standard Library

Files under `stdlib/` are the TypeScript source for Node-compatible modules
baked into SharpTS. They are embedded as resources in `SharpTS.dll` and
compiled into every user output alongside the user's own code.

The resolution/embedding architecture lives in `Modules/Stdlib/`
(`StdlibProviderChain`, `EmbeddedStdlibProvider`); the sources are embedded via
the `stdlib\**\*.ts` `EmbeddedResource` group in `SharpTS.csproj`. (The original
migration plan doc was retired when the migration completed.)

## Authoring contract

Stdlib modules are held to a light contract so future optimizations — tree
shaking, precompilation, cross-run caching — can land without rewrites.

### 1. Pure ESM named exports

```ts
// Good
export function parse(str: string): any { ... }
export const decode = parse;

// Bad — default exports break future tree-shaking
export default { parse, stringify };
```

### 2. No top-level side effects

Don't build lookup tables, register handlers, or initialize global state at
module load. If a module needs initialization, put it behind a function the
first caller invokes.

```ts
// Good
let cache: Map<string, any> | undefined;
function getCache() {
    if (!cache) cache = new Map();
    return cache;
}

// Bad — runs at import, even if the caller never needs it
const cache = new Map();
```

### 3. Primitive imports at the top of the file

All `primitive:*` imports grouped and minimal. A module that imports from
fewer primitives is healthier.

```ts
import { openSync, readSync } from 'primitive:io';
import { cwd } from 'primitive:process';
```

`primitive:*` modules are importable **only from stdlib code** — user code
cannot import them. That enforces Node-compatible semantics from the outside.

### 4. No cross-stdlib imports from leaf modules

A "leaf" module (like `querystring` or `path`) must not import from another
stdlib module. Composite modules (`fs/promises` importing `fs`) are fine, but
migrations must land in dependency order: leaves first, composites second.

### 5. Node semantics are the spec

Target Node.js 24.15.0. Match observable behavior including error codes
(`ENOENT`, `EACCES`, etc.). Any deliberate divergence needs an explicit
comment explaining why — no silent differences.

### 6. Optional params and parameter defaults: write idiomatic TS

Omitted optional parameters and parameter defaults round-trip faithfully
through module imports — including the `$TSFunction.Invoke` value-call /
cross-module path. Write the natural TypeScript; no boundary-specific
workarounds are needed.

```ts
// Optional param: an omitted arg arrives as `undefined`, so `=== undefined`,
// `typeof`, and `!= null` all answer correctly across module boundaries.
export function basename(p: string, ext?: string): string {
    if (ext !== undefined && ext.length > 0) { /* use ext */ }
}

// Defaults of any type fire when the arg is omitted — reference (string,
// object, array) AND value type (number, boolean, bigint).
export function parse(str: string, sep: string = '&', eq: string = '='): any { ... }
export function pad(width: number = 4): number { return width * 2; }
```

**Why this works:** value-type-defaulted params are widened to an `object`
slot, and omitted optional args are padded with the `undefined` sentinel
(not CLR null) for every user-function kind — declarations, arrows,
methods, and async/generator/async-generator functions. The entry prologue
fires the default when the slot holds `undefined`. (Previously, value-type
defaults silently became `0`/`false` and unset optional ref params arrived
as CLR null; both were fixed in #925 — this section retired the old
`!= null` / `param?: T` + `??` workarounds.)

### 7. No SharpTS-specific APIs

Shim code should be legal Node.js as far as syntax and semantics go.
`console.log`, `JSON.*`, `Math.*`, `Array.*`, `Object.*`, standard globals
— all fair game. Anything that only works in SharpTS (or only exists in
SharpTS) is out.

## Specifier → file convention

The module specifier maps 1:1 to a file path under `stdlib/node/`:

| Specifier       | File                                |
|-----------------|-------------------------------------|
| `querystring`   | `stdlib/node/querystring.ts`        |
| `fs`            | `stdlib/node/fs.ts`                 |
| `fs/promises`   | `stdlib/node/fs/promises.ts`        |
| `node:fs`       | (same as `fs` — prefix is stripped) |

When you add a new module, add the `.ts` file. The embedded resource
discovery and specifier routing are automatic via convention.

## Adding a new module

1. Write the TS source under `stdlib/node/<name>.ts` following the contract.
2. Remove the module's entry from `Runtime/BuiltIns/Modules/BuiltInModuleRegistry.cs`
   (this is how the C# fallback provider stops claiming the specifier).
3. Delete the legacy C# interpreter at
   `Runtime/BuiltIns/Modules/Interpreter/<Name>ModuleInterpreter.cs`.
4. Delete the legacy compiler emitter at
   `Compilation/Emitters/Modules/<Name>ModuleEmitter.cs` and any
   `Compilation/RuntimeEmitter.<Name>*.cs` helpers.
5. Remove the module from the dispatch switch in
   `Runtime/BuiltIns/Modules/Interpreter/BuiltInModuleValues.cs` and
   `HasInterpreterSupport`.
6. Remove the module's `GetXxxModuleTypes` method and switch entry in
   `TypeSystem/BuiltInModuleTypes.cs` — types now flow from the TS source.
7. Remove the `Register(new XxxModuleEmitter())` line from
   `Compilation/ILCompiler.cs`.
8. Add test fixtures under `SharpTS.Tests/SharedTests/BuiltInModules/` that
   exercise the stdlib module through both interpreter and compiler modes.
9. Verify the standalone-DLL test still passes (no new reflection).

## Testing

Every stdlib module should have both:

- **Behavior tests**: `SharpTS.Tests/SharedTests/BuiltInModules/<Name>ModuleTests.cs`
  exercise the public API through both execution modes, asserting
  Node-compatible output.
- **Standalone tests**: `SharpTS.Tests/CompilerTests/StandaloneDllTests.cs`
  verify the compiled output runs without `SharpTS.dll` on disk.

When a future migration touches a primitive, add **primitive unit tests**
(C#) in addition to the behavior tests, so failures localize to one of
three layers: shim bug, compiler bug, primitive bug.
