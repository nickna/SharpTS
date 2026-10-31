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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedAdmissionAlsoGuardsEverySaveEntryBeforeWriting(bool reflection)
    {
        string source = reflection ? ReflectionWrite : Builder + "throw new Builder();";
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
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
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.True(error.Message.Contains("catch binding") || error.Message.Contains("source callable"));
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

    [Theory]
    [InlineData("const value: StringBuilder = new StringBuilder(); export = value;")]
    [InlineData("const Value = StringBuilder; export = Value;")]
    public void CommonJsExportAssignmentCannotDiscardAnOpaqueAliasRead(string body)
    {
        const string entry = "/native-clr/export-assignment.ts";
        var files = new Dictionary<string, string> { [entry] = "import { StringBuilder } from 'dotnet:System.Text.StringBuilder'; " + body };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var types = new TypeChecker().CheckModules(modules, resolver);
        var compiler = new ILCompiler("native_clr_export_assignment", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, Assert.Throws<CompileException>(() => compiler.CompileModules(modules, resolver, types)).Diagnostic.Code);
    }

    [Fact]
    public void CommonJsGuestAnyExportRemainsAValueExport()
    {
        const string entry = "/native-clr/export-guest.ts";
        var resolver = new ModuleResolver(entry, new Dictionary<string, string> { [entry] = "const value: any = { x: 4 }; export = value;" });
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var types = new TypeChecker().CheckModules(modules, resolver);
        var compiler = new ILCompiler("native_clr_guest_export", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.CompileModules(modules, resolver, types);
        using var stream = new MemoryStream(compiler.SaveToBytes());
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
    }

    private const string ExceptionSource = Builder + """
        @DotNetType("System.Collections.IDictionary")
        declare class Dictionary { add(key: any, value: any): void; }
        @DotNetType("System.Exception")
        declare class HostException { constructor(message: string); data: Dictionary; }
        @DotNetType("System.Runtime.ExceptionServices.ExceptionDispatchInfo")
        declare class Dispatch { static capture(error: HostException): Dispatch; throw(): void; }
        function fail() {
            const value: Builder = new Builder(); value.append('opaque');
            const error: HostException = new HostException('test');
            const data: Dictionary = error.data; data.add('__tsValue', value);
            Dispatch.capture(error).throw();
        }
        """;

    [Theory]
    [InlineData("async function run() { fail(); } run().catch(reason => console.log('reason', reason.toString()));", "reason opaque\n")]
    [InlineData("new Promise((resolve, reject) => fail()).catch(reason => console.log('executor', reason.toString()));", "executor opaque\n")]
    [InlineData("Promise.resolve(1).then(fail).catch(reason => console.log('then', reason.toString()));", "then opaque\n")]
    public void PromiseRejectionIngressCannotEraseKnownClrExceptionalEffects(string body, string expected)
    {
        string source = ExceptionSource + body;
        Assert.Equal(expected, Execute(Compile(source, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("exceptional", error.Message);
    }

    [Theory]
    [InlineData(true, "event opaque\n")]
    [InlineData(false, "raw test\n")]
    public void EventEmitterCaptureRejectionsCannotAdmitClrMetadataOrRawExceptions(bool metadata, string expected)
    {
        string source = ExceptionSource;
        if (!metadata) source = source.Replace("data.add('__tsValue', value);", "");
        source += """
            import { EventEmitter } from 'node:events';
            const emitter = new EventEmitter({ captureRejections: true });
            """ + (metadata
                ? "emitter.on('error', reason => console.log('event', reason.toString()));"
                : "emitter.on('error', reason => console.log('raw', reason.message));") + """
            emitter.on('run', async () => { fail(); }); emitter.emit('run');
            """;
        Assert.Equal(expected, Execute(CompileModule(source, false).SaveToBytes()));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape,
            Assert.Throws<CompileException>(() => CompileModule(source, true)).Diagnostic.Code);
    }

    [Theory]
    [InlineData("async function run() { fail(); } run();")]
    [InlineData("function* run() { fail(); yield 1; } run();")]
    [InlineData("const callback = () => fail(); Promise.resolve(1).then(callback);")]
    [InlineData("const callback = fail; const callbacks = [callback];")]
    [InlineData("const value = { run: fail };")]
    [InlineData("function wrap() { return () => fail(); } wrap();")]
    [InlineData("const callback = fail.bind(null);")]
    [InlineData("fail.call(null);")]
    [InlineData("fail.apply(null, []);")]
    [InlineData("class Owner { run() { fail(); } } new Owner();")]
    [InlineData("class Owner { get value() { fail(); return 1; } } new Owner();")]
    [InlineData("class Owner { value = (() => { fail(); return 1; })(); } new Owner();")]
    [InlineData("@DotNetType(\"System.Threading.Thread\") declare class ClrThread { constructor(fn: () => void); } new ClrThread(fail);")]
    public void EffectfulCallableValuesDoNotReuseTypedArgumentAdmission(string body)
    {
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape,
            Assert.Throws<CompileException>(() => Compile(ExceptionSource + body, true)).Diagnostic.Code);
    }

    [Fact]
    public void GuestAndUnrelatedPureAsyncCallbacksRemainGuestOnly()
    {
        const string source = """
            const value: Builder = new Builder(); value.append('bcl');
            async function pure() { return 4; }
            const alias: any = pure;
            Promise.resolve(1).then(async () => 5).then(v => console.log('callback', v));
            alias().then(v => console.log('pure', v));
            new Promise((resolve, reject) => reject({ x: 6 })).catch(e => console.log('guest', e.x));
            """;
        Assert.Equal("pure 4\nguest 6\ncallback 5\n", Execute(Compile(Builder + source, true).SaveToBytes()));
    }

    [Fact]
    public void SharedOriginalCallableUseCannotBorrowItsFirstImmutableAliasProof()
    {
        var statements = new Parser(new Lexer(Builder + "function fail() { new Builder(); } const alias = fail; [fail];").ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var alias = statements.OfType<Stmt.Const>().Single();
        var array = Assert.IsType<Expr.ArrayLiteral>(Assert.IsType<Stmt.Expression>(statements[^1]).Expr);
        array.Elements[0] = alias.Initializer;
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy); var types = checker.Check(statements);
        var compiler = new ILCompiler("native_clr_shared_callable", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        var error = Assert.Throws<CompileException>(() => compiler.Compile(statements, types));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("shared original", error.Message);
    }

    [Fact]
    public void MixedProgramGuestEventRejectionsAndGeneratorsRemainGuestValues()
    {
        const string source = """
            import { EventEmitter } from 'node:events';
            const value: Builder = new Builder(); value.append('bcl');
            const emitter = new EventEmitter({ captureRejections: true });
            emitter.on('error', reason => console.log('guest event', reason.x));
            emitter.on('run', async () => { throw { x: 6 }; }); emitter.emit('run');
            function* pure() { yield 4; }
            console.log('generator', pure().next().value);
            """;
        Assert.Equal("generator 4\nguest event 6\n", Execute(CompileModule(Builder + source, true).SaveToBytes()));
    }

    [Fact]
    public void EffectfulSynchronousCheckedAliasCanStillBeCalledDirectly()
    {
        const string source = """
            function run() { const value: Builder = new Builder(); value.append('typed'); return value.toString(); }
            const alias: any = run;
            console.log(alias());
            """;
        Assert.Equal("typed\n", Execute(Compile(Builder + source, true).SaveToBytes()));
    }

    [Theory]
    [InlineData("const value: Builder = new Builder(); const leaked: any = eval('value');")]
    [InlineData("const value: Builder = new Builder(); const leaked: any = (0, eval)('value');")]
    [InlineData("const value: Builder = new Builder(); const indirect: any = eval; const leaked: any = indirect('value');")]
    [InlineData("console.log(eval('1')); const value: Builder = new Builder();")]
    [InlineData("console.log((0, eval)('1')); const value: Builder = new Builder();")]
    public void StaticEvalBoundaryUsesTheFinalClrFactsRatherThanEmissionOrder(string body)
    {
        var error = Assert.Throws<CompileException>(() => Compile(Builder + body, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("static eval", error.Message);
    }

    [Fact]
    public void GuestOnlyStaticEvalAndLiteralCallableAliasRemainSupported()
    {
        const string source = "const value = 4; console.log(eval('value')); const indirect: any = eval; console.log(indirect('value'));";
        Assert.Equal("4\n4\n", Execute(Compile(source, true).SaveToBytes()));
    }

    [Fact]
    public void FunctionConstructorUsesItsExistingBoundedRuntimeGrammar()
    {
        Assert.Equal("7\n", Execute(Compile(Builder + "const value: Builder = new Builder(); const fn = Function('return this'); console.log(fn.call({ x: 7 }).x);", true).SaveToBytes()));
        Assert.Equal("true\n", Execute(Compile("try { Function('return arbitrarySource'); } catch (e) { console.log(e.message.includes('source text is not supported')); }", true).SaveToBytes()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredCommonJsRootCannotEraseDirectOrTransitiveClrExceptionalEffects(bool transitive)
    {
        const string entry = "/native-clr/deferred-entry.cjs";
        string module;
        if (transitive) module = ExceptionSource + "fail(); module.exports = {};";
        else
        {
            int start = ExceptionSource.IndexOf("function fail() {", StringComparison.Ordinal);
            string body = ExceptionSource[(start + "function fail() {".Length)..];
            module = ExceptionSource[..start] + body[..body.LastIndexOf('}')] + "module.exports = {};";
        }
        var files = new Dictionary<string, string>
        {
            [entry] = "Promise.resolve(1).then(() => require('./deferred.cjs')).catch(reason => console.log('lazy', reason.toString()));",
            ["/native-clr/deferred.cjs"] = module
        };
        Assert.Equal("lazy opaque\n", Execute(CompileFiles(files, entry, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => CompileFiles(files, entry, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("deferred module", error.Message);
    }

    [Fact]
    public void EagerEntryTypedClrAndUnrelatedPureDeferredModuleRemainSupported()
    {
        const string entry = "/native-clr/mixed-entry.cjs";
        var files = new Dictionary<string, string>
        {
            [entry] = Builder + "const value: Builder = new Builder(); value.append('typed'); Promise.resolve(1).then(() => require('./pure.cjs')).then(value => console.log('pure', value.x));",
            ["/native-clr/pure.cjs"] = "module.exports = { x: 4 };"
        };
        Assert.Equal("pure 4\n", Execute(CompileFiles(files, entry, true).SaveToBytes()));
    }

    [Fact]
    public void PureCommonJsCycleDoesNotReclassifyTheEagerEntryAsDeferred()
    {
        const string entry = "/native-clr/cycle-entry.cjs";
        var files = new Dictionary<string, string>
        {
            [entry] = Builder + "exports.x = 3; const value: Builder = new Builder(); value.append('typed'); const child = require('./cycle-child.cjs'); console.log('cycle', child.x);",
            ["/native-clr/cycle-child.cjs"] = "const parent = require('./cycle-entry.cjs'); module.exports = { x: parent.x };"
        };
        Assert.Equal("cycle 3\n", Execute(CompileFiles(files, entry, true).SaveToBytes()));
    }

    [Fact]
    public void DynamicOnlyModuleRootsKeepTheirDeferredClrEffectIdentity()
    {
        const string entry = "/native-clr/dynamic-entry.ts";
        var files = new Dictionary<string, string>
        {
            [entry] = "import('./dynamic').catch(reason => console.log(reason));",
            ["/native-clr/dynamic.ts"] = ExceptionSource + "fail(); export const value = 1;"
        };
        var error = Assert.Throws<CompileException>(() => CompileFiles(files, entry, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("deferred module", error.Message);
    }

    [Fact]
    public void PureDynamicModuleDoesNotTaintEagerEntryClrFacts()
    {
        const string entry = "/native-clr/dynamic-pure-entry.ts";
        var files = new Dictionary<string, string>
        {
            [entry] = Builder + "const value: Builder = new Builder(); value.append('typed'); import('./dynamic-pure').then(module => console.log('import', module.value)); export {};",
            ["/native-clr/dynamic-pure.ts"] = "export const value = 4;"
        };
        Assert.Equal("import 4\n", Execute(CompileFiles(files, entry, true).SaveToBytes()));
    }

    [Fact]
    public void UnresolvedScriptGlobalDoesNotAcquireTheModuleLocalProducerProof()
    {
        const string entry = "/native-clr/dynamic-script-entry.ts";
        var files = new Dictionary<string, string>
        {
            [entry] = Builder + "const value: Builder = new Builder(); value.append('typed'); import('./dynamic-pure').then(module => console.log(module.value));",
            ["/native-clr/dynamic-pure.ts"] = "export const value = 4;"
        };
        var error = Assert.Throws<CompileException>(() => CompileFiles(files, entry, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("guest reference pass-through", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonHostedTopLevelAwaitRetainsItsExistingCompilerContextRefusal(bool native)
    {
        var error = Assert.Throws<CompileException>(() => CompileModule(Builder + "const value: Builder = new Builder(); await Promise.resolve(1); export {};", native));
        Assert.Equal(DiagnosticCode.CompileError, error.Diagnostic.Code); Assert.Contains("Await not supported in this context", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedStatementCannotBorrowTheFirstExecutionOwnerEvenForScalarClrResults(bool scalar)
    {
        string declaration = scalar
            ? "@DotNetType(\"System.Math\") declare class ClrMath { static abs(value: number): number; }"
            : Builder;
        string operation = scalar ? "ClrMath.abs(1);" : "new Builder();";
        string source = declaration + "function first() { " + operation + " } async function second() { " + operation + " } second().catch(reason => console.log(reason));";
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var functions = statements.OfType<Stmt.Function>().ToArray();
        functions[1].Body![0] = functions[0].Body![0];
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy); var types = checker.Check(statements);
        var compiler = new ILCompiler("native_clr_shared_owner", false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        var error = Assert.Throws<CompileException>(() => compiler.Compile(statements, types));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("ambiguous original", error.Message);
    }

    private const string ConsumerSource = Builder + """
        @DotNetType("System.Collections.IList")
        declare class ClrList { add(value: any): number; count: number; }
        @DotNetType("System.Collections.ArrayList")
        declare class ClrArrayList { constructor(); add(value: any): number; count: number; static adapter(value: any): ClrArrayList; }
        """;

    [Theory]
    [InlineData("(guest as any as ClrList).add(value);", "array")]
    [InlineData("const alias: ClrList = ClrArrayList.adapter(guest as any as ClrList); alias.add(value);", "adapter")]
    public void SelectedClrConsumersCannotWriteOpaqueValuesThroughGuestStorageAliases(string operation, string label)
    {
        string source = ConsumerSource + "const value: Builder = new Builder(); value.append('opaque'); const guest: any[] = []; " + operation + " console.log('" + label + "', guest[0].toString());";
        Assert.Equal(label + " opaque\n", Execute(Compile(source, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("guest reference pass-through", error.Message);
    }

    [Fact]
    public void ReferenceReturningCallbackCannotTurnGuestArrayIntoTrustedClrProducer()
    {
        const string source = ConsumerSource + """
            @DotNetType("System.Lazy`1[System.Collections.IList]")
            declare class LazyList { constructor(factory: () => ClrList); value: ClrList; }
            const value: Builder = new Builder(); value.append('opaque'); const guest: any[] = [];
            const lazy: LazyList = new LazyList(() => guest as any as ClrList);
            const alias: ClrList = lazy.value;
            alias.add(value); console.log('callback', guest[0].toString());
            """;
        Assert.Equal("callback opaque\n", Execute(Compile(source, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("reference return", error.Message);
    }

    [Theory]
    [InlineData("System.Lazy`1[System.Object]")]
    [InlineData("System.Lazy`1[System.Int32[]]")]
    public void CallbackObjectAndArrayReturnsAreNotIndependentCopies(string clrType)
    {
        string source = "@DotNetType(\"" + clrType + "\") declare class LazyValue { constructor(factory: () => any); } const guest: any[] = []; new LazyValue(() => guest);";
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("reference return", error.Message);
    }

    [Fact]
    public void TrustedClrLocalsAndTheirAdaptersStayWithinTheClrDomain()
    {
        const string source = ConsumerSource + """
            const value: Builder = new Builder(); value.append('opaque');
            const owned: ClrArrayList = new ClrArrayList(); owned.add(value);
            const adapted: ClrArrayList = ClrArrayList.adapter(owned); adapted.add(value);
            console.log(owned.count, adapted.count);
            """;
        Assert.Equal("2 2\n", Execute(Compile(source, true).SaveToBytes()));
    }

    [Fact]
    public void ScalarArrayCopiesAndScalarCallbacksKeepTheirActualConversions()
    {
        const string source = """
            @DotNetType("System.Convert") declare class Converter { static toBase64String(values: number[]): string; }
            @DotNetType("System.Lazy`1[System.Double]") declare class LazyNumber { constructor(factory: () => number); value: number; }
            @DotNetType("System.Array") declare class ClrArray { static forEach<T>(values: T[], fn: (value: T) => void): void; }
            const values = [1, 2, 3]; console.log(Converter.toBase64String(values));
            const lazy: LazyNumber = new LazyNumber(() => 7); console.log(lazy.value);
            ClrArray.forEach<string>(['a', 'b'], value => console.log(value));
            """;
        Assert.Equal("AQID\n7\na\nb\n", Execute(Compile(source, true).SaveToBytes()));
    }

    [Fact]
    public void ReferenceRetainingClrArrayConversionsRemainUnsupported()
    {
        const string source = """
            @DotNetType("System.Array") declare class ClrArray { static sort<T>(values: T[]): void; }
            const values: any[] = [{}]; ClrArray.sort<any>(values);
            """;
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code); Assert.Contains("guest reference pass-through", error.Message);
    }

    [Fact]
    public void IndexerAssignmentResultRetainsItsOriginalGuestRhsProvenance()
    {
        const string source = ConsumerSource + """
            @DotNetType("System.Collections.Generic.List`1[System.Int32[]]")
            declare class IntArrays { constructor(); add(value: number[]): void; [index: number]: number[]; }
            const value: Builder = new Builder(); value.append('opaque');
            const owned: IntArrays = new IntArrays(); owned.add([1]);
            const guest: any[] = [];
            const result = (owned[0] = guest);
            const alias: ClrArrayList = ClrArrayList.adapter(result);
            alias.add(value); console.log(result === guest, guest[0].toString());
            """;
        Assert.Equal("true opaque\n", Execute(Compile(source, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("guest reference pass-through", error.Message);
    }

    private const string ReflectionMetadata = Builder + """
        @DotNetType("System.Reflection.Assembly")
        declare class ClrAssembly { static getExecutingAssembly(): ClrAssembly; getType(name: string): ClrType; }
        @DotNetType("System.Type")
        declare class ClrType { getField(name: string): ClrField; name: string; }
        @DotNetType("System.Reflection.FieldInfo")
        declare class ClrField { setValue(target: any, value: any): void; name: string; }
        class Guest { static leaked: any = null; }
        const value: Builder = new Builder(); value.append('opaque');
        const assembly: ClrAssembly = ClrAssembly.getExecutingAssembly();
        const owner: ClrType = assembly.getType('Guest');
        const field: ClrField = owner.getField('leaked');
        """;
    private const string ReflectionWrite = ReflectionMetadata + "field.setValue(null, value); console.log('reflection', Guest.leaked.toString());";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflectedFieldWriteCannotAddressGuestStorageThroughMetadataOrItsCheckedAlias(bool alias)
    {
        string source = alias ? ReflectionWrite.Replace("field.setValue", "const alias: ClrField = field; alias.setValue") : ReflectionWrite;
        Assert.Equal("reflection opaque\n", Execute(Compile(source, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("reflected metadata execution/mutation", error.Message);
        Assert.Contains("target ownership", error.Message); Assert.NotNull(error.Diagnostic.Location);
    }

    [Fact]
    public void RawSourceClrNameLookupRemainsAnExactAssemblyLookup()
    {
        // Preserve the complete original source, including its subsequent field
        // read. Native CLR identities are generated; the raw old lookup is null.
        const string source = ReflectionMetadata + "console.log(owner.name, field.name, value.toString());";
        var bytes = Compile(source, true).SaveToBytes();
        var assembly = Assembly.Load(bytes);
        Assert.Null(assembly.GetType("Guest"));
        Assert.StartsWith(NativeSourceTypeNames.DeclarationDomain, NativeSourceTypeLookup.Find(assembly, "Guest").FullName);
        Assert.Throws<NullReferenceException>(() => Execute(bytes));
    }

    [Fact]
    public void PureReflectedMetadataReadsAndDirectTypedCallsRemainSupported()
    {
        var metadata = Assembly.Load(Compile(ReflectionMetadata, true).SaveToBytes());
        string physicalName = NativeSourceTypeLookup.Find(metadata, "Guest").FullName!;
        string source = ReflectionMetadata.Replace("assembly.getType('Guest')", "assembly.getType('" + physicalName + "')", StringComparison.Ordinal);
        Assert.Equal("Guest leaked opaque\n", Execute(Compile(source + "console.log(owner.name, field.name, value.toString());", true).SaveToBytes()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflectedDelegateCannotInvokeAGuestFunctionWithAnOpaqueClrArgument(bool staticFactory)
    {
        string source = Builder + """
            @DotNetType("System.Reflection.Assembly")
            declare class ClrAssembly { static getExecutingAssembly(): ClrAssembly; getType(name: string): ClrType; }
            @DotNetType("System.Type")
            declare class ClrType { getMethod(name: string): ClrMethod; }
            @DotNetType("System.Reflection.MethodInfo")
            declare class ClrMethod { createDelegate(type: any): ClrFunction; }
            @DotNetType("System.Func`2[System.Object,System.Double]")
            declare class ClrFunction { invoke(value: any): number; }
            class Guest { static leaked: any = null; }
            function store(value: any): number { Guest.leaked = value; return 1; }
            const value: Builder = new Builder(); value.append('opaque');
            const assembly: ClrAssembly = ClrAssembly.getExecutingAssembly();
            const owner: ClrType = assembly.getType('$Program');
            const method: ClrMethod = owner.getMethod('store');
            const action: ClrFunction = method.createDelegate(ClrFunction);
            action.invoke(value); console.log('delegate', Guest.leaked.toString());
            """;
        if (staticFactory)
            source = source.Replace("class Guest {", "@DotNetType(\"System.Delegate\") declare class ClrDelegate { static createDelegate(type: any, method: ClrMethod): ClrFunction; } class Guest {")
                .Replace("method.createDelegate(ClrFunction)", "ClrDelegate.createDelegate(ClrFunction, method)");
        Assert.Equal("delegate opaque\n", Execute(Compile(source, false).SaveToBytes()));
        var error = Assert.Throws<CompileException>(() => Compile(source, true));
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains(staticFactory ? "System.Delegate.CreateDelegate" : "MethodInfo.CreateDelegate", error.Message);
        Assert.Contains("target ownership", error.Message);
    }

    public static IEnumerable<object[]> ReflectedExecutionOverloads()
    {
        foreach (var type in new[] { typeof(FieldInfo), typeof(PropertyInfo), typeof(MethodBase), typeof(ConstructorInfo), typeof(MethodInfo), typeof(Delegate), typeof(System.Reflection.TypeDelegator) })
            foreach (var method in type.GetMethods().Where(method => method.Name is "SetValue" or "Invoke" or "InvokeMember" or "CreateDelegate"))
                yield return [method];
    }

    [Theory]
    [MemberData(nameof(ReflectedExecutionOverloads))]
    public void ReflectedExecutionUsesActualOverloadAndInheritedDeclaringTypeIdentity(MethodInfo method)
    {
        var source = new Expr.Literal(1d);
        var admission = NativeClrAdmission.Capture([new Stmt.Expression(source)], new TypeMap(), TypeProvider.Runtime);
        admission.Record(source, method, method.ReturnType, NativeClrAdmission.Marshalling.MethodResult);
        var error = Assert.Throws<CompileException>(admission.Complete);
        Assert.Equal(DiagnosticCode.NativeGuestClrEscape, error.Diagnostic.Code);
        Assert.Contains("reflected metadata execution/mutation", error.Message);
    }

    [Fact]
    public void OrdinaryClrMembersWithTheSameNamesAreNotMetadataCapabilities()
    {
        foreach (var method in new[] { typeof(Array).GetMethod("SetValue", [typeof(object), typeof(int)])!, typeof(Action).GetMethod("Invoke")! })
        {
            var source = new Expr.Literal(1d);
            var admission = NativeClrAdmission.Capture([new Stmt.Expression(source)], new TypeMap(), TypeProvider.Runtime);
            admission.Record(source, method, method.ReturnType, NativeClrAdmission.Marshalling.MethodResult);
            admission.Complete();
        }
    }

    private static ILCompiler CompileModule(string source, bool native) =>
        CompileFiles(new Dictionary<string, string> { ["/native-clr/exceptional.ts"] = source }, "/native-clr/exceptional.ts", native);

    private static ILCompiler CompileFiles(Dictionary<string, string> files, string entry, bool native)
    {
        var resolver = new ModuleResolver(entry, files);
        var entryModule = resolver.LoadModule(entry, DecoratorMode.Legacy);
        var modules = resolver.GetModulesInOrder(entryModule);
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.CheckModules(modules, resolver);
        var dynamicModules = new List<ParsedModule>();
        var processed = new HashSet<(string Specifier, string ImportingModulePath)>();
        while (true)
        {
            var pending = checker.DynamicImportReferences.Where(reference => processed.Add(reference)).ToArray();
            if (pending.Length == 0) break;
            var loaded = pending.SelectMany(reference => resolver.LoadDynamicImportModules(
                [reference.Specifier], reference.ImportingModulePath, DecoratorMode.Legacy)).ToArray();
            if (loaded.Length == 0) continue;
            dynamicModules.AddRange(loaded);
            modules = resolver.GetRuntimeModulesInOrder(dynamicModules.Append(entryModule));
            types = checker.CheckModules(resolver.GetModulesInOrder(dynamicModules.Append(entryModule)), resolver);
        }
        var compiler = new ILCompiler("native_clr_exceptional_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.CompileModules(modules, resolver, types); return compiler;
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
