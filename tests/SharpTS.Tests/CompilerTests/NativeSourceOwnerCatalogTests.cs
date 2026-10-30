using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeSourceOwnerCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedGenericStaticAndSymbolOwnersKeepIdentityAndClosedInstanceStorage(bool native)
    {
        const string source = """
            const events: string[] = [];
            function note(value: string): string { events.push(value); return value; }
            class Bound {}
            class Base<T> {
                static item = note('base');
                static get [Symbol.toStringTag]() { return 'base-tag'; }
                static [Symbol.iterator]() { return 'base-method'; }
            }
            class Child<U extends Bound> extends Base<U> { static child = note('child'); }
            const Expression = class<V> { static item = note('expression'); };
            const original: any = Child;
            console.log(original === Child, original[Symbol.toStringTag], original[Symbol.iterator]());
            console.log((Base as any).item, original.child, (Expression as any).item);
            console.log(Object.getOwnPropertyDescriptor(Base, 'item')!.value);
            Object.defineProperty(Base, 'item', {value: 'changed'});
            console.log((Base as any).item, Object.prototype.propertyIsEnumerable.call(Base, 'item'));
            console.log(events.join(','));
            """;
        Assembly saved = Compile(source, native, out var emission);
        Assert.Equal("true base-tag base-method\nbase child expression\nbase\nchanged true\nbase,child,expression\n", Run(saved));
        Type? catalog = saved.GetType("<>NativeSourceOwnerCatalog");
        if (!native) { Assert.Null(catalog); return; }
        MethodInfo resolve = catalog!.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(resolve.IsAssembly);
        var genericImplementers = saved.GetTypes().Where(type => type.IsGenericTypeDefinition &&
            saved.GetType("$IHasFields")!.IsAssignableFrom(type)).ToArray();
        Assert.True(genericImplementers.Length >= 3);
        Type[] definitions = Instructions(resolve).Where(instruction => instruction.Code == OpCodes.Ldtoken &&
            instruction.Member is Type { IsGenericTypeDefinition: true }).Select(instruction => (Type)instruction.Member!).ToArray();
        var accounted = definitions.Select(type => type.FullName).Concat(emission!.LegacyOwners.Where(type => type.IsGenericTypeDefinition).Select(type => type.FullName));
        Assert.Equal(genericImplementers.Select(type => type.FullName).Order(), accounted.Order());
        Assert.Contains(Instructions(resolve), instruction => instruction.Member is Type type && type.IsConstructedGenericType &&
            type.GetGenericTypeDefinition().Name == "Base" && type.GetGenericArguments().Single() == typeof(object));
        MethodInfo symbols = saved.GetType("$Runtime")!.GetMethod("SymbolClosedOwner")!;
        Assert.Contains(Instructions(symbols), instruction => instruction.Code == OpCodes.Ldtoken && instruction.Member == saved.GetType("$IHasFields"));
        Assert.Contains(Instructions(symbols), instruction => instruction.Member?.Name == "Resolve");
        Assert.Contains(Instructions(symbols), instruction => instruction.Member?.Name == "MakeGenericType");
        foreach (string name in new[] { "GetProperty", "SetProperty", "ObjectGetOwnPropertyDescriptor", "ObjectDefineProperty", "PropertyIsEnumerableHelper", "GetClassPrototype", "GetClassConstructorKeys" })
        {
            MethodInfo method = saved.GetType("$Runtime")!.GetMethod(name)!;
            Assert.Contains(Instructions(method), instruction => instruction.Member?.DeclaringType?.Name == "<>NativeOwnerClosure" &&
                instruction.Member.Name is "StaticMemberOwner" or "StaticStorageOwner");
            Assert.DoesNotContain(Instructions(method), instruction => instruction.Member?.Name == "MakeGenericType");
        }
        Assert.Equal(typeof(Dictionary<object, object>), symbols.Invoke(null, [typeof(Dictionary<,>)]));
        Assert.Equal(typeof(List<string>), symbols.Invoke(null, [typeof(List<string>)]));
    }

    [Fact]
    public void ProvenConstraintUsesTheDeclarationSnapshotForStaticLookup()
    {
        const string source = "class Bound {} class Box<T extends Bound> { static item = 7; } console.log((Box as any).item);";
        Assembly saved = Compile(source, native: true, out var emission);
        Assert.Equal("7\n", Run(saved)); Assert.Empty(emission!.Blockers); Assert.Equal(1, emission.MappedOwnerCount);
        MethodInfo resolve = saved.GetType("<>NativeSourceOwnerCatalog")!.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Contains(Instructions(resolve), instruction => instruction.Code == OpCodes.Ldtoken && instruction.Member is Type type &&
            type.IsConstructedGenericType && type.GetGenericTypeDefinition().Name == "Box" && type.GetGenericArguments().Single().Name == "Bound");
    }

    [Fact]
    public void CatalogLiteralHandlesDoNotRunInitializersAndUnknownOwnersKeepLegacyClosure()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var contract = module.DefineType("Contract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType()!;
        var catalog = new NativeSourceOwnerCatalog(module, TypeProvider.Runtime); catalog.DeclareContract(contract);
        var state = module.DefineType("State", TypeAttributes.Public);
        var counter = state.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static); state.CreateType();
        var owner = module.DefineType("Owner", TypeAttributes.Public);
        var parameter = owner.DefineGenericParameters("T")[0];
        NativeSourceOwnerCatalog.Implement(owner, contract, catalog);
        var cctor = owner.DefineTypeInitializer().GetILGenerator(); cctor.Emit(OpCodes.Ldc_I4_1); cctor.Emit(OpCodes.Stsfld, counter); cctor.Emit(OpCodes.Ret);
        var parameters = new ClassGenericParameterRegistry(); parameters.Declare(owner, [parameter], [typeof(object)]);
        parameters.CompleteEmission([owner]); owner.CreateType(); catalog.CompleteEmission(parameters, [owner]);
        Assert.Empty(catalog.Blockers); Assert.Equal(1, catalog.MappedOwnerCount);
        Assert.Throws<InvalidOperationException>(() => NativeSourceOwnerCatalog.Implement(owner, contract, catalog));
        Assembly saved = Load(assembly);
        var resolve = saved.GetType("<>NativeSourceOwnerCatalog")!.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        var savedOwner = saved.GetType("Owner")!;
        Type closed = (Type)resolve.Invoke(null, [savedOwner])!;
        Assert.Equal(typeof(object), closed.GetGenericArguments().Single());
        Assert.Equal(0, saved.GetType("State")!.GetField("Count")!.GetValue(null));
        Assert.Equal(typeof(Dictionary<object, object>), resolve.Invoke(null, [typeof(Dictionary<,>)]));
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(closed.TypeHandle);
        Assert.Equal(1, saved.GetType("State")!.GetField("Count")!.GetValue(null));
    }

    [Fact]
    public void UnprovedCanonicalOwnerRecordsBlockerAndRetainsLegacy()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var contract = module.DefineType("Contract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType()!;
        var catalog = new NativeSourceOwnerCatalog(module, TypeProvider.Runtime); catalog.DeclareContract(contract);
        var owner = module.DefineType("Owner", TypeAttributes.Public); var parameter = owner.DefineGenericParameters("T")[0];
        NativeSourceOwnerCatalog.Implement(owner, contract, catalog);
        var parameters = new ClassGenericParameterRegistry(); parameters.Declare(owner, [parameter], [parameter]);
        parameters.CompleteEmission([owner]); owner.CreateType(); catalog.CompleteEmission(parameters, [owner]);
        Assert.Single(catalog.Blockers); Assert.Equal(0, catalog.MappedOwnerCount);
        Assembly saved = Load(assembly);
        var resolve = saved.GetType("<>NativeSourceOwnerCatalog")!.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type closed = (Type)resolve.Invoke(null, [saved.GetType("Owner")])!;
        Assert.Equal(typeof(object), closed.GetGenericArguments().Single());
    }

    [Fact]
    public void AllRuntimeCarrierDeclarationsAreRegisteredFromActualInterfaces()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var catalog = new NativeSourceOwnerCatalog(module, TypeProvider.Runtime);
        new RuntimeEmitter(TypeProvider.Runtime, false, true).EmitAll(module, RuntimeFeatureSet.EmitEverything(), FunctionEmissionPolicy.Full, catalog);
        var parameters = new ClassGenericParameterRegistry(); parameters.CompleteEmission([]); catalog.CompleteEmission(parameters, []);
        Assert.Empty(catalog.Blockers);
        Assembly saved = Load(assembly); Type contract = saved.GetType("$IHasFields")!;
        Type[] direct = saved.GetTypes().Where(type => type != contract && type.GetInterfaces().Contains(contract)
            && (type.BaseType == null || !contract.IsAssignableFrom(type.BaseType))).ToArray();
        Assert.Equal(direct.Select(type => type.FullName).Order(), catalog.Implementers.Select(type => type.FullName).Order());
        Assert.DoesNotContain(direct, type => type.IsGenericTypeDefinition);
    }

    [Fact]
    public void MissingInterfaceRegistrationRecordsBlockerAndStillClosesTheOwner()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var contract = module.DefineType("Contract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract).CreateType()!;
        var catalog = new NativeSourceOwnerCatalog(module, TypeProvider.Runtime); catalog.DeclareContract(contract);
        var owner = module.DefineType("Missed", TypeAttributes.Public); var parameter = owner.DefineGenericParameters("T")[0];
        owner.AddInterfaceImplementation(contract);
        var parameters = new ClassGenericParameterRegistry(); parameters.Declare(owner, [parameter], [typeof(object)]);
        parameters.CompleteEmission([owner]); owner.CreateType(); catalog.CompleteEmission(parameters, [owner]);
        Assert.Single(catalog.Blockers); Assert.Equal(0, catalog.MappedOwnerCount);
        Assembly saved = Load(assembly);
        var resolve = saved.GetType("<>NativeSourceOwnerCatalog")!.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
        Type closed = (Type)resolve.Invoke(null, [saved.GetType("Missed")])!;
        Assert.Equal(typeof(object), closed.GetGenericArguments().Single());
    }

    [Fact]
    public void ImportedGenericDefinitionCompletesItsCatalogAfterAllModules()
    {
        string directory = Path.Combine(Path.GetTempPath(), "native_owner_" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(directory, "main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.Combine(directory, "lib.ts")] = "export class Box<T> { static item = 'module'; static get [Symbol.toStringTag]() { return 'tag'; } }",
            [entry] = "import {Box} from './lib'; console.log((Box as any).item, (Box as any)[Symbol.toStringTag]);"
        };
        var resolver = new ModuleResolver(entry, files); var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker(); var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("native_owner_module_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.CompileModules(modules, resolver, types);
        Assembly saved = Verify(compiler.SaveToBytes()); Assert.Equal("module tag\n", Run(saved));
        Assert.True(compiler.NativeSourceOwners!.IsComplete); Assert.Empty(compiler.NativeSourceOwners.Blockers);
        Assert.Equal(1, compiler.NativeSourceOwners.MappedOwnerCount);
    }

    private static Assembly Compile(string source, bool native, out NativeSourceOwnerCatalog? emission)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("source_owner_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types); emission = compiler.NativeSourceOwners; return Verify(compiler.SaveToBytes());
    }
    private static string Run(Assembly assembly) => TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("source_owner_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static Assembly Load(PersistedAssemblyBuilder assembly) { using var stream = new MemoryStream(); assembly.Save(stream); return Verify(stream.ToArray()); }
    private static Assembly Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
