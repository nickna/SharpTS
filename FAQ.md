# FAQ

## What is the end goal?

Enable TypeScript code to be used in .NET projects and vice versa.
With .NET as the execution engine for TypeScript, you can now use .NET libraries from TypeScript if you use SharpTS.

## When should I use the IL compiler vs. the interpreter?

The interpreter is significantly slower but it can be useful for quickly running short scripts, or code that is expected to change often.
The IL compiler (`--compile`) produces a standalone .NET DLL that runs at native .NET speed, making it ideal for production use.

## Are the IL compiler and interpreter equal in feature parity?

At the time of writing, yes. The interpreter uses more stack memory because it traverses the AST recursively using the call stack, creating new `RuntimeEnvironment` scope objects for each block. The IL compiler generates flat .NET IL using local variables and the evaluation stack.

Due to time constraints, the interpreter will drift as we prioritize IL performance.

## When you say TypeScript, what do you mean?

We support the static type system and syntax of TypeScript, not a JavaScript runtime. This means:

- Full type annotations, interfaces, generics, union/intersection types, and type aliases are supported
- Classes with inheritance, abstract members, access modifiers, and decorators work as expected
- Async/await and Promises are being implemented

See STATUS.md for a detailed compatibility matrix. Note that we parse `.ts` files directly rather than transpiling to JavaScript first.

## Why not just support JavaScript or ECMAScript?

SharpTS does run JavaScript since TypeScript is a superset of JavaScript. However, targeting TypeScript directly offers advantages:

1. **Type information enables better compilation** - Static types allow the IL compiler to emit more efficient .NET code and catch errors earlier
2. **No transpilation step** - TypeScript is parsed and executed/compiled directly, avoiding the overhead of generating intermediate JavaScript
3. **Tighter .NET integration** - Type annotations can map to .NET types, enabling seamless interop with .NET libraries

**Intentional trade-offs we make:**

1. **Strict equality only** - `===` and `!==` behave the same as `==` and `!=`. We don't implement JavaScript's loose equality coercion rules (e.g., `"1" == 1` is `false`, not `true`)
2. **No hoisting** - Variables must be declared before use. JavaScript's `var` hoisting behavior is not replicated
3. **typeof null** - Currently returns an incorrect value (known bug). TypeScript/JavaScript returns `"object"`, but this edge case isn't prioritized

## How much TypeScript is supported?

Check the STATUS.md for an updated list of compatibility.

## What about Node support?

This hasn't started yet. The goal is to get to ~70% Node compatibility by supporting the most commonly used functions.
This will enable a wide swath of projects to run with little to no modifications. Full Node compatibility isn't a goal.
