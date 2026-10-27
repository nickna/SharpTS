using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeSourceInvocationPlanTests
{
    public static IEnumerable<object[]> ExistingAliasCases() => GenericFunctionAliasTests.Cases()
        .SelectMany(row => new[] { false, true }.Select(native => new object[] { row[0], row[1], row[2], native }));

    [Theory, MemberData(nameof(ExistingAliasCases))]
    public void ExistingGenericAliasCorpusKeepsDeclarationConstraintErasure(string name, string source, string expected, bool native)
    {
        Assert.NotEmpty(name);
        GenericFunctionAliasTests.AssertStrictSource(source);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = Compiler(native);
        compiler.Compile(statements, types);
        Assembly saved = LoadVerified(compiler.SaveToBytes());
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAliasesKeepErasedGenericIdentityClosuresAndReceivers(bool native)
    {
        const string source = """
            function identity<T>(value: T): T { return value; }
            const alias: any = identity;
            alias.tag = 'kept';
            const prototype = alias.prototype;
            console.log(alias(7), alias('seven'), alias === identity, alias.tag,
                alias.prototype === prototype, alias.name, alias.length);
            function outer(start: number) {
                let saved = start;
                function next(step: number = 2, ...rest: number[]) {
                    saved += step + rest.length;
                    return saved;
                }
                const arrow = (extra: number) => { saved += extra; return saved; };
                return [next, arrow];
            }
            const functions: any = outer(10);
            console.log(functions[0](undefined, 3, 4), functions[1](1), functions[0](1));
            const key = Symbol('method');
            class Holder<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                read(extra: any = '!') { return '' + this.value + extra; }
                #secret(extra: any) { return this.read(extra); }
                getPrivate() { return this.#secret; }
                [key](extra: any) { return this.read(extra); }
                get label() { return this.read('='); }
                static keep<U>(value: U): U { return value; }
            }
            const first = new Holder<number>(5);
            const second = new Holder<string>('s');
            const read: any = first.read;
            console.log(read === second.read, read.call(first), read.call(second, '?'));
            const secret: any = first.getPrivate();
            console.log(secret === second.getPrivate(), secret.call(first, '#'), secret.call(second, '$'));
            const keep: any = Holder.keep;
            console.log(keep(3), keep('t'), keep === Holder.keep);
            const left: any = first;
            const right: any = second;
            const computed: any = left[key];
            console.log(computed === right[key], computed.call(first, '@'), computed.call(second, '&'));
            const descriptor: any = Object.getOwnPropertyDescriptor(Holder.prototype, 'label');
            console.log(descriptor.get.call(first), descriptor.get.call(second));
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = Compiler(native);
        compiler.Compile(statements, types);
        Assembly saved = LoadVerified(compiler.SaveToBytes());
        Assert.Equal("7 seven true kept true identity 1\n14 15 16\ntrue 5! s?\ntrue 5# s$\n3 t true\ntrue 5@ s&\n5= s=\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        Type function = saved.GetType("$TSFunction")!;
        Assert.Equal(native, function.GetMethod("GetOrCreateSource", BindingFlags.Static | BindingFlags.NonPublic) is not null);
        Assert.Null(function.GetMethod("GetOrCreateSource"));
        if (!native) return;
        AssertNativeConstructorsDoNotCloseMethods(function);
        var main = saved.GetType("$Program")!.GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Contains(Instructions(main), instruction => instruction.Member is MethodInfo { Name: "GetOrCreateSource" });
        Assert.Contains(Instructions(main), instruction => instruction.Member is MethodInfo method && method.Name == "identity" &&
            method.IsGenericMethod && !method.IsGenericMethodDefinition && method.GetGenericArguments().SequenceEqual(new[] { typeof(object) }));
        var original = saved.GetType("$Program")!.GetMethod("identity")!;
        object wrapper = function.GetMethod("GetOrCreateSource", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [original, "identity", 1, original.MakeGenericMethod(typeof(object))])!;
        Assert.Equal("kept", saved.GetType("$Runtime")!.GetMethod("GetProperty")!.Invoke(null, [wrapper, "tag"]));
        Assert.Same(wrapper, function.GetMethod("GetOrCreateSource", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null,
            [original, "identity", 1, original.MakeGenericMethod(typeof(object))]));
        Assert.Equal(42d, function.GetMethod("Invoke")!.Invoke(wrapper, [new object[] { 42d }]));
        Assert.Equal("again", function.GetMethod("Invoke")!.Invoke(wrapper, [new object[] { "again" }]));
        Assert.Equal(original, function.GetMethod("GetMethodInfo")!.Invoke(wrapper, null));
        Assert.True(((MethodInfo)function.GetMethod("GetMethodInfo")!.Invoke(wrapper, null)!).IsGenericMethodDefinition);
        Assert.Contains(saved.GetTypes().Where(type => type.Name == "$Program" || type.Name.Contains("Holder", StringComparison.Ordinal))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Where(method => method.DeclaringType == type && method.GetMethodBody() is not null)).SelectMany(Instructions),
            instruction => instruction.Member is ConstructorInfo constructor && constructor.DeclaringType == function &&
                constructor.GetParameters().Length == 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespaceAndModuleAliasesKeepTheirSourceFunctionIdentity(bool native)
    {
        string root = Path.Combine(Path.GetTempPath(), "native_plan_" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(root, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(root, "lib.ts")] = """
                export function identity<T>(value: T): T { return value; }
                export namespace Library {
                    export function identity<T>(value: T): T { return value; }
                }
                """,
            [entry] = """
                import { identity, Library } from './lib';
                import * as moduleAlias from './lib';
                const fn: any = identity;
                const member: any = Library.identity;
                console.log(fn(2), fn('module'), fn === moduleAlias.identity);
                console.log(member(4), member('namespace'), member === moduleAlias.Library.identity);
                """
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker();
        var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = Compiler(native);
        compiler.CompileModules(modules, resolver, types);
        Assembly saved = LoadVerified(compiler.SaveToBytes());
        Assert.Equal("2 module true\n4 namespace true\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (native) AssertNativeConstructorsDoNotCloseMethods(saved.GetType("$TSFunction")!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericClassClosuresCreatedAfterSuspensionKeepTheirActualReceivers(bool native)
    {
        const string source = """
            class Box<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                async make() { await Promise.resolve(0); return () => this.value; }
                *values() { yield 0; yield () => this.value; }
                async *stream() { yield 0; yield () => this.value; }
            }
            async function run() {
                const first = new Box<number>(8);
                const second = new Box<string>('string');
                const a: any = await first.make();
                const b: any = await second.make();
                console.log(a(), b());
                const left = first.values();
                const right = second.values();
                left.next(); right.next();
                const c: any = left.next().value;
                const d: any = right.next().value;
                console.log(c(), d());
                const leftAsync = first.stream();
                const rightAsync = second.stream();
                await leftAsync.next(); await rightAsync.next();
                const e: any = (await leftAsync.next()).value;
                const f: any = (await rightAsync.next()).value;
                console.log(e(), f());
            }
            run();
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = Compiler(native);
        compiler.Compile(statements, types);
        Assembly saved = LoadVerified(compiler.SaveToBytes());
        Assert.Equal("8 string\n8 string\n8 string\n", TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void ProducerCarriesTheActualConstructedGenericOwner()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("native_owner_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer("const value=1;").ScanTokens()).ParseOrThrow()));
        var owner = module.DefineType("Owner", TypeAttributes.Public);
        var parameter = owner.DefineGenericParameters("T")[0];
        var value = owner.DefineField("Value", parameter, FieldAttributes.Public);
        var constructor = owner.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [parameter]);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, EmitterTypeHelpers.SelfFieldReference(value)); il.Emit(OpCodes.Ret);
        var read = owner.DefineMethod("Read", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        il = read.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(value)); il.Emit(OpCodes.Box, parameter); il.Emit(OpCodes.Ret);
        Type self = EmitGenerics.MakeGenericType(owner, parameter);
        var wrap = owner.DefineMethod("Wrap", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        il = wrap.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        TypeProvider.Runtime.EmitLoadMethodInfo(il, TypeBuilder.GetMethod(self, read));
        NativeSourceFunctionEmission.EmitConstructor(il, TypeProvider.Runtime, runtime, read, actualOwner: self);
        il.Emit(OpCodes.Ret);
        owner.CreateType();
        using var stream = new MemoryStream(); builder.Save(stream);
        Assembly saved = LoadVerified(stream.ToArray());
        Type function = saved.GetType("$TSFunction")!;
        object Wrap(Type argument, object item)
        {
            Type closed = saved.GetType("Owner")!.MakeGenericType(argument);
            object result = closed.GetMethod("Wrap")!.Invoke(Activator.CreateInstance(closed, [item]), null)!;
            var original = (MethodInfo)function.GetMethod("GetMethodInfo")!.Invoke(result, null)!;
            Assert.Equal(closed, original.DeclaringType);
            return result;
        }
        object first = Wrap(typeof(int), 42), second = Wrap(typeof(string), "closed");
        Assert.Equal(42d, function.GetMethod("Invoke")!.Invoke(first, [Array.Empty<object>()]));
        Assert.Equal("closed", function.GetMethod("Invoke")!.Invoke(second, [Array.Empty<object>()]));
    }

    [Fact]
    public void NativeSourceMetadataCannotCompleteWithMissingFactoriesOrMutateAfterCompletion()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("native_metadata_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        typeof(RuntimeEmitter).GetMethod("DeclareNativeCallableCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [module, runtime]);
        var catalog = runtime.NativeCallables!;
        Assert.Null(catalog.SourceFunctions);
        var source = catalog.BeginSourceFunctionsEmission();
        Assert.Throws<InvalidOperationException>(() => source.Constructor);
        Assert.Throws<InvalidOperationException>(source.CompleteEmission);
        Assert.Throws<InvalidOperationException>(catalog.CompleteEmission);
        var owner = module.DefineType("Owner");
        var constructor = owner.DefineConstructor(MethodAttributes.Assembly, CallingConventions.Standard, Type.EmptyTypes);
        source.Constructor = constructor;
        source.CachedConstructor = constructor;
        source.GetOrCreate = owner.DefineMethod("Factory", MethodAttributes.Assembly | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        source.CompleteEmission();
        catalog.CompleteEmission();
        Assert.Throws<InvalidOperationException>(() => source.Constructor = constructor);
        Assert.Throws<InvalidOperationException>(source.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => catalog.BeginSourceFunctionsEmission());
    }

    private static void AssertNativeConstructorsDoNotCloseMethods(Type function)
    {
        var supplied = function.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(constructor => constructor.GetParameters().Length is 3 or 5).ToArray();
        Assert.Equal(2, supplied.Length);
        foreach (var constructor in supplied)
        {
            Assert.DoesNotContain(Instructions(constructor), instruction => instruction.Member?.Name == "$ErasedSourceInvokerTarget");
            Assert.Contains(Instructions(constructor), instruction => instruction.Member is MethodInfo { Name: "Create", DeclaringType: { Name: "MethodInvoker" } });
        }
        MethodInfo factory = function.GetMethod("GetOrCreateSource", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Contains(Instructions(factory), instruction => instruction.Member is ConstructorInfo constructor &&
            constructor.DeclaringType == function && constructor.GetParameters().Length == 5);
        Assert.DoesNotContain(Instructions(factory), instruction => instruction.Member?.Name == "$ErasedSourceInvokerTarget");
        Assert.Contains(Instructions(function.GetConstructors().Single(constructor => constructor.GetParameters().Length == 2)),
            instruction => instruction.Member?.Name == "$ErasedSourceInvokerTarget");
    }

    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("native_source_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        return compiler;
    }

    private static Assembly LoadVerified(byte[] bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        using var stream = new MemoryStream(bytes);
        Assert.Empty(verifier.Verify(stream));
        return Assembly.Load(bytes);
    }

    private static IEnumerable<(OpCode Code, MemberInfo? Member)> Instructions(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            byte first = bytes[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | bytes[offset++])) : first;
            OpCode code = Codes[value];
            MemberInfo? member = code.OperandType is OperandType.InlineMethod or OperandType.InlineTok
                ? method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null) : null;
            yield return (code, member);
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);
}
