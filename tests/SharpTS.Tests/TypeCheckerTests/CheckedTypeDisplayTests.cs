using System.Collections.Frozen;
using System.Globalization;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public class CheckedTypeDisplayTests
{
    public static IEnumerable<object[]> GoldenDisplays()
    {
        TypeInfo number = TypeInfo.Primitive.Number, text = TypeInfo.String.Shared;
        foreach (var (kind, expected) in new (Type, string)[]
        {
            (typeof(TypeInfo.Void), "void"), (typeof(TypeInfo.Any), "any"), (typeof(TypeInfo.Null), "null"),
            (typeof(TypeInfo.Undefined), "undefined"), (typeof(TypeInfo.Unknown), "unknown"), (typeof(TypeInfo.Never), "never"),
            (typeof(TypeInfo.Symbol), "symbol"),
            (typeof(TypeInfo.BigInt), "bigint"), (typeof(TypeInfo.Object), "object"),
            (typeof(TypeInfo.Date), "Date"), (typeof(TypeInfo.RegExp), "RegExp"), (typeof(TypeInfo.Timeout), "NodeJS.Timeout"),
            (typeof(TypeInfo.Buffer), "Buffer"), (typeof(TypeInfo.EventEmitter), "EventEmitter"),
            (typeof(TypeInfo.AbortController), "AbortController"), (typeof(TypeInfo.AbortSignal), "AbortSignal"),
            (typeof(TypeInfo.Worker), "Worker"), (typeof(TypeInfo.MessagePort), "MessagePort"),
            (typeof(TypeInfo.MessageChannel), "MessageChannel"),
            (typeof(TypeInfo.AtomicsNamespace), "typeof Atomics")
        }) yield return [Activator.CreateInstance(kind)!, expected];
        yield return [new TypeInfo.UniqueSymbol("declaration"), "unique symbol"];
        yield return [new TypeInfo.SharedArrayBuffer(), "SharedArrayBuffer"];
        yield return [new TypeInfo.ArrayBuffer(), "ArrayBuffer"];
        yield return [new TypeInfo.DataView(), "DataView"];
        yield return [number, "number"];
        yield return [TypeInfo.Primitive.Boolean, "boolean"];
        yield return [text, "string"];
        yield return [new TypeInfo.StringLiteral("a\"b"), "\"a\\\"b\""];
        yield return [new TypeInfo.NumberLiteral(1.25), "1.25"];
        yield return [new TypeInfo.BooleanLiteral(false), "false"];
        yield return [new TypeInfo.BigIntLiteral(123), "123n"];
        yield return [new TypeInfo.Array(number), "number[]"];
        yield return [new TypeInfo.Array(number, IsReadonly: true), "readonly number[]"];
        yield return [new TypeInfo.Tuple([new(number, TupleElementKind.Required, "first"), new(text, TupleElementKind.Optional, "second")], 1, IsReadonly: true), "readonly [first: number, second?: string]"];
        yield return [new TypeInfo.Tuple([], 0, new TypeInfo.Union([text, number])), "[...(string | number)[]]"];
        yield return [new TypeInfo.Map(text, number), "Map<string, number>"];
        yield return [new TypeInfo.Set(number), "Set<number>"];
        yield return [new TypeInfo.WeakMap(text, number), "WeakMap<string, number>"];
        yield return [new TypeInfo.WeakSet(number), "WeakSet<number>"];
        yield return [new TypeInfo.WeakRef(number), "WeakRef<number>"];
        yield return [new TypeInfo.FinalizationRegistry(number), "FinalizationRegistry<number>"];
        yield return [new TypeInfo.Iterator(number), "IterableIterator<number>"];
        yield return [new TypeInfo.Iterable(number), "Iterable<number>"];
        yield return [new TypeInfo.AsyncIterator(number), "AsyncIterableIterator<number>"];
        yield return [new TypeInfo.AsyncIterable(number), "AsyncIterable<number>"];
        yield return [new TypeInfo.Promise(number), "Promise<number>"];
        yield return [new TypeInfo.Generator(text) { ReturnType = number, NextType = TypeInfo.Primitive.Boolean }, "Generator<string, number, boolean>"];
        yield return [new TypeInfo.AsyncGenerator(text) { ReturnType = number, NextType = TypeInfo.Primitive.Boolean }, "AsyncGenerator<string, number, boolean>"];
        yield return [new TypeInfo.Function([number], text, 0, ParamNames: ["input"]), "(input?: number) => string"];
        yield return [new TypeInfo.FunctionSupertype(), "Function"];
        var parameter = new TypeInfo.TypeParameter("T", text, text);
        yield return [new TypeInfo.GenericFunction([parameter], [parameter], parameter), "<T extends string = string>(arg0: T) => T"];
        yield return [new TypeInfo.CallSignature(null, [number], text), "(arg0: number): string"];
        yield return [new TypeInfo.OverloadSet([new TypeInfo.Function([number], text), new TypeInfo.GenericFunction([parameter], [parameter], parameter)]),
            "{ (arg0: number): string; <T extends string = string>(arg0: T): T; }"];
        yield return [new TypeInfo.ConstructorSignature(null, [number], text), "new (arg0: number) => string"];
        yield return [new TypeInfo.Record(new Dictionary<string, TypeInfo> { ["z"] = number, ["a"] = text }.ToFrozenDictionary(), OptionalFields: new[] { "z" }.ToFrozenSet()), "{ a: string; z?: number; }"];
        yield return [new TypeInfo.Union([text, number]), "string | number"];
        yield return [new TypeInfo.Intersection([text, number]), "string & number"];
        yield return [new TypeInfo.SpreadType(new TypeInfo.Array(number)), "...number[]"];
        yield return [new TypeInfo.KeyOf(parameter), "keyof T"];
        yield return [new TypeInfo.TypeOf("value"), "typeof value"];
        yield return [new TypeInfo.IndexedAccess(parameter, text), "T[string]"];
        yield return [new TypeInfo.ConditionalType(parameter, text, text, number), "T extends string ? string : number"];
        yield return [new TypeInfo.MappedType("K", new TypeInfo.KeyOf(parameter), text, MappedTypeModifiers.AddOptional), "{ [K in keyof T]+?: string }"];
        yield return [new TypeInfo.TemplateLiteralType(["before", "after"], [text]), "`before${string}after`"];
        yield return [new TypeInfo.IntrinsicStringType(StringManipulation.Uppercase, text), "Uppercase<string>"];
        yield return [new TypeInfo.TypePredicate("value", text), "value is string"];
        yield return [new TypeInfo.AssertsNonNull("value"), "asserts value"];
        yield return [new TypeInfo.InferredTypeParameter("T", text), "infer T extends string"];
        yield return [new TypeInfo.RecursiveTypeAlias("Alias", [text]), "Alias<string>"];
        yield return [new TypeInfo.TypedArray("Uint8"), "Uint8Array"];
        yield return [new TypeInfo.Error("RangeError"), "RangeError"];
        var emptyTypes = FrozenDictionary<string, TypeInfo>.Empty;
        var emptyAccess = FrozenDictionary<string, AccessModifier>.Empty;
        var core = new ClassMetadataCore("Box", null, emptyTypes, emptyTypes, emptyTypes, emptyAccess,
            emptyAccess, FrozenSet<string>.Empty, emptyTypes, emptyTypes, emptyTypes);
        var classType = new TypeInfo.Class(core);
        yield return [classType, "typeof Box"];
        yield return [new TypeInfo.MutableClass("Box"), "typeof Box"];
        yield return [new TypeInfo.GenericClass(core, [parameter]), "typeof Box"];
        yield return [new TypeInfo.Instance(classType), "Box"];
        yield return [new TypeInfo.Interface("Shape", emptyTypes, FrozenSet<string>.Empty), "Shape"];
        var genericInterface = new TypeInfo.GenericInterface("Shape", [parameter], emptyTypes, FrozenSet<string>.Empty);
        yield return [genericInterface, "Shape"];
        yield return [new TypeInfo.InstantiatedGeneric(genericInterface, [text]), "Shape<string>"];
        yield return [new TypeInfo.Enum("Choice", FrozenDictionary<string, object>.Empty, EnumKind.Numeric), "Choice"];
        yield return [new TypeInfo.Namespace("Scope", emptyTypes, emptyTypes), "typeof Scope"];
    }

    [Theory]
    [MemberData(nameof(GoldenDisplays))]
    public void GoldenDisplayPreservesTheCheckedType(TypeInfo type, string expected)
    {
        var actual = CheckedTypeDisplay.Render(type);
        Assert.Null(actual.Unsupported);
        Assert.Equal(expected, actual.Text);
    }

    [Fact]
    public void OverloadsRetainPublicSignaturesAndNestedFunctionArrows()
    {
        var nested = new TypeInfo.Function([TypeInfo.Primitive.Number], TypeInfo.String.Shared);
        var signature = new TypeInfo.Function([nested], nested);
        var implementation = new TypeInfo.Function([TypeInfo.Any.Shared], TypeInfo.Any.Shared);
        Assert.Equal("{ (arg0: (arg0: number) => string): (arg0: number) => string; }",
            CheckedTypeDisplay.Render(new TypeInfo.OverloadedFunction([signature], implementation)).Text);
        var parameter = new TypeInfo.TypeParameter("T");
        Assert.Equal("{ <T>(arg0: T): T; }", CheckedTypeDisplay.Render(new TypeInfo.GenericOverloadedFunction(
            [parameter], [new TypeInfo.Function([parameter], parameter)], implementation)).Text);
    }

    [Fact]
    public void FormattingIsInvariantAndNeverTruncatesOrHidesCycles()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1.25", CheckedTypeDisplay.Render(new TypeInfo.NumberLiteral(1.25)).Text);
            Assert.NotNull(CheckedTypeDisplay.Render(new TypeInfo.NumberLiteral(double.NaN)).Unsupported);
            Assert.NotNull(CheckedTypeDisplay.Render(new TypeInfo.Intersection([])).Unsupported);
            Assert.NotNull(CheckedTypeDisplay.Render(new TypeInfo.Union([])).Unsupported);
            Assert.NotNull(CheckedTypeDisplay.Render(new TypeInfo.Interface("", FrozenDictionary<string, TypeInfo>.Empty,
                FrozenSet<string>.Empty)).Unsupported);
            string value = new('x', 2048);
            Assert.Equal('"' + value + '"', CheckedTypeDisplay.Render(new TypeInfo.StringLiteral(value)).Text);
            var constituents = new List<TypeInfo>();
            var cycle = new TypeInfo.Union(constituents);
            constituents.Add(new TypeInfo.Array(cycle));
            Assert.NotNull(CheckedTypeDisplay.Render(cycle).Unsupported);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
