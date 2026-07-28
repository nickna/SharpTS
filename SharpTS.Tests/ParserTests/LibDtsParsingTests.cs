using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.ParserTests;

/// <summary>
/// Parser coverage for dense `.d.ts` constructs that the bundled TypeScript lib files use
/// (es5.d.ts / es2015.core.d.ts now parse clean). Part of #99 Phase A (parser hardening).
/// </summary>
public class LibDtsParsingTests
{
    private static System.Collections.Generic.List<Stmt> Parse(string source)
    {
        var parseResult = new Parser(new Lexer(source).ScanTokens()).Parse();
        Assert.True(parseResult.IsSuccess,
            parseResult.Diagnostics.Count > 0 ? parseResult.Diagnostics[0].ToString() : "parse failed");
        return parseResult.Statements;
    }

    [Fact]
    public void Interface_NamedAfterBuiltinSymbol_Parses()
    {
        // lib.d.ts declares `interface Symbol { ... }` / `interface BigInt { ... }`.
        Assert.NotEmpty(Parse("interface Symbol { toString(): string; }"));
        Assert.NotEmpty(Parse("interface BigInt { valueOf(): bigint; }"));
    }

    [Fact]
    public void IndexSignature_WithTypeAliasKey_Parses()
    {
        // `[key: PropertyKey]: T` — a non-primitive (type-alias) index key.
        Assert.NotEmpty(Parse("interface M { [key: PropertyKey]: number; }"));
    }

    [Fact]
    public void Method_WithThisParameterAndRest_Parses()
    {
        // `call(this: Function, thisArg: any, ...argArray: any[]): any`.
        Assert.NotEmpty(Parse("interface F { call(this: Function, thisArg: any, ...argArray: any[]): any; }"));
    }

    [Fact]
    public void Method_WithRestBindingPattern_Parses()
    {
        // TypeScript 6's iterator declarations use a destructured rest parameter.
        Assert.NotEmpty(Parse(
            "interface I<TNext> { next(...[value]: [] | [TNext]): IteratorResult<TNext>; }"));
    }

    [Fact]
    public void ReadonlyIndexSignature_Parses()
    {
        Assert.NotEmpty(Parse("interface S { readonly [index: number]: string; }"));
    }

    [Fact]
    public void ReadonlyArrayType_Parses()
    {
        Assert.NotEmpty(Parse("interface T { raw: readonly string[]; }"));
    }

    [Fact]
    public void ReadonlyArray_InsideUnion_Parses()
    {
        // `readonly` must bind to the array within a union member.
        Assert.NotEmpty(Parse("interface T { raw: readonly string[] | ArrayLike<string>; }"));
    }

    [Fact]
    public void ThisTypePredicate_Parses()
    {
        // `): this is readonly S[]` — a `this` type predicate.
        Assert.NotEmpty(Parse("interface A<T> { every<S extends T>(p: (v: T) => v is S): this is readonly S[]; }"));
    }

    [Fact]
    public void QualifiedTypeName_Parses()
    {
        // `Intl.CollatorOptions` — a namespace-qualified type reference.
        Assert.NotEmpty(Parse("interface S { m(c?: Intl.CollatorOptions): number; }"));
    }

    [Fact]
    public void ComputedSymbolMethodName_Parses()
    {
        // `[Symbol.iterator](): Iterator<T>` — computed member name via a well-known symbol.
        Assert.NotEmpty(Parse("interface I<T> { [Symbol.iterator](): Iterator<T>; }"));
    }

    [Fact]
    public void ComputedSymbolPropertyName_ReadonlyAndLiteral_Parses()
    {
        Assert.NotEmpty(Parse("interface B { readonly [Symbol.toStringTag]: \"BigInt\"; }"));
    }

    [Fact]
    public void ReadonlyTuple_InGenericArgument_Parses()
    {
        // `Iterable<readonly [K, V]>` — a readonly tuple as a generic type argument.
        Assert.NotEmpty(Parse("interface I<K, V> { new (iterable?: Iterable<readonly [K, V]> | null): Map<K, V>; }"));
    }

    [Fact]
    public void ContextualKeywordParameterName_InFunctionType_Parses()
    {
        // `set: Set<T>` — a function-type parameter named with a contextual keyword.
        Assert.NotEmpty(Parse("interface S<T> { forEach(cb: (value: T, set: Set<T>) => void): void; }"));
    }

    [Fact]
    public void ComputedSymbolName_InsideInlineObjectType_Parses()
    {
        // `{ [Symbol.match](string: string): T }` — a computed member name inside an inline object type.
        Assert.NotEmpty(Parse("interface R { match(m: { [Symbol.match](string: string): string; }): string; }"));
    }

    [Fact]
    public void SymbolAsTypeName_Parses()
    {
        Assert.NotEmpty(Parse("interface SymbolConstructor { readonly prototype: Symbol; }"));
    }
}
