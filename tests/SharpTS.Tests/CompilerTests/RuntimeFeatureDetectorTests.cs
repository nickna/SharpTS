using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class RuntimeFeatureDetectorTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("const observed: number = items.push({ key: 2, tag: 'y' });", true)]
    [InlineData("Object.defineProperty([], '0', { value: 1 });", false)]
    [InlineData("const prototype = Array.prototype;", false)]
    public void StableDiscardedArrayPush_TracksCompactLiteralEligibility(
        string extra,
        bool expectedEligible)
    {
        string source = $$"""
            type Item = { key: number; tag: string };
            const items: Item[] = [];
            items.push({ key: 1, tag: "x" });
            {{extra}}
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(
            expectedEligible,
            features.CompactObjectRecordStablePushLiterals.Count == 1);
        Assert.Equal(
            expectedEligible,
            features.CompactObjectRecordStablePushShapes.Count == 1);
        var shape = features.CompactObjectRecordShapes.Single(pair =>
            pair.Value.Fields.Any(field => field.Key == "key"));
        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(shape.Key));
    }

    [Theory]
    [InlineData("const value = { x: 1 }; JSON.stringify(value);", 1)]
    [InlineData("const value = { x: 1 }; JSON.stringify(value, null, 2);", 0)]
    [InlineData("const value = { x: 1 }; const stringify = JSON.stringify; stringify(value);", 0)]
    public void CollectsJsonScalarRecordsOnlyForDirectOneArgumentCalls(
        string source,
        int expectedShapeCount)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expectedShapeCount, features.JsonScalarRecordShapeFingerprints.Count);
    }

    [Fact]
    public void CollectsNestedClosedJsonScalarRecords()
    {
        const string source = """
            const items: { i: number; s: string; v: boolean }[] = [];
            items.push({ i: 1, s: "x", v: true });
            const root = { items };
            JSON.stringify(root);
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(2, features.JsonScalarRecordShapeFingerprints.Count);
    }

    [Theory]
    [InlineData("async function f() { for await (const x of [1]) {} }", true)]
    [InlineData("function f() { for (const x of [1]) {} }", false)]
    public void DetectsForAwaitOfAdapterRequirement(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesForAwaitOf);
    }

    [Theory]
    [InlineData("Object.defineProperty([], '0', { get() { return 1; } });", true)]
    [InlineData("Object.defineProperties([], {});", true)]
    [InlineData("Object.create(null, {});", true)]
    [InlineData("Reflect.defineProperty([], '0', { value: 1 });", true)]
    [InlineData("const define = Object.defineProperty; define([], '0', { value: 1 });", true)]
    [InlineData("Object['defineProperty']([], '0', { value: 1 });", true)]
    [InlineData("Object.keys([]);", false)]
    public void DetectsDynamicPropertyDescriptorUsage(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesDynamicPropertyDescriptors);
    }

    [Theory]
    [InlineData("const value = { x: 1 }; value.x = 2;", false)]
    [InlineData("Object.freeze({ x: 1 });", true)]
    [InlineData("const O = Object; O.seal({ x: 1 });", true)]
    [InlineData("Reflect.preventExtensions({ x: 1 });", true)]
    [InlineData("globalThis.Object.freeze({ x: 1 });", true)]
    [InlineData("this.Object.freeze({ x: 1 });", true)]
    [InlineData("eval('Object.freeze({ x: 1 })');", true)]
    public void DetectsObjectIntegrityMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesObjectIntegrityMutation);
    }

    [Theory]
    [InlineData("class Counter { step(): number { return 1; } } new Counter().step();", false)]
    [InlineData("class Counter { step(): number { return 1; } } const p = Counter.prototype;", true)]
    [InlineData("class Counter { step(): number { return 1; } } const c = new Counter(); c.step = () => 2;", true)]
    [InlineData("const value = ({} as any).__proto__;", true)]
    public void DetectsClassPrototypeMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expected, features.UsesClassPrototypeMutation);
    }

    [Theory]
    [InlineData("Date.prototype.toString = Object.prototype.toString;", true)]
    [InlineData("Date.prototype['valueOf'] = function() { return 0; };", true)]
    [InlineData("Object.defineProperty(Date.prototype, 'x', { value: 1 });", true)]
    [InlineData("const prototype = Date.prototype;", true)]
    [InlineData("const d = new Date(0); (d as any).getTime = function() { return 1; };", true)]
    [InlineData("const d = new Date(0); const alias: any = d; alias.setTime = function() { return 1; };", true)]
    [InlineData("const d = new Date(0); Object.defineProperty(d, 'getTime', { value: function() { return 1; } });", true)]
    [InlineData("const d = new Date(0); Object.getPrototypeOf(d);", true)]
    [InlineData("new Date().toString();", false)]
    public void DetectsDateMethodMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expected, features.UsesDatePrototypeMutation);
    }

    [Fact]
    public void DateTypedParameterMethodRequiresDateRuntimeWithoutDisablingFastPath()
    {
        const string source = "function read(d: Date): number { return d.getTime(); }";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);

        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.True(features.UsesDate);
        Assert.False(features.UsesDatePrototypeMutation);
    }

    [Theory]
    [InlineData("const p = Promise.resolve(1); p.then(x => x + 1);", false)]
    [InlineData("const prototype = Promise.prototype;", true)]
    [InlineData("const p: any = Promise.resolve(1); p.then = () => Promise.resolve(2);", true)]
    [InlineData("Promise[Symbol.species] = Promise;", true)]
    [InlineData("Object.defineProperty({}, 'x', { value: 1 });", true)]
    [InlineData("eval('void 0');", true)]
    public void DetectsPromisePrototypeMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expected, features.UsesPromisePrototypeMutation);
    }

    [Theory]
    [InlineData("const p = Array.prototype;", true)]
    [InlineData("Object.setPrototypeOf({}, null);", true)]
    [InlineData("Reflect.setPrototypeOf({}, null);", true)]
    [InlineData("const setProto = Object.setPrototypeOf;", true)]
    [InlineData("const setProto = Reflect['setPrototypeOf'];", true)]
    [InlineData("const value = ([] as any).__proto__;", true)]
    [InlineData("const values: object[] = []; (values as any).push = () => 0;", true)]
    [InlineData("const values: object[] = []; delete (values as any)['push'];", true)]
    [InlineData("Reflect.set([], 'push', () => 0);", true)]
    [InlineData("Object.assign([], { push: () => 0 });", true)]
    [InlineData("const values: number[] = []; values.push(1);", false)]
    public void DetectsArrayPrototypeMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesArrayPrototypeMutation);
    }

    [Theory]
    [InlineData("const value: number = 1; value.toString();", false)]
    [InlineData("const prototype = Number.prototype;", true)]
    [InlineData("Number.prototype.toString = function() { return 'x'; };", true)]
    [InlineData("Number.prototype['toFixed'] = function() { return 'x'; };", true)]
    [InlineData("const N = Number; N.prototype.toString = function() { return 'x'; };", true)]
    [InlineData("const p = Number.prototype; p.toFixed = function() { return 'x'; };", true)]
    [InlineData("Object.defineProperty(Number.prototype, 'x', { value: 1 });", true)]
    [InlineData("eval('void 0');", true)]
    public void DetectsNumberPrototypeMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expected, features.UsesNumberPrototypeMutation);
    }

    [Theory]
    [InlineData("/^[a-z]+$/.test('abc');", false)]
    [InlineData("const prototype = RegExp.prototype;", true)]
    [InlineData("RegExp.prototype.test = function() { return false; };", true)]
    [InlineData("Object.defineProperty(RegExp.prototype, 'exec', { value: null });", true)]
    [InlineData("const prototype = Object.getPrototypeOf(/x/);", true)]
    [InlineData("const prototype = (/x/ as any).__proto__;", true)]
    [InlineData("const dynamicFunction = Function;", true)]
    [InlineData("globalThis.RegExp = RegExp;", true)]
    [InlineData("eval('void 0');", true)]
    public void DetectsRegExpPrototypeMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expected, features.UsesRegExpPrototypeMutation);
    }

    [Theory]
    [InlineData("parseInt('10', 10);", false)]
    [InlineData("globalThis.parseInt = function() { return 1; };", true)]
    [InlineData("globalThis['parseInt'] = function() { return 1; };", true)]
    [InlineData("parseInt = function() { return 1; };", true)]
    [InlineData("eval('void 0');", true)]
    public void DetectsGlobalParseIntMutationRisk(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);

        Assert.Equal(expected, features.UsesGlobalParseIntMutation);
    }

    [Theory]
    [InlineData("const node: { left: object | null; right: object | null } = { left: null, right: null }; const isLeaf = node.left === null; console.log(isLeaf);", true)]
    [InlineData("const node: { left: object | null; right: object | null } = { left: null, right: null }; node.left = null;", false)]
    [InlineData("export const node: { left: object | null; right: object | null } = { left: null, right: null };", false)]
    [InlineData("const options: { month: string; day: string } = { month: 'long', day: 'numeric' }; new Intl.DateTimeFormat('en-US', options);", false)]
    [InlineData("const node: { left: object | null; right: object | null } = { left: null, right: null }; const clone = { ...node };", false)]
    [InlineData("console.log(({ left: null, right: null } as any));", false)]
    public void ProvesCompactRecordShapeStableOnlyWithoutMutationOrEscape(
        string source, bool expectedStable)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);
        var shape = Assert.Single(features.CompactObjectRecordShapes);

        bool actualStable = features.CanAssumeCompactObjectRecordIsUnmaterialized(shape.Key);
        Assert.True(actualStable == expectedStable,
            $"expectedStable={expectedStable}; actualStable={actualStable}; " +
            $"unknown={features.PotentiallyMaterializesUnknownCompactObjectRecordShape}; " +
            $"mutable=[{string.Join(",", features.PotentiallyMaterializedCompactObjectRecordShapes)}]; " +
            $"shape={shape.Key}");
    }

    [Theory]
    [InlineData("""
        type Node = { left: Node | null; right: Node | null };
        function consume(node: Node): void { const isLeaf = node.left === null; }
        const node: Node = { left: null, right: null };
        consume(node);
        """, true)]
    [InlineData("""
        type Node = { left: Node | null; right: Node | null };
        function consume(node: Node): void { const isLeaf = node.left === null; }
        function invoke(consume: (node: Node) => void): void {
            const node: Node = { left: null, right: null };
            consume(node);
        }
        """, false)]
    [InlineData("""
        type Node = { left: Node | null; right: Node | null };
        function consume(node: any): void { console.log(node); }
        const node: Node = { left: null, right: null };
        consume(node);
        """, false)]
    public void SourceCallProofRejectsShadowedFunctionBindings(
        string source, bool expectedStable)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);
        var shape = Assert.Single(features.CompactObjectRecordShapes);

        Assert.Equal(
            expectedStable,
            features.CanAssumeCompactObjectRecordIsUnmaterialized(shape.Key));
    }

    [Fact]
    public void EscapeProofRejectsWideningThroughAnyBinding()
    {
        const string source = """
            type Node = { left: Node | null; right: Node | null };
            const node: Node = { left: null, right: null };
            const escaped: any = node;
            console.log(escaped);
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);
        var shape = Assert.Single(features.CompactObjectRecordShapes);

        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(shape.Key));
    }

    [Fact]
    public void EscapeProofRejectsContextuallyAnyObjectLiteral()
    {
        const string source = """
            const escaped: any = { left: null, right: null };
            console.log(escaped);
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);
        var shape = Assert.Single(features.CompactObjectRecordShapes);

        Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(shape.Key));
    }

    [Fact]
    public void EscapeProofTraversesNestedRecordFields()
    {
        const string source = """
            type Node = { left: Node | null; right: Node | null };
            type Wrapper = { node: Node };
            const node: Node = { left: null, right: null };
            const wrapper: Wrapper = { node };
            console.log(wrapper);
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(2, features.CompactObjectRecordShapes.Count);
        Assert.All(features.CompactObjectRecordShapes.Keys, fingerprint =>
            Assert.False(features.CanAssumeCompactObjectRecordIsUnmaterialized(fingerprint)));
    }
}
