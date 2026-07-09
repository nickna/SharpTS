// Ambient lib type declarations loaded by LibTypeLoader (#99 increment 2). Embedded as a resource
// so it is available at runtime — the full vendored TypeScript lib.d.ts is a dev-only submodule.
// A faithful (member types simplified to `symbol` for now) subset, expanded toward the real
// external/typescript/src/lib/*.d.ts in later #99 increments.

interface SymbolConstructor {
    readonly iterator: symbol;
    readonly asyncIterator: symbol;
    readonly hasInstance: symbol;
    readonly isConcatSpreadable: symbol;
    readonly match: symbol;
    readonly matchAll: symbol;
    readonly replace: symbol;
    readonly search: symbol;
    readonly species: symbol;
    readonly split: symbol;
    readonly toPrimitive: symbol;
    readonly toStringTag: symbol;
    readonly unscopables: symbol;
    for(key: string): symbol;
    keyFor(sym: symbol): string | undefined;
}
