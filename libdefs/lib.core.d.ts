// Ambient lib type declarations loaded by LibTypeLoader (#99 increment 2). Embedded as a resource
// so it is available at runtime — the full vendored TypeScript lib.d.ts is a dev-only submodule.
// A faithful (member types simplified to `symbol` for now) subset, expanded toward the real
// external/typescript/src/lib/*.d.ts in later #99 increments.

// The primitive WRAPPER object types, as they appear in `implements`/`extends` position
// (`class C implements String`, `interface I extends String`). Modeled member-less for now — enough
// to be a valid implements/extends target (a class/interface trivially satisfies an empty base);
// a later increment fills their real methods once the string/number/boolean primitives model their
// apparent members so the primitive can satisfy the wrapper.
interface String { }
interface Number { }
interface Boolean { }

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
