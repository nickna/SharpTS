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
    public void GlobalIsNaNReturnsTrueWhenTheArgumentIsOmitted(ExecutionMode mode)
    {
        var source = """
            console.log(isNaN(), globalThis.isNaN());
            """;

        Assert.Equal(
            "true true\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNPreservesNonFiniteNumericArrayLiteralElements(ExecutionMode mode)
    {
        var source = """
            console.log(Number([Infinity]), Number([-Infinity]), Number([NaN]), isNaN([NaN] as any));
            function toNumber(value: any) { return Number(value); }
            function toIsNaN(value: any) { return isNaN(value); }
            console.log(toNumber([Infinity]), toNumber([-Infinity]), toNumber([NaN]), toIsNaN([NaN]));
            """;

        Assert.Equal(
            "Infinity -Infinity NaN true\nInfinity -Infinity NaN true\n",
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

    [Theory, ModeData]
    public void GlobalIsNaNPreservesArrayAndBoxedConversionHooks(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNUsesECMAScriptWhitespaceForTheNELCharacter(ExecutionMode mode)
    {
        var source = """
            const nel = String.fromCharCode(0x85);
            console.log(isNaN(nel as any), isNaN((nel + "1") as any));
            """;

        Assert.Equal(
            "true true\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNHonorsArrayConversionHooks(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
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

    [Theory, ModeData]
    public void GlobalIsNaNHonorsConversionHooksOnOtherFastPathKinds(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNHonorsClassConstructorConversionHooks(ExecutionMode mode)
    {
        var source = """
            class Box {}
            const constructor: any = Box;
            let trace = "";
            constructor[Symbol.toPrimitive] = (hint: string) => { trace += hint + ";"; return 7; };
            console.log(isNaN(constructor), Number(constructor), trace);
            class Fallback {}
            const fallback: any = Fallback;
            trace = "";
            fallback.valueOf = () => { trace += "v"; return {}; };
            fallback.toString = () => { trace += "s"; return "bad"; };
            console.log(isNaN(fallback), Number(fallback), trace);
            """;

        Assert.Equal(
            "false 7 number;number;\ntrue NaN vsvs\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNHonorsNativeObjectConversionHooks(ExecutionMode mode)
    {
        var source = """
            const factories: any[] = [() => new Map(), () => new Set(), () => /x/, () => Promise.resolve(1)];
            for (const factory of factories) {
                const original: any = factory();
                console.log(isNaN(original), Number(original));
                const exotic: any = factory();
                exotic[Symbol.toPrimitive] = () => 9;
                console.log(isNaN(exotic), Number(exotic));
                const ordinary: any = factory();
                Object.defineProperty(ordinary, "valueOf", { configurable: true, value: () => 7 });
                console.log(isNaN(ordinary), Number(ordinary));
            }
            """;

        Assert.Equal(
            "true NaN\nfalse 9\nfalse 7\ntrue NaN\nfalse 9\nfalse 7\ntrue NaN\nfalse 9\nfalse 7\ntrue NaN\nfalse 9\nfalse 7\n",
            TestHarness.Run(source, mode));
    }

    // Compiled only: interpreter globalThis lacks the Object.prototype conversion chain and own symbol-property support.
    [Fact]
    public void GlobalIsNaNPreservesGlobalObjectDefaultAndCustomConversions()
    {
        var source = """
            const root: any = globalThis;
            console.log(Number(root), isNaN(root));
            const exoticDescriptor: any = Object.getOwnPropertyDescriptor(root, Symbol.toPrimitive);
            const ordinaryDescriptor: any = Object.getOwnPropertyDescriptor(root, "valueOf");
            try {
                Object.defineProperty(root, Symbol.toPrimitive, { configurable: true, value: () => 9 });
                console.log(Number(root), isNaN(root));
                delete root[Symbol.toPrimitive];
                Object.defineProperty(root, "valueOf", { configurable: true, value: () => 7 });
                console.log(Number(root), isNaN(root));
            } finally {
                if (exoticDescriptor === undefined) delete root[Symbol.toPrimitive];
                else Object.defineProperty(root, Symbol.toPrimitive, exoticDescriptor);
                if (ordinaryDescriptor === undefined) delete root.valueOf;
                else Object.defineProperty(root, "valueOf", ordinaryDescriptor);
            }
            console.log(Number(root), isNaN(root));
            """;

        Assert.Equal(
            "NaN true\n9 false\n7 false\nNaN true\n",
            TestHarness.Run(source, ExecutionMode.Compiled));
    }

    // Compiled only: interpreter typed arrays cannot store own Symbol.toPrimitive properties.
    [Fact]
    public void GlobalIsNaNHonorsTypedArrayExoticConversionHooks()
    {
        var source = """
            const value: any = new Uint8Array([1]);
            value[Symbol.toPrimitive] = () => 9;
            console.log(isNaN(value), Number(value));
            """;

        Assert.Equal(
            "false 9\n",
            TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void GlobalIsNaNPropagatesExoticConversionErrorsAcrossObjectKinds(ExecutionMode mode)
    {
        var source = """
            class Box {}
            const marker = {};
            const values: any[] = [[], function () {}, (function () { return arguments; })(),
                new Number(1), new Box(), Box, new Date(0)];
            for (const value of values) {
                let trace = "";
                Object.defineProperty(value, Symbol.toPrimitive, {
                    configurable: true,
                    get() {
                        trace += "g";
                        return (hint: string) => { trace += hint; throw marker; };
                    }
                });
                try { console.log(isNaN(value)); }
                catch (error) { console.log(error === marker, trace); }
            }
            """;

        Assert.Equal(
            "true gnumber\ntrue gnumber\ntrue gnumber\ntrue gnumber\ntrue gnumber\ntrue gnumber\ntrue gnumber\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNRejectsObjectConversionResultsAndAcceptsNull(ExecutionMode mode)
    {
        var source = """
            class Result {}
            const objectResults: any[] = [[], function () {}, Result, new Date(0)];
            for (const result of objectResults) {
                const exotic: any = {};
                exotic[Symbol.toPrimitive] = () => result;
                try { console.log(isNaN(exotic)); }
                catch (error) { console.log(error.name, error instanceof TypeError); }
                const ordinary: any = {
                    valueOf() { return result; },
                    toString() { return "5"; }
                };
                console.log(isNaN(ordinary), Number(ordinary));
                ordinary.toString = () => result;
                try { console.log(isNaN(ordinary)); }
                catch (error) { console.log(error.name, error instanceof TypeError); }
            }
            const exoticNull: any = {};
            exoticNull[Symbol.toPrimitive] = () => null;
            console.log(isNaN(exoticNull), Number(exoticNull));
            const ordinaryNull: any = { valueOf() { return null; } };
            console.log(isNaN(ordinaryNull), Number(ordinaryNull));
            """;

        Assert.Equal(
            "TypeError true\nfalse 5\nTypeError true\nTypeError true\nfalse 5\nTypeError true\nTypeError true\nfalse 5\nTypeError true\nTypeError true\nfalse 5\nTypeError true\nfalse 0\nfalse 0\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNPreservesArrayAndArgumentsReceiversForInheritedConversionGetters(ExecutionMode mode)
    {
        var source = """
            const args: any = (function () { return arguments; })();
            const receivers: any[] = [[], args];
            for (const receiver of receivers) {
                let trace = "";
                const descriptor: any = Object.getOwnPropertyDescriptor(Object.prototype, "valueOf");
                try {
                    Object.defineProperty(Object.prototype, "valueOf", {
                        configurable: true,
                        get() {
                            trace += "g" + String(this === receiver) + ";";
                            return function () { trace += "v" + String(this === receiver) + ";"; return 7; };
                        }
                    });
                    console.log(isNaN(receiver), Number(receiver), trace);
                } finally {
                    Object.defineProperty(Object.prototype, "valueOf", descriptor);
                }
            }
            """;

        Assert.Equal(
            "false 7 gtrue;vtrue;gtrue;vtrue;\nfalse 7 gtrue;vtrue;gtrue;vtrue;\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNTracesConversionOrderAndHint(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNTrimsExactlyTheECMAScriptWhitespaceSet(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
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

    // Interpreted only: StringPrototypeMethodWrapper dispatches these intrinsics.
    [Fact]
    public void BoxedStringValueOfAndIntrinsicToStringIgnoreConversionHooks()
    {
        var source = """
            const s: any = new String("7");
            let calls = 0;
            s.toString = () => { calls++; return "bad"; };
            console.log(isNaN(s), Number(s), s.valueOf(), String.prototype.toString.call(s), calls);
            console.log(s.charAt(0), calls);
            s.toString = String.prototype.toString;
            console.log(Number(s), s.charAt(0));
            console.log(JSON.stringify(String.prototype.charAt(0)), String.prototype.indexOf("x"));
            """;

        Assert.Equal(
            "false 7 7 7 0\nb 1\n7 7\n\"\" -1\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    // Interpreted only: StringPrototypeMethodWrapper dispatches these methods.
    [Fact]
    public void BoxedStringGenericMethodsUseTheFullConversionProtocol()
    {
        var source = """
            const exotic: any = new String("ab");
            let hints = "";
            exotic[Symbol.toPrimitive] = (hint: string) => { hints += hint; return "xy"; };
            console.log(exotic.charAt(0), exotic.valueOf(), String.prototype.toString.call(exotic), hints);
            const fallback: any = new String("ab");
            let reads = 0;
            Object.defineProperty(fallback, Symbol.toPrimitive, {
                get() { reads++; return undefined; }
            });
            fallback.toString = undefined;
            fallback.valueOf = () => "yz";
            console.log(fallback.charAt(1), reads);
            const failing: any = new String("ab");
            failing.toString = () => ({});
            failing.valueOf = () => ({});
            try { console.log(failing.charAt(0)); }
            catch (error) { console.log(error.name, error instanceof TypeError); }
            """;

        Assert.Equal(
            "x ab ab string\nz 1\nTypeError true\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Theory, ModeData]
    public void GlobalIsNaNReadsArrayExoticConversionAccessorExactlyOnce(ExecutionMode mode)
    {
        var source = """
            let reads = 0;
            let calls = 0;
            const array: any = [];
            Object.defineProperty(array, Symbol.toPrimitive, {
                get() {
                    reads++;
                    return (hint: string) => { calls++; console.log(hint); return 7; };
                }
            });
            console.log(isNaN(array), reads, calls);
            console.log(Number(array), reads, calls);
            """;

        Assert.Equal(
            "number\nfalse 1 1\nnumber\n7 2 2\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GlobalIsNaNSkipsNonCallableProxyConversionMethods(ExecutionMode mode)
    {
        var source = """
            const array: any = [1];
            array.valueOf = new Proxy({}, {});
            console.log(isNaN(array), Number(array));
            const boxed: any = new Number(1);
            boxed.valueOf = new Proxy({}, {});
            boxed.toString = () => "bad";
            console.log(isNaN(boxed), Number(boxed));
            """;

        Assert.Equal(
            "false 1\ntrue NaN\n",
            TestHarness.Run(source, mode));
    }

    // Pins: an arguments object's conversion hooks run exactly once — valueOf runs once (n increments to 1), both hooks return objects, so the coercion throws TypeError with n still 1.
    [Theory, ModeData]
    public void GlobalIsNaNArgumentsHooksRunExactlyOnce(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
    }

    // Pins: an Object.prototype-level valueOf override is the resolved hook for an array and diverts the join fast path (Node: 42, then NaN with the "x" override).
    [Theory, ModeData]
    public void GlobalIsNaNHonorsObjectPrototypeLevelValueOf(ExecutionMode mode)
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
            TestHarness.Run(source, mode));
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
