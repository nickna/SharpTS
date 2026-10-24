using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalIsNaNValueTests
{
    [Theory, ModeData]
    public void GlobalIsNaNCoercesArgumentsBeforeTestingForNaN(ExecutionMode mode)
    {
        var source = """
            console.log(isNaN(null as any), globalThis.isNaN(null as any));
            console.log(isNaN("" as any), globalThis.isNaN("" as any));
            console.log(isNaN("   " as any), globalThis.isNaN("   " as any));
            console.log(isNaN(undefined as any));
            console.log(isNaN("abc" as any));
            console.log(isNaN(42));
            console.log(isNaN("0x10" as any));
            console.log(isNaN({ valueOf() { return 4; } } as any));
            console.log(Number.isNaN(null), Number.isNaN(""), Number.isNaN("abc"));
            """;

        Assert.Equal(
            "false false\nfalse false\nfalse false\ntrue\ntrue\nfalse\nfalse\nfalse\nfalse false false\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNConvertsObjectsInOrderAndPropagatesCoercionErrors(ExecutionMode mode)
    {
        var source = """
            try { console.log(isNaN(Symbol() as any)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }
            try { console.log(isNaN(0n as any)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }

            const original = new Error("coerce");
            const throwing: any = { valueOf() { throw original; } };
            try { console.log(isNaN(throwing)); }
            catch (error) { console.log(error === original, error.name, error.message); }

            let trace = "";
            const invalid: any = {
                valueOf() { trace += "v"; return "bad"; },
                toString() { trace += "s"; return "4"; }
            };
            console.log(isNaN(invalid), trace);
            trace = "";
            const fallback: any = {
                valueOf() { trace += "v"; return {}; },
                toString() { trace += "s"; return "4"; }
            };
            console.log(isNaN(fallback), trace);
            trace = "";
            const failed: any = {
                valueOf() { trace += "v"; return {}; },
                toString(): any { trace += "s"; return {}; }
            };
            try { console.log(isNaN(failed)); }
            catch (error) { console.log(error.name, error instanceof TypeError, trace); }
            """;

        Assert.Equal(
            "TypeError true\nTypeError true\ntrue Error coerce\ntrue v\nfalse vs\nTypeError true vs\n",
            TestHarness.Run(source, mode));
    }

    // Interpreted only: compiled numeric conversion skips array and boxed conversion hooks (nickna/SharpTS#2929).
    [Fact]
    public void GlobalIsNaNPreservesArrayAndBoxedConversionHooks()
    {
        var source = """
            const a: any = [];
            a.valueOf = () => NaN;
            console.log(isNaN(a));

            const n: any = new Number(1);
            n.valueOf = () => ({});
            n.toString = () => "bad";
            console.log(isNaN(n));
            """;

        Assert.Equal(
            "true\ntrue\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: compiled numeric string conversion trims .NET whitespace instead of ECMAScript whitespace (nickna/SharpTS#2930).
    [Fact]
    public void GlobalIsNaNUsesECMAScriptWhitespaceForTheNELCharacter()
    {
        var source = """
            const nel = String.fromCharCode(0x85);
            console.log(isNaN(nel as any), isNaN((nel + "1") as any));
            """;

        Assert.Equal(
            "true true\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: compiled numeric conversion skips array conversion hooks (nickna/SharpTS#2929).
    [Fact]
    public void GlobalIsNaNHonorsArrayConversionHooks()
    {
        var source = """
            let trace = "";
            const exoticNaN: any = [];
            exoticNaN[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return NaN; };
            console.log(typeof exoticNaN[Symbol.toPrimitive], isNaN(exoticNaN), Number(exoticNaN), trace);
            trace = "";
            const exoticNumber: any = [];
            exoticNumber[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return 7; };
            console.log(isNaN(exoticNumber), Number(exoticNumber), trace);
            trace = "";
            const exoticString: any = [];
            exoticString[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return "5"; };
            console.log(isNaN(exoticString), Number(exoticString), trace);
            trace = "";
            const exoticObject: any = [];
            exoticObject[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return {}; };
            try { console.log(isNaN(exoticObject), Number(exoticObject), trace); }
            catch (error) { console.log(error.name, error instanceof TypeError, trace); }
            const valueOfNaN: any = [];
            valueOfNaN.valueOf = () => NaN;
            console.log(isNaN(valueOfNaN), Number(valueOfNaN));
            const valueOfNumber: any = [];
            valueOfNumber.valueOf = () => 7;
            console.log(isNaN(valueOfNumber), Number(valueOfNumber));
            const valueOfObject: any = [];
            valueOfObject.valueOf = () => ({});
            valueOfObject.toString = () => "bad";
            console.log(isNaN(valueOfObject), Number(valueOfObject));
            const valueOfString: any = [];
            valueOfString.valueOf = () => "5";
            console.log(isNaN(valueOfString), Number(valueOfString));
            const valueOfFailed: any = [];
            valueOfFailed.valueOf = () => ({});
            valueOfFailed.toString = () => ({});
            try { console.log(isNaN(valueOfFailed), Number(valueOfFailed)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }
            const untouched: any = [];
            console.log(isNaN(untouched), Number(untouched));
            const hintProbe: any = [];
            hintProbe[Symbol.toPrimitive] = (hint: string) => { trace += hint + ";"; return NaN; };
            trace = "";
            console.log(isNaN(hintProbe), trace);
            """;

        Assert.Equal(
            "function true NaN pp\nfalse 7 pp\nfalse 5 pp\nTypeError true p\ntrue NaN\nfalse 7\ntrue NaN\nfalse 5\nTypeError true\nfalse 0\ntrue number;\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, ModeData]
    public void GlobalIsNaNHonorsBoxedWrapperConversionHooks(ExecutionMode mode)
    {
        var source = """
            let trace = "";
            const exoticNaN: any = new Number(1);
            exoticNaN[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return NaN; };
            console.log(typeof exoticNaN[Symbol.toPrimitive], isNaN(exoticNaN), Number(exoticNaN), trace);
            trace = "";
            const exoticNumber: any = new Number(1);
            exoticNumber[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return 7; };
            console.log(isNaN(exoticNumber), Number(exoticNumber), trace);
            trace = "";
            const exoticString: any = new Number(1);
            exoticString[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return "5"; };
            console.log(isNaN(exoticString), Number(exoticString), trace);
            trace = "";
            const exoticObject: any = new Number(1);
            exoticObject[Symbol.toPrimitive] = (hint: string) => { trace += "p"; return {}; };
            try { console.log(isNaN(exoticObject), Number(exoticObject), trace); }
            catch (error) { console.log(error.name, error instanceof TypeError, trace); }
            const valueOfNaN: any = new Number(1);
            valueOfNaN.valueOf = () => NaN;
            console.log(isNaN(valueOfNaN), Number(valueOfNaN));
            const valueOfNumber: any = new Number(1);
            valueOfNumber.valueOf = () => 7;
            console.log(isNaN(valueOfNumber), Number(valueOfNumber));
            const valueOfObject: any = new Number(1);
            valueOfObject.valueOf = () => ({});
            valueOfObject.toString = () => "bad";
            console.log(isNaN(valueOfObject), Number(valueOfObject));
            const valueOfString: any = new Number(1);
            valueOfString.valueOf = () => "5";
            console.log(isNaN(valueOfString), Number(valueOfString));
            const valueOfFailed: any = new Number(1);
            valueOfFailed.valueOf = () => ({});
            valueOfFailed.toString = () => ({});
            try { console.log(isNaN(valueOfFailed), Number(valueOfFailed)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }
            const untouched: any = new Number(1);
            console.log(isNaN(untouched), Number(untouched));
            const boxedString: any = new String("9");
            boxedString.valueOf = () => ({});
            boxedString.toString = () => "bad";
            console.log(isNaN(boxedString), Number(boxedString));
            const boxedBoolean: any = new Boolean(true);
            boxedBoolean.valueOf = () => ({});
            boxedBoolean.toString = () => "bad";
            console.log(isNaN(boxedBoolean), Number(boxedBoolean));
            const hintProbe: any = new Number(1);
            hintProbe[Symbol.toPrimitive] = (hint: string) => { trace += hint + ";"; return NaN; };
            trace = "";
            console.log(isNaN(hintProbe), trace);
            """;

        Assert.Equal(
            "function true NaN pp\nfalse 7 pp\nfalse 5 pp\nTypeError true p\ntrue NaN\nfalse 7\ntrue NaN\nfalse 5\nTypeError true\nfalse 1\ntrue NaN\ntrue NaN\ntrue number;\n",
            TestHarness.Run(source, mode));
    }

    // Interpreted only: compiled numeric conversion skips conversion hooks on other fast-path object kinds (nickna/SharpTS#2929).
    [Fact]
    public void GlobalIsNaNHonorsConversionHooksOnOtherFastPathKinds()
    {
        var source = """
            let trace = "";
            try {
                const fnExotic: any = () => 1;
                fnExotic[Symbol.toPrimitive] = (hint: string) => 7;
                console.log(typeof fnExotic[Symbol.toPrimitive], isNaN(fnExotic), Number(fnExotic));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            try {
                const fnValueOf: any = () => 1;
                fnValueOf.valueOf = () => NaN;
                console.log(isNaN(fnValueOf), Number(fnValueOf));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            try {
                const fnPlain: any = () => 1;
                console.log(isNaN(fnPlain), Number(fnPlain));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            const argsExotic: any = (function () { return arguments; })();
            try {
                argsExotic[Symbol.toPrimitive] = (hint: string) => 7;
                console.log(typeof argsExotic[Symbol.toPrimitive], isNaN(argsExotic), Number(argsExotic));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            const argsValueOf: any = (function () { return arguments; })();
            try {
                argsValueOf.valueOf = () => 7;
                console.log(isNaN(argsValueOf), Number(argsValueOf));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            const argsPlain: any = (function () { return arguments; })();
            try { console.log(isNaN(argsPlain), Number(argsPlain)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }
            const dateUntouched: any = new Date(0);
            console.log(isNaN(dateUntouched), Number(dateUntouched));
            const dateHooked: any = new Date(0);
            try {
                dateHooked.valueOf = () => 42;
                console.log(isNaN(dateHooked), Number(dateHooked));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            class Box {
                value: number;
                constructor(v: number) { this.value = v; }
            }
            const instExotic: any = new Box(1);
            try {
                instExotic[Symbol.toPrimitive] = (hint: string) => 7;
                console.log(typeof instExotic[Symbol.toPrimitive], isNaN(instExotic), Number(instExotic));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            const instFallback: any = new Box(2);
            try {
                instFallback.valueOf = () => ({});
                instFallback.toString = () => "bad";
                console.log(isNaN(instFallback), Number(instFallback));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            const instPlain: any = new Box(3);
            try { console.log(isNaN(instPlain), Number(instPlain)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }
            const plainFallback: any = {};
            plainFallback.valueOf = () => ({});
            plainFallback.toString = () => "bad";
            console.log(isNaN(plainFallback), Number(plainFallback));
            console.log(isNaN(Number.prototype as any), Number(Number.prototype as any));
            console.log(isNaN(String.prototype as any), Number(String.prototype as any));
            console.log(isNaN(Boolean.prototype as any), Number(Boolean.prototype as any));
            """;

        Assert.Equal(
            "function false 7\ntrue NaN\ntrue NaN\nfunction false 7\nfalse 7\ntrue NaN\nfalse 0\nfalse 42\nfunction false 7\ntrue NaN\ntrue NaN\ntrue NaN\nfalse 0\nfalse 0\nfalse 0\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: compiled numeric conversion skips conversion hooks on arrays and other fast-path object kinds (nickna/SharpTS#2929).
    [Fact]
    public void GlobalIsNaNTracesConversionOrderAndHint()
    {
        var source = """
            let trace = "";
            const plain = {
                valueOf() { trace += "v"; return {}; },
                toString() { trace += "s"; return "5"; }
            };
            console.log(isNaN(plain as any), trace);
            const boxed: any = new Number(3);
            boxed.valueOf = () => { trace += "v"; return {}; };
            boxed.toString = () => { trace += "s"; return "5"; };
            console.log(isNaN(boxed), trace);
            const arrayCase: any = [];
            arrayCase.valueOf = () => { trace += "v"; return {}; };
            arrayCase.toString = () => { trace += "s"; return "5"; };
            console.log(isNaN(arrayCase), trace);
            """;

        Assert.Equal(
            "false vs\nfalse vsvs\nfalse vsvsvs\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: compiled numeric string conversion trims .NET whitespace instead of ECMAScript whitespace (nickna/SharpTS#2930).
    [Fact]
    public void GlobalIsNaNTrimsExactlyTheECMAScriptWhitespaceSet()
    {
        var source = """
            const whitespace: number[] = [
                0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x20, 0xA0, 0x1680, 0x2000, 0x2001,
                0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A,
                0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF
            ];
            let mismatchCount = 0;
            for (let codePoint = 0; codePoint <= 0xFFFF; codePoint++) {
                const c = String.fromCharCode(codePoint);
                const inSet = whitespace.indexOf(codePoint) >= 0;
                const isDigit = codePoint >= 0x30 && codePoint <= 0x39;
                const numericPunct = isDigit || codePoint === 0x2B || codePoint === 0x2D || codePoint === 0x2E;
                const wrapped = Number(c + "1" + c);
                if (inSet ? wrapped !== 1 : (!isDigit && !Number.isNaN(wrapped))) {
                    console.log("U+" + ("000" + codePoint.toString(16)).slice(-4).toUpperCase());
                    mismatchCount++;
                    continue;
                }
                if (inSet ? isNaN(c as any) : (!isDigit && !isNaN(c as any))) {
                    console.log("U+" + ("000" + codePoint.toString(16)).slice(-4).toUpperCase());
                    mismatchCount++;
                    continue;
                }
                if (inSet ? isNaN((c + "1") as any) : (!numericPunct && !isNaN((c + "1") as any))) {
                    console.log("U+" + ("000" + codePoint.toString(16)).slice(-4).toUpperCase());
                    mismatchCount++;
                }
            }
            console.log(mismatchCount + " mismatches");
            """;

        Assert.Equal(
            "0 mismatches\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, ModeData]
    public void GlobalIsNaNHonorsOverridesOnPrimitivePrototypes(ExecutionMode mode)
    {
        var source = """
            console.log(isNaN(Number.prototype as any), Number(Number.prototype as any));
            try {
                (Number.prototype as any).valueOf = () => NaN;
                console.log(isNaN(Number.prototype as any), Number(Number.prototype as any));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            console.log(isNaN(String.prototype as any), Number(String.prototype as any));
            try {
                (String.prototype as any).valueOf = () => NaN;
                console.log(isNaN(String.prototype as any), Number(String.prototype as any));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            console.log(isNaN(Boolean.prototype as any), Number(Boolean.prototype as any));
            try {
                (Boolean.prototype as any).valueOf = () => NaN;
                console.log(isNaN(Boolean.prototype as any), Number(Boolean.prototype as any));
            } catch (error) { console.log(error.name, error instanceof TypeError); }
            """;

        Assert.Equal(
            "false 0\ntrue NaN\nfalse 0\ntrue NaN\nfalse 0\ntrue NaN\n",
            TestHarness.Run(source, mode));
    }

    // Interpreted only: the StringPrototypeMethodWrapper boxed-receiver coercion is interpreter machinery.
    // Pins: a String.prototype method dispatched on a boxed String wrapper uses an overriding own toString hook instead of the stored primitive (Node: "xy x").
    [Fact]
    public void GlobalIsNaNBoxedStringIntrinsicsHonorOverridingHooks()
    {
        var source = """
            const s: any = new String("ab");
            s.toString = () => "xy";
            console.log(s.replace("ab","Z"), s.charAt(0));
            """;

        Assert.Equal(
            "xy x\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: the shared numeric coercion hook machinery is interpreter-side (nickna/SharpTS#2929).
    // Pins: an arguments object's conversion hooks run exactly once — valueOf runs once (n increments to 1), both hooks return objects, so the coercion throws TypeError with n still 1.
    [Fact]
    public void GlobalIsNaNArgumentsHooksRunExactlyOnce()
    {
        var source = """
            const args: any = (function () { return arguments; })();
            let n = 0;
            args.valueOf = () => (n++, n > 1 ? 5 : {});
            args.toString = () => ({});
            try { console.log(isNaN(args), n); }
            catch (error) { console.log(error.name, error instanceof TypeError, n); }
            """;

        Assert.Equal(
            "TypeError true 1\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: the shared numeric coercion hook machinery is interpreter-side (nickna/SharpTS#2929).
    // Pins: an Object.prototype-level valueOf override is the resolved hook for an array and diverts the join fast path (Node: 42, then NaN with the "x" override).
    [Fact]
    public void GlobalIsNaNHonorsObjectPrototypeLevelValueOf()
    {
        var source = """
            const original: any = Object.prototype.valueOf;
            try {
                (Object.prototype as any).valueOf = () => 42;
                console.log(isNaN([5] as any), Number([5] as any));
            } finally {
                (Object.prototype as any).valueOf = original;
            }
            const original2: any = Object.prototype.valueOf;
            try {
                (Object.prototype as any).valueOf = () => "x";
                console.log(isNaN([5] as any), Number([5] as any));
            } finally {
                (Object.prototype as any).valueOf = original2;
            }
            """;

        Assert.Equal(
            "false 42\ntrue NaN\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { "original", """
            const root:any=globalThis;console.log(root.parseInt===parseInt,root.parseFloat===parseFloat,root.isNaN===isNaN,root.isFinite===isFinite);console.log(root.parseInt("21"),root.parseFloat("2.5"),root.isNaN("x"),root.isFinite(4));console.log(root.encodeURIComponent===encodeURIComponent,root.decodeURIComponent===decodeURIComponent,root.eval===eval);
            """, "true true true true\n21 2.5 true true\ntrue true true\n" };
        yield return new object[] { "original-predicate-controls", """
            const root:any=globalThis;console.log(isNaN("x" as any),Number.isNaN("x" as any),root.isNaN(NaN),root.isNaN(4));
            """, "true false true false\n" };
        yield return new object[] { "string-numeric-coercion", """
            const g:any=isNaN;for(const value of [""," ","0x10","0b10","0o10","NaN","Infinity","1x"]){console.log(g(value),isNaN(value as any));}
            """, "false false\nfalse false\nfalse false\nfalse false\nfalse false\ntrue true\nfalse false\ntrue true\n" };
        yield return new object[] { "omitted-and-primitive-values", """
            const g:any=isNaN;console.log(g(),g(undefined),g(null),g(NaN),g(-0),g(Infinity),g(true),g(false));
            """, "true true false true false false false false\n" };
        yield return new object[] { "object-coercion", """
            let log="";const bad:any={valueOf(){log+="v";return "bad";},toString(){log+="s";return "4";}};const g:any=isNaN;console.log(g(bad),log);log="";console.log(isNaN(bad),log);const good:any={valueOf(){return {};},toString(){return "4";}};console.log(g(good),Number.isNaN(good));
            """, "true v\ntrue v\nfalse false\n" };
        yield return new object[] { "abrupt-coercion", """
            const g:any=isNaN;const original=new Error("coerce");const bad:any={valueOf(){throw original;}};try{g(bad);}catch(e){console.log(e===original);}for(const value of [Symbol("s"),1n]){try{g(value);}catch(e){console.log(e.name,e instanceof TypeError);}console.log(Number.isNaN(value));}
            """, "true\nTypeError true\nfalse\nTypeError true\nfalse\n" };
        yield return new object[] { "borrowed-and-global-calls", """
            const root:any=globalThis;const g:any=root["isNaN"];console.log(g.call({},"x"),g.apply(null,["4"]),g.bind({},"x")());console.log(root.isNaN("x"),globalThis.isNaN("4" as any));
            """, "true false true\ntrue false\n" };
        yield return new object[] { "identity-and-metadata", """
            const root:any=globalThis;const g:any=isNaN;console.log(g===root.isNaN,g===root["isNaN"],g!==Number.isNaN);console.log(g.name,g.length,g.prototype===undefined);console.log(Number.isNaN(NaN),Number.isNaN("x" as any));
            """, "true true true\nisNaN 1 true\ntrue false\n" };
    }

    public static IEnumerable<object[]> SourceCases() => Cases().Select(row => row[1..]);
    public static IEnumerable<object[]> HostedCases() => Cases().Select(row => row[..2]);

    [Theory, MemberData(nameof(SourceCases))]
    public void GlobalIsNaNValuesUseNumericCoercionAndKeepStrictPredicatesSeparate(string source, string expected)
    {
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
