using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class NativeClrAdmissionTests
{
    private const string Builder = """
        @DotNetType("System.Text.StringBuilder")
        declare class Builder {
            constructor();
            append(value: string): Builder;
            toString(): string;
            length: number;
        }
        """;

    [Theory]
    [InlineData("console.log(new Builder().append('abc').toString());", "abc\n")]
    [InlineData("const value = new Builder(); value.append('abc'); console.log(value.toString(), value.length);", "abc 3\n")]
    [InlineData("const value: Builder = new Builder(); value.append('abc'); console.log(value.toString(), value.length);", "abc 3\n")]
    [InlineData("function run() { const value: Builder = new Builder(); value.append('abc'); console.log(value.toString()); } run();", "abc\n")]
    public void SelectedTypedDomainKeepsDirectAndImmutableLocalCalls(string body, string expected)
    {
        Assert.Equal(expected, Execute(Compile(Builder + body, true).SaveToBytes()));
    }

    [Theory]
    [InlineData("const value: any = new Builder(); console.log(value['toString']());")]
    [InlineData("let value: Builder = new Builder(); value.append('abc');")]
    [InlineData("function make(): Builder { return new Builder(); } make();")]
    [InlineData("throw new Builder();")]
    [InlineData("const values = [new Builder()];")]
    [InlineData("const value = { owner: new Builder() };")]
    [InlineData("const value: Builder = new Builder(); function read() { return value.toString(); } read();")]
    [InlineData("const value: Builder = new Builder(); console.log(String(value));")]
    [InlineData("const value: Builder = new Builder(); console.log(value instanceof Builder);")]
    public void OpaqueEscapesRefuseWithNamedNativeDiagnostic(string body)
    {
        var error = Assert.Throws<CompileException>(() => Compile(Builder + body, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
    }

    [Fact]
    public void GuestAnyCallbacksAndCatchesRemainGuestValues()
    {
        const string source = """
            const value: any = { x: 4 };
            const call = (fn: (v: any) => any) => fn(value);
            try { console.log(call(v => v['x'])); throw value; }
            catch (e) { console.log(e === value); }
            """;
        Assert.Equal("4\ntrue\n", Execute(Compile(source, true).SaveToBytes()));
    }

    [Fact]
    public void OrdinaryCompilationRetainsDynamicClrFlow()
    {
        Assert.Equal("abc\n", Execute(Compile(Builder + "const typed: Builder = new Builder(); typed.append('abc'); const value: any = typed; console.log(value.toString());", false).SaveToBytes()));
    }

    [Fact]
    public void CatchBindingFromClrEffectsIsNotAssumedGuestSafe()
    {
        var error = Assert.Throws<CompileException>(() => Compile(Builder + "try { new Builder(); } catch (e) { console.log(e); }", true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("catch binding", error.Message);
    }

    [Theory]
    [InlineData("@DotNetType(\"System.Globalization.CultureInfo\") declare class Culture { static invariantCulture: Culture; clone(): any; } console.log(Culture.invariantCulture.clone());", "System.Object")]
    [InlineData("@DotNetType(\"System.Diagnostics.Process\") declare class Proc { static getProcesses(): any[]; } Proc.getProcesses();", "array")]
    [InlineData("@DotNetType(\"System.DateTime\") declare class Clock { static now: Clock; } console.log(Clock.now);", "CLR value")]
    [InlineData("@DotNetType(\"System.Threading.ThreadPool\") declare class Pool { static queueUserWorkItem(fn: (state: any) => void): boolean; } Pool.queueUserWorkItem(state => console.log(state));", "callback")]
    public void ResultAndCallbackMarshallingCannotHideOpaqueOrigins(string source, string reason)
    {
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void SafeMaterializedScalarArraysRemainGuestArrays()
    {
        const string source = """
            @DotNetType("System.Environment") declare class Env { static getCommandLineArgs(): string[]; }
            const args = Env.getCommandLineArgs(); console.log(Array.isArray(args), args.length > 0);
            """;
        Assert.Equal("true true\n", Execute(Compile(source, true).SaveToBytes()));
    }

    [Fact]
    public void FailedAdmissionAlsoGuardsEverySaveEntryBeforeWriting()
    {
        var statements = new Parser(new Lexer(Builder + "throw new Builder();").ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy); var types = checker.Check(statements);
        var compiler = new ILCompiler("native_clr_save", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => compiler.Compile(statements, types)).Diagnostic.Code);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => compiler.Save(path)).Diagnostic.Code);
        Assert.False(File.Exists(path));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => compiler.SaveArtifacts()).Diagnostic.Code);
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => compiler.SaveToBytes()).Diagnostic.Code);
    }

    [Fact]
    public void OriginalBindingProofSurvivesNestedFunctionRelocation()
    {
        Assert.Equal("abc\n", Execute(Compile(Builder + "function outer() { function inner() { const value: Builder = new Builder(); value.append('abc'); console.log(value.toString()); } inner(); } outer();", true).SaveToBytes()));
    }

    [Fact]
    public void ImportedTypedLocalIsAdmittedButNamedExportIsNot()
    {
        var files = new Dictionary<string, string> { ["/native-clr/main.ts"] = "import { StringBuilder } from 'dotnet:System.Text.StringBuilder'; const value: StringBuilder = new StringBuilder(); value.append('abc'); console.log(value.toString());" };
        var resolver = new ModuleResolver("/native-clr/main.ts", files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule("/native-clr/main.ts"));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        var compiler = new ILCompiler("native_clr_module", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.CompileModules(modules, resolver, types);
        Assert.Equal("abc\n", Execute(compiler.SaveToBytes()));

        files["/native-clr/main.ts"] = "import { StringBuilder } from 'dotnet:System.Text.StringBuilder'; const value: StringBuilder = new StringBuilder(); export { value };";
        resolver = new ModuleResolver("/native-clr/main.ts", files);
        modules = resolver.GetModulesInOrder(resolver.LoadModule("/native-clr/main.ts"));
        checker = new TypeChecker(); types = checker.CheckModules(modules, resolver);
        compiler = new ILCompiler("native_clr_export", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => compiler.CompileModules(modules, resolver, types)).Diagnostic.Code);
    }

    [Fact]
    public void MissingSelectedSourceContextAndLateWritesAreNeverGuestProof()
    {
        var admission = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime);
        admission.Record(null, typeof(string), typeof(string), NativeClrAdmission.Marshalling.BoxedSlot);
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(admission.Complete).Diagnostic.Code);
        Assert.Throws<InvalidOperationException>(() => admission.RecordTypeValue(new Expr.Literal(1d), typeof(string)));
    }

    [Fact]
    public void BoxedStructTypedReceiverAndConvertedSetterResultsUseDistinctProofs()
    {
        const string source = """
            @DotNetType("System.DateTime") declare class Clock { static now: Clock; year: number; }
            const value: Clock = Clock.now; console.log(value.year > 2000);
            """;
        Assert.Equal("true\n", Execute(Compile(source, true).SaveToBytes()));
        const string converted = """
            @DotNetType("System.Globalization.NumberFormatInfo") declare class Format { constructor(); numberDecimalDigits: number; }
            const value: Format = new Format(); console.log(value.numberDecimalDigits = 3);
            """;
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => Compile(converted, true)).Diagnostic.Code);
        const string constructor = """
            @DotNetType("System.Numerics.Vector2") declare class Vector { constructor(x: number, y: number); x: number; }
            const value: Vector = new Vector(1, 2); console.log(value.x);
            """;
        var error = Assert.Throws<CompileException>(() => Compile(constructor, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("value-type constructor", error.Message);
    }

    [Fact]
    public void ClrReferenceSetterStoreIsAnExplicitlyUnimplementedFlow()
    {
        const string source = """
            @DotNetType("System.Diagnostics.ProcessStartInfo") declare class Info { constructor(); }
            @DotNetType("System.Diagnostics.Process") declare class Proc { constructor(); startInfo: Info; }
            const owner: Proc = new Proc(); const value: Info = new Info(); owner.startInfo = value;
            """;
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("reference-store", error.Message);
        Assert.NotNull(error.Diagnostic.Location);
    }

    [Fact]
    public void CallbackScalarProofUsesItsActualBoxingConvention()
    {
        var scalar = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime);
        scalar.RecordCallback(typeof(Action<string, double>)); scalar.RecordCallback(typeof(Action<int, int[][]>)); scalar.Complete();
        var boxed = NativeClrAdmission.Capture([], new TypeMap(), TypeProvider.Runtime);
        boxed.RecordCallback(typeof(Action<decimal>));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(boxed.Complete).Diagnostic.Code);
    }

    [Theory]
    [InlineData("function make() { const b: Builder = new Builder(); class Holder { get text() { return b.toString(); } } return new Holder(); } make();")]
    [InlineData("const b: Builder = new Builder(); class Holder { [b as any] = 1; } new Holder();")]
    [InlineData("const b: Builder = new Builder(); class Holder { text = b.toString(); } new Holder();")]
    [InlineData("const b: Builder = new Builder(); const Holder = class { text = b.toString(); }; new Holder();")]
    [InlineData("const b: Builder = new Builder(); class Holder { accessor text = b.toString(); } new Holder();")]
    [InlineData("const b: Builder = new Builder(); class Holder { static { b.append('x'); } }")]
    [InlineData("const b: Builder = new Builder(); class Holder extends (b as any) {}")]
    [InlineData("const b: Builder = new Builder(); const value = { get text() { return b.toString(); } }; console.log(value.text);")]
    public void CompleteClassInventoryAndExecutionOwnersPreserveNegativeEvidence(string body)
    {
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => Compile(Builder + body, true)).Diagnostic.Code);
    }

    [Theory]
    [InlineData("function run() { new Builder(); return 1; } const alias: any = run; try { alias(); } catch (e) { console.log(e); }")]
    [InlineData("function run() { new Builder(); return 1; } function invoke(fn: () => number) { return fn(); } try { invoke(run); } catch (e) { console.log(e); }")]
    [InlineData("new Builder(); const target: any = { run: () => 1 }; try { target['run'](); } catch (e) { console.log(e); }")]
    public void ExceptionalClrIngressSurvivesSourceCallsAliasesAndCallbacks(string body)
    {
        var error = Assert.Throws<CompileException>(() => Compile(Builder + body, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("catch binding", error.Message);
    }

    [Fact]
    public void KnownPureSourceAndGuestAnyAliasCatchesStayGuestOnlyInMixedPrograms()
    {
        const string body = """
            new Builder();
            function pure() { return 4; }
            const alias: any = pure;
            let result = 0;
            try { result = pure(); result = alias(); } catch (e) { console.log(e); }
            console.log(result);
            """;
        Assert.Equal("4\n", Execute(Compile(Builder + body, true).SaveToBytes()));
    }

    [Fact]
    public void NativeSerializationCannotSkipSourceAdmission()
    {
        var compiler = new ILCompiler("native_clr_not_started", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        Assert.Throws<InvalidOperationException>(() => compiler.Save(path)); Assert.False(File.Exists(path));
        Assert.Throws<InvalidOperationException>(() => compiler.SaveArtifacts());
        Assert.Throws<InvalidOperationException>(() => compiler.SaveToBytes());
    }

    private static ILCompiler Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.Check(statements);
        var compiler = new ILCompiler("native_clr_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        return compiler;
    }

    private static string Execute(byte[] bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        using var stream = new MemoryStream(bytes); Assert.Empty(verifier.Verify(stream));
        var assembly = Assembly.Load(bytes);
        return TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
    }
}
