using System.Reflection;
using System.Reflection.Emit;
using PEPacker;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeMethodLookupTests
{
    private const BindingFlags Helpers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void SavedCatalogQueriesMatchLoadedOwnerSelectionForEveryFlagAndName()
    {
        var saved = Compile("class Source { static value() { return 1; } read() { return 2; } } console.log(42);", true, out var compiler);
        var catalog = compiler.NativeMethods!;
        Assert.True(catalog.IsComplete); Assert.False(catalog.UsesLegacy); Assert.NotEmpty(catalog.Owners);
        Assert.Equal(compiler.NativeProperties!.Inventory.Select(entry => entry.Owner), catalog.Owners);
        foreach (var owner in catalog.Owners)
        {
            var actualOwner = owner.Module.ModuleVersionId == saved.ManifestModule.ModuleVersionId
                ? saved.GetType(owner.FullName!)! : owner;
            foreach (var flags in NativeMethodLookupCatalog.SupportedFlags)
            {
                if (catalog.IsManualBridgeOwner(owner))
                {
                    string bridgeMessage = catalog.IsArrayBridgeOwner(owner) ? NativeMethodLookupCatalog.ArrayBridgeMessage : NativeMethodLookupCatalog.FunctionBridgeMessage;
                    foreach (string name in NativeMethodLookupCatalog.ManualBridgeNames)
                    {
                        if (flags == (BindingFlags.Public | BindingFlags.Instance))
                            Assert.Equal(Identity(Legacy(actualOwner, name, flags)), Identity(Find(saved).Invoke(null, [actualOwner, name, flags]) as MethodInfo));
                        else AssertGuestTypeError(saved, () => Find(saved).Invoke(null, [actualOwner, name, flags]), bridgeMessage);
                    }
                    AssertGuestTypeError(saved, () => Query(saved, "Enumerate").Invoke(null, [actualOwner, flags]), bridgeMessage);
                    AssertGuestTypeError(saved, () => Find(saved).Invoke(null, [actualOwner, "GetValue", flags]), bridgeMessage);
                    continue;
                }
                if (!catalog.FlagsFor(owner).Contains(flags))
                {
                    AssertGuestTypeError(saved, () => Find(saved).Invoke(null, [actualOwner, "ToString", flags]), NativeMethodLookupCatalog.UnsupportedFlagsMessage);
                    AssertGuestTypeError(saved, () => Query(saved, "Enumerate").Invoke(null, [actualOwner, flags]), NativeMethodLookupCatalog.UnsupportedFlagsMessage);
                    continue;
                }
                var expectedMethods = actualOwner.GetMethods(flags);
                var actualMethods = Assert.IsType<MethodInfo[]>(Query(saved, "Enumerate").Invoke(null, [actualOwner, flags]));
                Assert.Equal(expectedMethods.Select(Identity), actualMethods.Select(Identity));
                foreach (string name in expectedMethods.Select(method => method.Name).Distinct(StringComparer.Ordinal)
                    .SelectMany(name => new[] { name, name.ToUpperInvariant() }).Append("__absent_method__").Distinct(StringComparer.Ordinal))
                    Assert.Equal(Identity(Legacy(actualOwner, name, flags)), Identity(Find(saved).Invoke(null, [actualOwner, name, flags]) as MethodInfo));
            }
        }
        var find = Find(saved);
        Assert.DoesNotContain(Instructions(find), instruction => instruction.Member is MethodInfo { DeclaringType: { } owner, Name: "GetMethod" or "GetMethods" } && owner == typeof(Type));
        Assert.Contains(Instructions(find), instruction => instruction.Member?.DeclaringType?.Name == "$NativeMethodLookup");
        Assert.Equal(catalog.Owners.Sum(owner => catalog.IsManualBridgeOwner(owner) ? 3 : catalog.FlagsFor(owner).Count), Instructions(Query(saved, "Select")).Count(instruction => instruction.Member?.Name == "GetMethod"));
        Assert.Equal(catalog.Owners.Sum(owner => catalog.IsManualBridgeOwner(owner) ? 0 : catalog.FlagsFor(owner).Count), Instructions(Query(saved, "Enumerate")).Count(instruction => instruction.Member?.Name == "GetMethods"));
    }

    [Fact]
    public void UnprovedGenericInheritanceRetainsTheWholeLegacyQueryOperation()
    {
        var saved = Compile("class Base<T> {} class Owner<T> extends Base<T> { static item = 7; } console.log((Owner as any).item);", true, out var compiler);
        Assert.Equal("7\n", Run(saved));
        Assert.True(compiler.NativeMethods!.UsesLegacy);
        Assert.NotEmpty(compiler.NativeSourceOwners!.Blockers);
        foreach (string query in new[] { "Select", "Enumerate" })
            Assert.Single(Instructions(Query(saved, query)), instruction => instruction.Member is MethodInfo
                { DeclaringType: { } owner, Name: "GetMethod" or "GetMethods" } && owner == typeof(Type));
        Assert.Equal(Identity(Legacy(typeof(Foreign), "Read", BindingFlags.Public | BindingFlags.Instance)),
            Identity(Find(saved).Invoke(null, [typeof(Foreign), "Read", BindingFlags.Public | BindingFlags.Instance]) as MethodInfo));
    }
    [Fact]
    public void QueriesRejectUnknownOwnersInheritedForeignTypesAndUnsupportedFlags()
    {
        var saved = Compile("console.log(42);", true, out _);
        foreach (var owner in new[] { typeof(Foreign), typeof(ForeignList) })
        {
            AssertGuestTypeError(saved, () => Find(saved).Invoke(null, [owner, "ToString", BindingFlags.Public | BindingFlags.Instance]), NativeMethodLookupCatalog.ForeignOwnerMessage);
            AssertGuestTypeError(saved, () => Query(saved, "Enumerate").Invoke(null, [owner, BindingFlags.Public | BindingFlags.Instance]), NativeMethodLookupCatalog.ForeignOwnerMessage);
        }
        foreach (var flags in new[] { BindingFlags.Default, BindingFlags.NonPublic | BindingFlags.Instance, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy })
            AssertGuestTypeError(saved, () => Find(saved).Invoke(null, [typeof(object), "ToString", flags]), NativeMethodLookupCatalog.UnsupportedFlagsMessage);
        var nullName = Assert.Throws<TargetInvocationException>(() => Find(saved).Invoke(null, [typeof(object), null, BindingFlags.Public | BindingFlags.Instance]));
        Assert.IsType<ArgumentNullException>(nullName.InnerException);
        var normal = Compile("console.log(42);", false, out var compiler);
        Assert.Null(compiler.NativeMethods); Assert.Null(normal.GetType("$NativeMethodLookup"));
        Assert.Contains(Instructions(Find(normal)), instruction => instruction.Member is MethodInfo { DeclaringType: { } owner, Name: "GetMethod" } && owner == typeof(Type));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreCarrierMethodAliasesAndSetMemberBridgesRemainExecutable(bool native)
    {
        var files = new Dictionary<string, string> { ["/native-method/main.ts"] = """
            import { Readable } from 'node:stream';
            import { webcrypto } from 'node:crypto';
            const stream: any = new Readable({ objectMode: true });
            const push: any = stream.push;
            console.log(typeof push, push === stream.push, stream.readableLength);
            push('item'); console.log(stream.readableLength); stream.destroy();
            const channel: any = new BroadcastChannel('native-method');
            channel.onmessage = (event: any) => {};
            console.log(channel.name, typeof channel.onmessage, typeof channel.close); channel.close();
            const crypto: any = webcrypto; const subtle: any = crypto.subtle;
            console.log(typeof crypto.randomUUID(), typeof subtle.digest);
            """ };
        Assert.Equal("function true 0\n1\nnative-method function function\nstring function\n", Run(CompileModules(files, native)));
    }

    [Fact]
    public void ManagedZlibCarrierStillUsesItsDeclaredMemberBridge()
    {
        var files = new Dictionary<string, string> { ["/native-method/main.ts"] = """
            import { createGzip } from 'node:zlib';
            const zipper:any=createGzip();
            console.log(zipper.bytesWritten,typeof zipper.close); zipper.close();
            """ };
        Assert.Equal("0 function\n", Run(CompileModules(files, false)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryRawRestArraysKeepDynamicMethodsMissingReadsExpandosAndPrototypes(bool native)
    {
        const string source = """
            function read(...args: any[]) {
                const value: any = args;
                console.log(value.length, typeof value.slice, typeof value.map, value.slice(0).length);
                console.log(value.missing === undefined, typeof value.getValue);
                value.extra = 42;
                const prototype = Object.create(Array.prototype); prototype.inherited = 7;
                Object.setPrototypeOf(value, prototype);
                console.log(value[0], value.extra, value.inherited, value.missing === undefined);
            }
            read(1, 2);
            """;
        Assert.Equal("2 function function 2\ntrue undefined\n1 42 7 true\n", Run(Compile(source, native, out _)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryFunctionsKeepPrototypeDescriptorsSymbolsAndMemberAliases(bool native)
    {
        const string source = """
            function add(x: number): number { return x + 1; }
            const value: any = add;
            console.log(value.name, value.length, value(2), value.call(null,3), value.apply(null,[4]), value.bind(null,5)());
            value.extra = 7;
            Object.defineProperty(value, 'read', { get() { return this.extra; } });
            console.log(value.extra, value.read, value.missing === undefined);
            const prototype = Object.create(Object.getPrototypeOf(value)); prototype.inherited = 8;
            Object.setPrototypeOf(value, prototype);
            console.log(value.inherited, typeof value.call, value(6));
            const holder: any = { callback: value };
            console.log(holder.callback === value, holder.callback(7));
            const symbol = Symbol('slot'); value[symbol] = 9; console.log(value[symbol]);
            """;
        Assert.Equal("add 1 3 4 5 6\n7 7 true\n8 function 7\ntrue 8\n9\n", Run(Compile(source, native, out _)));
        if (native)
        {
            var saved = Compile(source, true, out _);
            AssertGuestTypeError(saved, () => Find(saved).Invoke(null,
                [saved.GetType("$TSFunction"), "InvokeConstructed", BindingFlags.Public | BindingFlags.Instance]),
                NativeMethodLookupCatalog.FunctionBridgeMessage);
        }
    }
    [Fact]
    public void ExactHandlesAndLateCatalogRequireSameOutputAndCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("method_lifecycle"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var types = TypeProvider.Runtime;
        var methods = new NativeMethodLookupCatalog(module, types);
        var properties = new NativePropertyLookupCatalog(module, types);
        var sources = new NativeSourceOwnerCatalog(module, types);
        Assert.Throws<InvalidOperationException>(() => methods.Owners);
        var runtime = new RuntimeEmitter(types, false, true).EmitAll(module, new RuntimeFeatureDetector()
            .Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow()), FunctionEmissionPolicy.Full,
            nativePropertyGetter: properties.FindGetter, nativeMethodLookup: methods.Handles);
        Assert.Throws<InvalidOperationException>(() => methods.CompleteEmission(properties, sources, runtime.Errors, runtime.FunctionValues.Type));
        Assert.Throws<InvalidOperationException>(() => runtime.ReflectedMethods.DeclareNativeMethodLookup(module, methods.Handles));
        var other = new PersistedAssemblyBuilder(new AssemblyName("foreign_method_lifecycle"), typeof(object).Assembly).DefineDynamicModule("other");
        var wrong = new NativeMethodLookupCatalog(other, types);
        Assert.Throws<InvalidOperationException>(() => new EmittedNativeMethodLookupRuntime(module, wrong.Handles.Select, wrong.Handles.Enumerate));
    }

    [Fact]
    public void OverloadAmbiguityUsesLoadedOrderAndRetainsCaseSensitiveFallbackQuirk()
    {
        var (saved, methods) = Fixture();
        var owner = saved.GetType("Carrier")!;
        foreach (var flags in NativeMethodLookupCatalog.SupportedFlags)
            foreach (string name in new[] { "Read", "READ", "Only", "only", "Inherited", "Missing" })
                Assert.Equal(Identity(Legacy(owner, name, flags)), Identity(Find(saved).Invoke(null, [owner, name, flags]) as MethodInfo));
        Assert.Equal("oNLY", Assert.IsAssignableFrom<MethodInfo>(Find(saved).Invoke(null, [owner, "Only", BindingFlags.Public | BindingFlags.Instance])).Name);
        var value = saved.GetType("$Runtime")!.GetMethod("GetFieldsProperty")!.Invoke(null, [Activator.CreateInstance(owner), "items"]);
        Assert.Single(Assert.IsType<object[]>(value));
        Assert.True(methods.IsComplete);
    }

    private static (Assembly Saved, NativeMethodLookupCatalog Methods) Fixture(bool nativeProbe = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(nativeProbe ? "Guest" : "method_fixture_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var types = TypeProvider.Runtime;
        var sources = new NativeSourceOwnerCatalog(module, types); var properties = new NativePropertyLookupCatalog(module, types);
        var methods = new NativeMethodLookupCatalog(module, types);
        var runtime = new RuntimeEmitter(types, false, true).EmitAll(module, nativeProbe
            ? new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow())
            : RuntimeFeatureSet.EmitEverything(), FunctionEmissionPolicy.Full,
            sourceOwners: sources, nativePropertyGetter: properties.FindGetter, nativeMethodLookup: methods.Handles);
        var parent = module.DefineType("Parent", TypeAttributes.Public, typeof(object));
        parent.DefineDefaultConstructor(MethodAttributes.Public);
        void Method(TypeBuilder owner, string name, bool isStatic, Type[] arguments)
        {
            var method = owner.DefineMethod(name, MethodAttributes.Public | MethodAttributes.HideBySig | (isStatic ? MethodAttributes.Static : 0), typeof(string), arguments);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldstr, name); il.Emit(OpCodes.Ret);
        }
        Method(parent, "Inherited", true, Type.EmptyTypes); parent.CreateType();
        var owner = module.DefineType("Carrier", TypeAttributes.Public, parent);
        var constructor = owner.DefineDefaultConstructor(MethodAttributes.Public);
        Method(owner, "Read", false, Type.EmptyTypes); Method(owner, "Read", false, [typeof(int)]);
        Method(owner, "rEAD", false, [typeof(string)]);
        Method(owner, "Only", false, [typeof(int)]); Method(owner, "Only", false, [typeof(string)]);
        Method(owner, "oNLY", false, Type.EmptyTypes);
        Method(owner, "Read", true, [typeof(int), typeof(int)]);
        var getMember = owner.DefineMethod("GetMember", MethodAttributes.Public, typeof(object), [typeof(string)]);
        var memberIl = getMember.GetILGenerator(); memberIl.Emit(OpCodes.Ldc_I4_1);
        memberIl.Emit(OpCodes.Newarr, typeof(object)); memberIl.Emit(OpCodes.Ret);
        properties.RegisterOutputOwner(parent); properties.RegisterOutputOwner(owner); owner.CreateType();
        var parameters = new ClassGenericParameterRegistry(); parameters.CompleteEmission([]); sources.CompleteEmission(parameters, []);
        if (nativeProbe) EmitNativeProbe(module, owner, constructor, runtime);
        properties.CompleteEmission(sources, runtime); methods.CompleteEmission(properties, sources, runtime.Errors, runtime.FunctionValues.Type);
        Assert.Throws<InvalidOperationException>(() => methods.CompleteEmission(properties, sources, runtime.Errors, runtime.FunctionValues.Type));
        using var stream = new MemoryStream(); assembly.Save(stream);
        var bytes = stream.ToArray();
        if (nativeProbe) bytes = AssemblyReferenceRewriteCompatibility.Rewrite(bytes, EmbeddedReferenceAssemblyIndex.Default,
            ReferencePolicy.DroppingReferences("SharpTS"));
        var saved = Verify(bytes);
        if (nativeProbe && Environment.GetEnvironmentVariable("SHARPTS_METHOD_NATIVE_FIXTURE_PATH") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
            File.WriteAllText(path + ".ilverify.txt", "IL verification errors: 0");
        }
        return (saved, methods);
    }

    [Fact]
    public void SyntheticNativeProbePreservesAmbiguityAndGuestErrorIdentity()
    {
        var (saved, _) = Fixture(nativeProbe: true);
        var probe = saved.GetType("NativeMethodProbe")!;
        Assert.Equal("oNLY", probe.GetMethod("Pick")!.Invoke(null, null));
        Assert.Equal(2, probe.GetMethod("StaticArity")!.Invoke(null, null));
        Assert.Equal(1, probe.GetMethod("ArrayValue")!.Invoke(null, null));
        Assert.Equal(true, probe.GetMethod("Foreign")!.Invoke(null, null));
        Assert.Equal(true, probe.GetMethod("Flags")!.Invoke(null, null));
    }

    private static void EmitNativeProbe(ModuleBuilder module, TypeBuilder owner, ConstructorBuilder constructor, EmittedRuntime runtime)
    {
        var foreign = module.DefineType("UnregisteredDerived", TypeAttributes.Public, owner);
        foreign.DefineDefaultConstructor(MethodAttributes.Public); foreign.CreateType();
        var probe = module.DefineType("NativeMethodProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var pick = probe.DefineMethod("Pick", MethodAttributes.Public | MethodAttributes.Static, typeof(string), Type.EmptyTypes);
        var il = pick.GetILGenerator(); il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        il.Emit(OpCodes.Ldstr, "Only"); il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Instance));
        il.Emit(OpCodes.Call, runtime.ReflectedMethods.FindMethod); il.Emit(OpCodes.Callvirt, typeof(MemberInfo).GetProperty("Name")!.GetGetMethod()!); il.Emit(OpCodes.Ret);
        var arity = probe.DefineMethod("StaticArity", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        il = arity.GetILGenerator(); il.Emit(OpCodes.Ldtoken, owner); il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
        il.Emit(OpCodes.Ldstr, "Read"); il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        il.Emit(OpCodes.Call, runtime.ReflectedMethods.FindMethod); il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod("GetParameters")!);
        il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ret);
        var array = probe.DefineMethod("ArrayValue", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        il = array.GetILGenerator(); il.Emit(OpCodes.Newobj, constructor); il.Emit(OpCodes.Ldstr, "items");
        il.Emit(OpCodes.Call, runtime.ObjectRead.FieldsProperty); il.Emit(OpCodes.Castclass, typeof(object[]));
        il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ret);
        foreach (bool unsupportedFlags in new[] { false, true })
        {
            var method = probe.DefineMethod(unsupportedFlags ? "Flags" : "Foreign", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
            il = method.GetILGenerator(); var result = il.DeclareLocal(typeof(bool)); var done = il.DefineLabel();
            il.BeginExceptionBlock(); il.Emit(OpCodes.Ldtoken, unsupportedFlags ? owner : foreign);
            il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!); il.Emit(OpCodes.Ldstr, "Read");
            il.Emit(OpCodes.Ldc_I4, (int)((unsupportedFlags ? BindingFlags.NonPublic : BindingFlags.Public) | BindingFlags.Instance));
            il.Emit(OpCodes.Call, runtime.ReflectedMethods.FindMethod); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Leave, done);
            il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Call, runtime.Errors.WrapException);
            il.Emit(OpCodes.Isinst, runtime.Errors.TypeErrorType); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un);
            il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Leave, done); il.EndExceptionBlock();
            il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
        }
        probe.CreateType();
    }
    public sealed class Foreign { public string Read() => "foreign"; }
    public sealed class ForeignList : List<object> { }
    private static string? Identity(MethodInfo? method) => method is null ? null :
        method.Module.ModuleVersionId + "|" + method.MetadataToken + "|" + method.DeclaringType;

    private static MethodInfo? Legacy(Type owner, string name, BindingFlags flags)
    {
        try { return owner.GetMethod(name, flags); }
        catch (AmbiguousMatchException)
        {
            var methods = owner.GetMethods(flags).Where(method => string.Equals(method.Name, name, StringComparison.OrdinalIgnoreCase));
            return methods.FirstOrDefault(method => method.GetParameters().Length == 0) ?? methods.FirstOrDefault();
        }
    }

    private static void AssertGuestTypeError(Assembly saved, Action action, string message)
    {
        var error = Assert.Throws<TargetInvocationException>(action).InnerException!;
        var guest = saved.GetType("$Runtime")!.GetMethod("WrapException")!.Invoke(null, [error])!;
        Assert.Equal("$TypeError", guest.GetType().Name);
        Assert.Equal(message, guest.GetType().GetProperty("Message")!.GetValue(guest));
    }
    private static MethodInfo Query(Assembly saved, string name) => saved.GetType("$NativeMethodLookup")!.GetMethod(name, Helpers)!;
    private static MethodInfo Find(Assembly saved) => saved.GetType("$Runtime")!.GetMethod("SafeGetMethod")!;
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("native_methods_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); return compiler;
    }
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        compiler = Compiler(native); compiler.Compile(statements, types); return Verify(compiler.SaveToBytes());
    }
    private static Assembly CompileModules(Dictionary<string, string> files, bool native)
    {
        const string entry = "/native-method/main.ts"; var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry)); var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        var compiler = Compiler(native); compiler.CompileModules(modules, resolver, types); return Verify(compiler.SaveToBytes());
    }
    private static Assembly Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
    private static string Run(Assembly saved) => TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20));
}
