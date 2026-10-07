using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PrivateClassElementRegistryTests
{
    [Fact]
    public void MethodValueRejectsForeignCacheAndRequiresAllBodies()
    {
        var module = new PersistedAssemblyBuilder(new AssemblyName(Guid.NewGuid().ToString("N")), typeof(object).Assembly).DefineDynamicModule("Main");
        var owner = module.DefineType("Box", TypeAttributes.Public);
        var adapter = module.DefineType("Value", TypeAttributes.Public);
        var foreign = module.DefineType("Foreign", TypeAttributes.Public);
        var original = owner.DefineMethod("read", MethodAttributes.Public, typeof(object), Type.EmptyTypes);
        var storage = owner.DefineField("brand", typeof(object), FieldAttributes.Static);
        var registry = new PrivateClassElementRegistry();
        registry.Declare("Box", owner, storage, [], new Dictionary<string, FieldBuilder>(),
            new Dictionary<string, MethodBuilder> { ["read"] = original }, new Dictionary<string, MethodBuilder>());
        var source = new SharpTS.Parsing.Stmt.Function(new SharpTS.Parsing.Token(SharpTS.Parsing.TokenType.IDENTIFIER, "#read", null, 0),
            null, null, [], [], null, IsPrivate: true);
        var method = adapter.DefineMethod("Invoke", MethodAttributes.Public, typeof(object), [typeof(object)]);
        var constructor = adapter.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var initializer = adapter.DefineTypeInitializer();
        var cache = adapter.DefineField("Value", typeof(object), FieldAttributes.Static);
        var value = new PrivateMethodValue(adapter, constructor, method, cache, initializer, source);
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethodValue("Box", "read",
            value with { Cache = foreign.DefineField("Value", typeof(object), FieldAttributes.Static) }));
        registry.DeclareMethodValue("Box", "read", value);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, PrivateMethodValue>)registry.Require("Box").MethodValues).Clear());
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("Box"));
        original.GetILGenerator().Emit(OpCodes.Ldnull); original.GetILGenerator().Emit(OpCodes.Ret);
        method.GetILGenerator().Emit(OpCodes.Ldnull); method.GetILGenerator().Emit(OpCodes.Ret);
        constructor.GetILGenerator().Emit(OpCodes.Ret);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("Box"));
        initializer.GetILGenerator().Emit(OpCodes.Ret);
        registry.MarkBodiesEmitted("Box");
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethodValue("Box", "read", value));
        registry.CompleteEmission();
        Assert.Throws<InvalidOperationException>(() => registry.DeclareMethodValue("Box", "read", value));
    }

    [Fact]
    public void GenericBridgeRejectsForeignOwnerAndRequiresItsStorageBody()
    {
        var module = new PersistedAssemblyBuilder(new AssemblyName(Guid.NewGuid().ToString("N")), typeof(object).Assembly)
            .DefineDynamicModule("Main");
        var owner = module.DefineType("Box", TypeAttributes.Public);
        owner.DefineGenericParameters("T");
        var foreign = module.DefineType("Other", TypeAttributes.Public);
        foreign.DefineGenericParameters("T");
        var contract = module.DefineType("PrivateBox", TypeAttributes.Interface | TypeAttributes.Abstract);
        var getter = contract.DefineMethod("GetStorage", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract,
            typeof(object), Type.EmptyTypes);
        var implementation = owner.DefineMethod("GetStorage", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final,
            typeof(object), Type.EmptyTypes);
        var storage = owner.DefineField("brand", typeof(object), FieldAttributes.Static);
        var bridge = new PrivateInstanceBridge(contract, getter, implementation, new Dictionary<string, MethodInfo>());
        var registry = new PrivateClassElementRegistry();
        var foreignStorage = foreign.DefineField("brand", typeof(object), FieldAttributes.Static);
        Assert.Throws<InvalidOperationException>(() => registry.Declare("Other", foreign, foreignStorage, ["value"],
            new Dictionary<string, FieldBuilder>(), new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>(), bridge));
        Assert.False(registry.TryGet("Other", out _));
        registry.Declare("Box", owner, storage, ["value"], new Dictionary<string, FieldBuilder>(),
            new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>(), bridge);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, MethodInfo>)bridge.Methods).Clear());
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("Box"));
        implementation.GetILGenerator().Emit(OpCodes.Ldnull);
        implementation.GetILGenerator().Emit(OpCodes.Ret);
        registry.MarkBodiesEmitted("Box");
        registry.CompleteEmission();
        Assert.Same(bridge, registry.Require("Box").InstanceBridge);
    }

    [Fact]
    public void DeclarationSnapshotsPreserveForwardReferencesAndRequireCompleteBodies()
    {
        var registry = new PrivateClassElementRegistry();
        var owner = NewOwner();
        var storage = owner.DefineField("brand", typeof(object), FieldAttributes.Static);
        var method = owner.DefineMethod("Read", MethodAttributes.Public, typeof(int), Type.EmptyTypes);
        var staticMethod = owner.DefineMethod("StaticRead", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        List<string> names = ["second", "first"];
        Dictionary<string, MethodBuilder> methods = new() { ["read"] = method };
        registry.Declare("C", owner, storage, names, new Dictionary<string, FieldBuilder>(), methods,
            new Dictionary<string, MethodBuilder> { ["read"] = staticMethod });
        names.Clear();
        methods.Clear();
        var declaration = registry.Require("C");
        Assert.Equal(new[] { "second", "first" }, declaration.FieldNames);
        Assert.Same(method, declaration.Methods["read"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, MethodBuilder>)declaration.Methods).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)declaration.FieldNames).Clear());
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        EmitResult(method);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        EmitResult(staticMethod);
        registry.MarkBodiesEmitted("C");
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
        Assert.Same(declaration, registry.Require("C"));
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        Assert.Throws<InvalidOperationException>(() => registry.MarkBodiesEmitted("C"));
        Assert.Throws<InvalidOperationException>(() => DeclareEmpty(registry, "D", NewOwner()));
        var type = owner.CreateType()!;
        Assert.Equal(42, type.GetMethod("Read")!.Invoke(Activator.CreateInstance(type), null));
        Assert.Equal(42, type.GetMethod("StaticRead")!.Invoke(null, null));
    }

    [Fact]
    public void InvalidDeclarationsAreAtomicAndTypeNamesDoNotDetermineOwnership()
    {
        var registry = new PrivateClassElementRegistry();
        var owner = NewOwner();
        var other = NewOwner();
        var foreign = other.DefineField("foreign", typeof(object), FieldAttributes.Static);
        Assert.False(registry.TryGet("C", out _));
        Assert.Throws<InvalidOperationException>(() => registry.Require("C"));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", owner, foreign, [],
            new Dictionary<string, FieldBuilder>(), new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>()));
        Assert.Throws<InvalidOperationException>(() => registry.Declare("C", owner, null, ["field"],
            new Dictionary<string, FieldBuilder>(), new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>()));
        Assert.False(registry.TryGet("C", out _));
        DeclareEmpty(registry, "C", owner);
        Assert.Throws<InvalidOperationException>(() => DeclareEmpty(registry, "alias", owner));
        Assert.Throws<InvalidOperationException>(() => DeclareEmpty(registry, "C", other));
        Assert.False(registry.TryGet("alias", out _));
        DeclareEmpty(registry, "module.C", other);
        Assert.NotSame(registry.Require("C"), registry.Require("module.C"));
        registry.MarkBodiesEmitted("C");
        Assert.Throws<InvalidOperationException>(registry.CompleteEmission);
        registry.MarkBodiesEmitted("module.C");
        registry.CompleteEmission();
        var independent = new PrivateClassElementRegistry();
        Assert.False(independent.TryGet("C", out _));
        independent.CompleteEmission();
    }

    [Fact]
    public void AmbientClassesDoNotRequireRuntimeBodiesInSingleFileCompilation()
    {
        const string source = """
            declare class Ambient {}
            class C { #value = 42; read() { return this.#value; } }
            console.log(new C().read());
            """;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("42\n", output);
    }

    [Fact]
    public void AmbientClassesDoNotRequireRuntimeBodiesInModuleCompilation()
    {
        Dictionary<string, string> files = new()
        {
            ["types.ts"] = "declare class Ambient {} export const value = 42;",
            ["main.ts"] = "import { value } from './types'; class C { #value = value; read() { return this.#value; } } console.log(new C().read());"
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
        Assert.Equal("42\n", TestHarness.RunModules(files, "main.ts", ExecutionMode.Compiled));
    }

    private static void DeclareEmpty(PrivateClassElementRegistry registry, string name, TypeBuilder owner)
        => registry.Declare(name, owner, null, [], new Dictionary<string, FieldBuilder>(),
            new Dictionary<string, MethodBuilder>(), new Dictionary<string, MethodBuilder>());

    private static TypeBuilder NewOwner()
        => AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Main").DefineType("C", TypeAttributes.Public);

    private static void EmitResult(MethodBuilder method)
    {
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 42);
        il.Emit(OpCodes.Ret);
    }
}
