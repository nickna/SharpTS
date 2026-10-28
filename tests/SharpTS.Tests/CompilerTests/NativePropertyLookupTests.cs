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

public sealed class NativePropertyLookupTests
{
    private const BindingFlags Properties = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
    private const BindingFlags Helpers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void CompletedInventoryMatchesSavedOwnersAndContainsOnlyExactGetterTokens()
    {
        var saved = Compile("class Owner { get value() { return 4; } } console.log(new Owner().value);", true, out var compiler);
        var catalog = compiler.NativeProperties!;
        Assert.True(catalog.IsComplete); Assert.NotEmpty(catalog.Inventory);
        var lookup = Lookup(saved);
        AssertInventory(saved, catalog);
        Assert.DoesNotContain(Instructions(lookup), instruction => instruction.Member?.Name == "GetProperties");
        Assert.Equal(catalog.Inventory.Sum(owner => (owner.UsesLiteralLookup ? 1 : 0) + owner.Properties.Count(property => property.UsesLiteralSelection)),
            Instructions(lookup).Count(instruction => instruction.Member is MethodInfo { Name: "GetProperty", DeclaringType: { } type } && type == typeof(Type)));
        var fields = saved.GetType("$Runtime")!.GetMethod("GetFieldsProperty")!;
        Assert.DoesNotContain(Instructions(fields), instruction => instruction.Member is MethodInfo { Name: "GetProperty", DeclaringType: { } type } && type == typeof(Type));
        Assert.Contains(Instructions(fields), instruction => instruction.Member?.DeclaringType?.Name == "$NativePropertyLookup");
        Assert.Throws<InvalidOperationException>(() => catalog.RegisterOutputOwner((TypeBuilder)catalog.Inventory.First(owner => owner.Owner is TypeBuilder).Owner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WebStreamPropertiesKeepCaseAliasesNumericValuesAndPromises(bool native)
    {
        const string source = """
            const stream: any = new ReadableStream({ start(controller: any) { console.log(typeof controller.desiredSize, controller.desiredSize); controller.close(); } });
            const alias: any = stream;
            console.log(alias.locked, alias.LoCkEd);
            const reader: any = stream.getReader(); console.log(stream.locked, typeof reader.closed.then); reader.releaseLock();
            const writable: any = new WritableStream(); const writer: any = writable.getWriter();
            console.log(writable.locked, typeof writer.ready.then, typeof writer.desiredSize); writer.releaseLock();
            """;
        Assert.Equal("number 1\nfalse false\ntrue function\ntrue function number\n", Run(Compile(source, native, out _)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedNodeStreamOwnersRetainInheritedAndReadonlyProperties(bool native)
    {
        var files = new Dictionary<string, string> { ["/native-property/main.ts"] = """
            import { Readable, Writable, Duplex } from 'node:stream';
            const readable: any = new Readable(); const writable: any = new Writable(); const duplex: any = new Duplex();
            console.log(readable.readable, readable.readableLength, readable.ReAdAbLeLength, writable.writable, duplex.writable, duplex.readable);
            console.log(typeof readable.push, readable.push === readable.push);
            """ };
        Assert.Equal("true 0 0 true true true\nfunction true\n", Run(CompileModule(files, native)));
    }

    [Fact]
    public void NativePropertyLookupKeepsExistingIntrinsicMissBehavior()
    {
        const string source = """
            const target = {};
            const values: any[] = [new Uint8Array([1]), new Int8Array(1), new Float64Array(1),
                new DataView(new ArrayBuffer(4)), new ArrayBuffer(4), new SharedArrayBuffer(4),
                new Date(0), /x/, Object(2), new WeakRef(target), Promise.resolve(1),
                new Map(), new Set(), [], {}, function () {}];
            console.log(values.every((value: any) => value.__absent_property__ === undefined));
            """;
        // The existing named-miss null sentinel on typed arrays/Map/Set is tracked
        // separately in #3055. This opt-in lookup must preserve the normal result.
        Assert.Equal(Run(Compile(source, false, out _)), Run(Compile(source, true, out _)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryHandleAndDirentPropertiesRemainAvailableThroughImportedAliases(bool native)
    {
        string directory = Path.Combine(Path.GetTempPath(), "native_property_fs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "item.txt"), "item");
        try
        {
            string path = System.Text.Json.JsonSerializer.Serialize(directory);
            var files = new Dictionary<string, string> { ["/native-property/main.ts"] =
                "import {opendirSync as open} from 'node:fs'; const path = " + path + "; const dir: any = open(path); " +
                "console.log(typeof dir.path, dir.path === path); const entry: any = dir.readSync(); " +
                "console.log(entry.name, typeof entry.isFile, entry.isFile()); dir.closeSync();" };
            Assert.Equal("string true\nitem.txt function true\n", Run(CompileModule(files, native)));
        }
        finally { File.Delete(Path.Combine(directory, "item.txt")); Directory.Delete(directory); }
    }

    [Fact]
    public void ForeignOwnerAndForeignSubclassCannotBorrowRegisteredBclProperties()
    {
        var saved = Compile("console.log(42);", true, out _);
        ForeignOwner.Reads = 0;
        foreach (var owner in new[] { typeof(ForeignOwner), typeof(ForeignList) })
        {
            var exception = Assert.Throws<TargetInvocationException>(() => Lookup(saved).Invoke(null, [owner, "Count"]));
            Assert.Contains(NativePropertyLookupCatalog.ForeignOwnerMessage, exception.InnerException!.Message);
        }
        Assert.Equal(0, ForeignOwner.Reads);
        Assert.Null(Lookup(saved).Invoke(null, [typeof(List<object>), "NoSuchProperty"]));
        var normal = Compile("console.log(42);", false, out var compiler);
        Assert.Null(compiler.NativeProperties); Assert.Null(normal.GetType("$NativePropertyLookup"));
        Assert.Contains(Instructions(normal.GetType("$Runtime")!.GetMethod("GetFieldsProperty")!),
            instruction => instruction.Member is MethodInfo { Name: "GetProperty", DeclaringType: { } type } && type == typeof(Type));
    }

    [Fact]
    public void RegisteredOutputGettersKeepOneReadExceptionIdentityAndIndexedFailure()
    {
        var (saved, catalog) = Fixture();
        AssertInventory(saved, catalog);
        var owner = saved.GetType("Carrier")!; var instance = Activator.CreateInstance(owner)!;
        var fields = saved.GetType("$Runtime")!.GetMethod("GetFieldsProperty")!;
        Assert.Equal("value", fields.Invoke(null, [instance, "vAlUe"]));
        Assert.Equal(1, owner.GetField("Reads")!.GetValue(null));
        Assert.Equal("value", fields.Invoke(null, [instance, "privateRead"]));
        Assert.Equal(2, owner.GetField("Reads")!.GetValue(null));
        var marker = new InvalidOperationException("marker"); owner.GetField("Marker")!.SetValue(null, marker);
        var actual = Assert.Throws<TargetInvocationException>(() => fields.Invoke(null, [instance, "throws"]));
        var reflection = Assert.Throws<TargetInvocationException>(() => owner.GetProperty("Throws")!.GetValue(instance));
        Assert.Same(marker, Assert.IsType<TargetInvocationException>(actual.InnerException).InnerException);
        Assert.Same(marker, reflection.InnerException);
        var indexed = Assert.Throws<TargetInvocationException>(() => fields.Invoke(null, [instance, "item"]));
        Assert.IsType<TargetParameterCountException>(indexed.InnerException);
        var writeOnly = Assert.Throws<TargetInvocationException>(() => fields.Invoke(null, [instance, "writeOnly"]));
        Assert.Contains("setter-only", writeOnly.InnerException!.Message);
        var ambiguous = Assert.Throws<TargetInvocationException>(() => fields.Invoke(null, [instance, "foo"]));
        var expected = Assert.Throws<AmbiguousMatchException>(() => owner.GetProperty("foo", Properties));
        Assert.IsType<AmbiguousMatchException>(ambiguous.InnerException); Assert.Equal(expected.Message, ambiguous.InnerException!.Message);
        Assert.True(catalog.IsComplete); Assert.Throws<InvalidOperationException>(() => catalog.RegisterOutputOwner(owner));
    }

    [Fact]
    public void InventoryRequiresCompletedSameOutputOwnersAndFreezesAfterCompletion()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("catalog_lifecycle"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var catalog = new NativePropertyLookupCatalog(module, TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(() => catalog.Inventory);
        Assert.Throws<InvalidOperationException>(() => catalog.RegisterOutputOwner(typeof(ForeignOwner)));
        var other = new PersistedAssemblyBuilder(new AssemblyName("foreign_catalog"), typeof(object).Assembly).DefineDynamicModule("other");
        Assert.Throws<InvalidOperationException>(() => catalog.RegisterOutputOwner(other.DefineType("Carrier")));
    }

    private static (Assembly Saved, NativePropertyLookupCatalog Catalog) Fixture(bool allRoles = true, bool nativeProbe = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(nativeProbe ? "Guest" : "property_fixture_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var types = TypeProvider.Runtime;
        var sources = new NativeSourceOwnerCatalog(module, types); var catalog = new NativePropertyLookupCatalog(module, types);
        var minimal = new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow();
        var runtime = new RuntimeEmitter(types, false, nativeProbe).EmitAll(module,
            allRoles ? RuntimeFeatureSet.EmitEverything() : new RuntimeFeatureDetector().Detect(minimal), FunctionEmissionPolicy.Full,
            sources, nativePropertyGetter: catalog.FindGetter);
        var owner = module.DefineType("Carrier", TypeAttributes.Public, typeof(object));
        var constructor = owner.DefineDefaultConstructor(MethodAttributes.Public);
        var reads = owner.DefineField("Reads", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var marker = owner.DefineField("Marker", typeof(Exception), FieldAttributes.Public | FieldAttributes.Static);
        PropertyBuilder Getter(string name, bool throws = false, Type[]? parameters = null, bool nonPublic = false)
        {
            var property = owner.DefineProperty(name, PropertyAttributes.None, typeof(string), parameters ?? Type.EmptyTypes);
            var getter = owner.DefineMethod("get_" + name, (nonPublic ? MethodAttributes.Private : MethodAttributes.Public) | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(string), parameters ?? Type.EmptyTypes);
            var il = getter.GetILGenerator();
            if (throws) { il.Emit(OpCodes.Ldsfld, marker); il.Emit(OpCodes.Throw); }
            else { il.Emit(OpCodes.Ldsfld, reads); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, reads); il.Emit(OpCodes.Ldstr, "value"); il.Emit(OpCodes.Ret); }
            property.SetGetMethod(getter); return property;
        }
        var privateRead = Getter("PrivateRead", nonPublic: true);
        var privateSetter = owner.DefineMethod("set_PrivateRead", MethodAttributes.Public | MethodAttributes.SpecialName, typeof(void), [typeof(string)]);
        privateSetter.GetILGenerator().Emit(OpCodes.Ret); privateRead.SetSetMethod(privateSetter);
        Getter("Value"); Getter("Throws", true); Getter("Item", parameters: [typeof(int)]); Getter("Foo"); Getter("fOO");
        var writeOnly = owner.DefineProperty("WriteOnly", PropertyAttributes.None, typeof(string), Type.EmptyTypes);
        var setter = owner.DefineMethod("set_WriteOnly", MethodAttributes.Public | MethodAttributes.SpecialName, typeof(void), [typeof(string)]);
        setter.GetILGenerator().Emit(OpCodes.Ret); writeOnly.SetSetMethod(setter);
        catalog.RegisterOutputOwner(owner);
        var parameters = new ClassGenericParameterRegistry(); parameters.CompleteEmission([]); sources.CompleteEmission(parameters, []);
        Assert.Throws<InvalidOperationException>(() => catalog.CompleteEmission(sources, runtime));
        owner.CreateType();
        if (nativeProbe) EmitNativeProbe(module, owner, constructor, runtime.ObjectRead.FieldsProperty);
        catalog.CompleteEmission(sources, runtime);
        Assert.Throws<InvalidOperationException>(() => catalog.CompleteEmission(sources, runtime));
        using var stream = new MemoryStream(); assembly.Save(stream);
        var bytes = stream.ToArray();
        if (nativeProbe) bytes = AssemblyReferenceRewriteCompatibility.Rewrite(bytes, EmbeddedReferenceAssemblyIndex.Default,
            ReferencePolicy.DroppingReferences("SharpTS"));
        var saved = Verify(bytes);
        if (nativeProbe && Environment.GetEnvironmentVariable("SHARPTS_PROPERTY_NATIVE_FIXTURE_PATH") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
            File.WriteAllText(path + ".ilverify.txt", "IL verification errors: 0");
        }
        return (saved, catalog);
    }

    [Fact]
    public void SyntheticNativeProbeVerifiesMixedGetterAndUnknownOperationsBeforeSaving()
    {
        var (saved, _) = Fixture(allRoles: false, nativeProbe: true);
        var probe = saved.GetType("NativePropertyProbe")!;
        Assert.Equal("value", probe.GetMethod("Read")!.Invoke(null, null));
        foreach (string name in new[] { "Foreign", "Derived" })
            Assert.Contains(NativePropertyLookupCatalog.ForeignOwnerMessage,
                Assert.IsType<string>(probe.GetMethod(name)!.Invoke(null, null)));
    }

    private static void EmitNativeProbe(ModuleBuilder module, TypeBuilder owner, ConstructorBuilder constructor, MethodInfo fields)
    {
        var derived = module.DefineType("UnregisteredDerived", TypeAttributes.Public, owner);
        var derivedConstructor = derived.DefineDefaultConstructor(MethodAttributes.Public); derived.CreateType();
        var probe = module.DefineType("NativePropertyProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var read = probe.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var il = read.GetILGenerator(); il.Emit(OpCodes.Newobj, constructor); il.Emit(OpCodes.Ldstr, "privateRead");
        il.Emit(OpCodes.Call, fields); il.Emit(OpCodes.Ret);
        foreach (bool isDerived in new[] { false, true })
        {
            var method = probe.DefineMethod(isDerived ? "Derived" : "Foreign", MethodAttributes.Public | MethodAttributes.Static, typeof(string), Type.EmptyTypes);
            il = method.GetILGenerator(); var result = il.DeclareLocal(typeof(string)); var done = il.DefineLabel();
            il.BeginExceptionBlock();
            if (isDerived) il.Emit(OpCodes.Newobj, derivedConstructor);
            else { il.Emit(OpCodes.Ldstr, "https://example.test/"); il.Emit(OpCodes.Newobj, typeof(Uri).GetConstructor([typeof(string)])!); }
            il.Emit(OpCodes.Ldstr, "Value"); il.Emit(OpCodes.Call, fields); il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldstr, "unexpected admission"); il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Leave, done);
            il.BeginCatchBlock(typeof(Exception)); il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, result); il.Emit(OpCodes.Leave, done); il.EndExceptionBlock();
            il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ret);
        }
        probe.CreateType();
    }

    private static void AssertInventory(Assembly saved, NativePropertyLookupCatalog catalog)
    {
        var lookup = Lookup(saved);
        foreach (var owner in catalog.Inventory)
        {
            Type actual = owner.Owner.Module.ModuleVersionId == lookup.Module.ModuleVersionId
                ? saved.GetType(owner.Owner.FullName!)! : owner.Owner;
            string[] expected = actual.GetProperties(Properties).Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
            if (!owner.UsesLiteralLookup) Assert.Equal(expected, owner.Properties.Select(property => property.Name).Order().ToArray());
            else Assert.Empty(owner.Properties);
            foreach (var name in expected)
            {
                PropertyInfo property;
                try { property = actual.GetProperty(name, Properties)!; }
                catch (AmbiguousMatchException)
                {
                    var error = Assert.Throws<TargetInvocationException>(() => lookup.Invoke(null, [actual, name]));
                    Assert.IsType<AmbiguousMatchException>(error.InnerException); continue;
                }
                var expectedGetter = property.GetGetMethod(true);
                if (expectedGetter is null)
                {
                    var error = Assert.Throws<TargetInvocationException>(() => lookup.Invoke(null, [actual, name]));
                    Assert.Contains("setter-only", error.InnerException!.Message); continue;
                }
                var selected = Assert.IsAssignableFrom<MethodInfo>(lookup.Invoke(null, [actual, name.ToLowerInvariant()]));
                Assert.Equal(expectedGetter.DeclaringType, selected.DeclaringType);
                Assert.Equal(expectedGetter.Module.ModuleVersionId, selected.Module.ModuleVersionId);
                Assert.Equal(expectedGetter.MetadataToken, selected.MetadataToken);
            }
            Assert.Null(lookup.Invoke(null, [actual, "__definitely_absent_property__"]));
        }
    }

    public sealed class ForeignOwner { public static int Reads; public int Count => ++Reads; }
    public sealed class ForeignList : List<object> { }
    private static MethodInfo Lookup(Assembly saved) => saved.GetType("$NativePropertyLookup")!.GetMethod("FindGetter", Helpers)!;
    private static ILCompiler Compiler(bool native)
    {
        var compiler = new ILCompiler("native_properties_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); return compiler;
    }
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = Compiler(native); compiler.Compile(statements, types); return Verify(compiler.SaveToBytes());
    }
    private static Assembly CompileModule(Dictionary<string, string> files, bool native)
    {
        const string entry = "/native-property/main.ts"; var resolver = new ModuleResolver(entry, files);
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
