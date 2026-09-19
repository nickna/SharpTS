using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedObjectStorageRuntimeTests
{
    private static PropertyInfo[] Handles => typeof(EmittedObjectStorageRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool)).ToArray();

    public static IEnumerable<object[]> RequiredHandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(RequiredHandleNames))]
    public void MissingDeclarationAllowsRepairAndCompletionFreezesEveryHandle(string name)
    {
        var owner = CreateDeclarations(name);
        var property = typeof(EmittedObjectStorageRuntime).GetProperty(name)!;
        var missing = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{name}'", Assert.IsType<InvalidOperationException>(missing.InnerException).Message);
        var nullWrite = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null));
        Assert.IsType<ArgumentNullException>(nullWrite.InnerException);
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
        property.SetValue(owner, property.GetValue(CreateDeclarations()));
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerIsPerCompilationAndCannotBeReplaced()
    {
        var first = new EmittedRuntime().ObjectStorage;
        Assert.NotSame(first, new EmittedRuntime().ObjectStorage);
        Assert.False(first.IsComplete);
        Assert.Equal(18, Handles.Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.ObjectStorage))!.SetMethod);
    }

    [Fact]
    public void TypeAndConstructorCanBeConsumedBeforeRemainingMembersAreDeclared()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("object_storage_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var type = module.DefineType("ObjectStorage", TypeAttributes.Public);
        var fields = type.DefineField("_fields", typeof(Dictionary<string, object>), FieldAttributes.Private);
        var owner = new EmittedObjectStorageRuntime { Type = type };
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("EmitTSObjectConstructor", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [type, owner, fields]);
        Assert.Same(type, owner.Type);
        Assert.Same(type, owner.Constructor.DeclaringType);
        Assert.False(type.IsCreated());
        var caller = module.DefineType("Caller").DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static,
            owner.Type, [typeof(Dictionary<string, object>)]);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, owner.Constructor);
        il.Emit(OpCodes.Ret);
        Assert.True(il.ILOffset > 0);
        Assert.Contains("'FieldsGetter'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        typeof(RuntimeEmitter).GetMethod("EmitTSObjectFieldsProperty", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(emitter, [type, owner, fields]);
        Assert.Same(type, owner.FieldsGetter.DeclaringType);
        Assert.Contains("'Freeze'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value={a:1};", false)]
    [InlineData("const value={get a(){return 1;}};", false)]
    [InlineData("/x/;1n;new Date();", false)]
    [InlineData("const value={a:1};", true)]
    [InlineData("const value=1;", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void RequiredMetadataPreservesHostingBuilderIdentitySignaturesAndOrder(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var owner = runtime.ObjectStorage;
        AssertFrozen(owner);
        var builder = Assert.IsAssignableFrom<TypeBuilder>(owner.Type);
        Assert.True(builder.IsCreated());
        foreach (var property in Handles.Where(property => property.PropertyType != typeof(Type)))
            Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).DeclaringType);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var type = assembly.GetType("$Object")!;
        Assert.Contains(assembly.GetType("$IHasFields"), type.GetInterfaces());
        Assert.Equal(typeof(Dictionary<string, object>), Assert.Single(Assert.Single(type.GetConstructors()).GetParameters()).ParameterType);
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).OrderBy(field => field.MetadataToken).ToArray();
        Assert.Equal(new[] { "_fields", "_isFrozen", "_isSealed", "_isNonExtensible", "_getters", "_setters" }, fields.Select(field => field.Name));
        Assert.All(fields, field => Assert.True(field.IsPrivate));
        var tokens = new List<int>();
        foreach (var (name, result, parameters) in new (string, Type, Type[])[]
        {
            ("get_Fields", typeof(Dictionary<string, object>), []),
            ("get_IsFrozen", typeof(bool), []), ("get_IsSealed", typeof(bool), []),
            ("Freeze", typeof(void), []), ("Seal", typeof(void), []), ("PreventExtensions", typeof(void), []),
            ("GetProperty", typeof(object), [typeof(string)]),
            ("SetProperty", typeof(void), [typeof(string), typeof(object)]),
            ("SetPropertyStrict", typeof(void), [typeof(string), typeof(object), typeof(bool)]),
            ("HasProperty", typeof(bool), [typeof(string)]),
            ("DeleteProperty", typeof(bool), [typeof(string)]),
            ("DeletePropertyStrict", typeof(bool), [typeof(string), typeof(bool)]),
            ("DefineGetter", typeof(void), [typeof(string), typeof(object)]),
            ("DefineSetter", typeof(void), [typeof(string), typeof(object)]),
            ("HasGetter", typeof(bool), [typeof(string)]), ("HasSetter", typeof(bool), [typeof(string)]),
            ("GetGetter", typeof(object), [typeof(string)]), ("GetSetter", typeof(object), [typeof(string)]),
            ("get_PropertyNames", typeof(IEnumerable<string>), []),
            ("GetGettersDict", typeof(Dictionary<string, object>), []),
            ("GetSettersDict", typeof(Dictionary<string, object>), []), ("ToString", typeof(string), [])
        })
        {
            var method = type.GetMethod(name)!;
            Assert.True(method.IsPublic && !method.IsStatic);
            Assert.Equal(result, method.ReturnType);
            Assert.Equal(parameters, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            tokens.Add(method.MetadataToken);
        }
        Assert.Equal(tokens.OrderBy(token => token).ToArray(), tokens);
        Assert.True(type.GetMethod("get_Fields")!.IsVirtual);
        Assert.True(type.GetMethod("ToString")!.IsVirtual);
    }

    [Fact]
    public void CompletedMetadataLeavesGuestStorageMutableAndKeepsLazyAccessorMapsAndDictionaryIdentity()
    {
        var runtime = EmitRuntime("const value={a:1};");
        AssertFrozen(runtime.ObjectStorage);
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Object")!;
        var data = new Dictionary<string, object> { ["a"] = 1d };
        var instance = Activator.CreateInstance(type, [data])!;
        Assert.Same(data, type.GetProperty("Fields")!.GetValue(instance));
        Assert.Null(Call(instance, "GetProperty", "missing"));
        Call(instance, "SetProperty", "a", 2d);
        Call(instance, "SetPropertyStrict", "b", 3d, true);
        Assert.Equal(2d, data["a"]);
        Assert.Equal(3d, data["b"]);
        Assert.Equal(true, Call(instance, "HasProperty", "a"));
        Assert.Equal("[object Object]", instance.ToString());
        Assert.Null(Call(instance, "GetGettersDict"));
        Assert.Null(Call(instance, "GetSettersDict"));
        // These sentinels verify storage identity; isolated guest tests exercise invocation.
        var getter = new object();
        var setter = new object();
        Call(instance, "DefineGetter", "accessor", getter);
        Call(instance, "DefineSetter", "accessor", setter);
        Assert.Same(getter, Call(instance, "GetGetter", "accessor"));
        Assert.Same(setter, Call(instance, "GetSetter", "accessor"));
        Assert.Same(getter, Assert.IsType<Dictionary<string, object>>(Call(instance, "GetGettersDict"))["accessor"]);
        Assert.Same(setter, Assert.IsType<Dictionary<string, object>>(Call(instance, "GetSettersDict"))["accessor"]);
        Assert.Equal(true, Call(instance, "HasProperty", "accessor"));
        // Native PropertyNames enumerates fields; generic Object.keys merges accessors separately.
        Assert.Equal(new[] { "a", "b" }, Assert.IsAssignableFrom<IEnumerable<string>>(type.GetProperty("PropertyNames")!.GetValue(instance)));
        data["accessor"] = 4d;
        Assert.Equal(true, Call(instance, "DeleteProperty", "accessor"));
        Assert.False(data.ContainsKey("accessor"));
        Assert.Equal(false, Call(instance, "HasGetter", "accessor"));
        Assert.Equal(false, Call(instance, "HasSetter", "accessor"));
        Assert.Equal(false, Call(instance, "HasProperty", "accessor"));
        Assert.Null(Call(instance, "GetGetter", "accessor"));
        Assert.Null(Call(instance, "GetSetter", "accessor"));
    }

    [Theory]
    [InlineData("Freeze", true, true)]
    [InlineData("Seal", false, true)]
    [InlineData("PreventExtensions", false, false)]
    public void GuestRestrictionsPreserveStrictFlagsAndEarlyTypeErrorWrapping(string restriction, bool frozen, bool sealedObject)
    {
        var runtime = EmitRuntime("const value=1;");
        using var bytes = Save(runtime);
        var type = Assembly.Load(bytes.ToArray()).GetType("$Object")!;
        var data = new Dictionary<string, object> { ["a"] = 1d };
        var instance = Activator.CreateInstance(type, [data])!;
        Call(instance, restriction);
        Assert.Equal(frozen, type.GetProperty("IsFrozen")!.GetValue(instance));
        Assert.Equal(sealedObject, type.GetProperty("IsSealed")!.GetValue(instance));
        Call(instance, "SetProperty", "a", 2d);
        Call(instance, "SetPropertyStrict", "a", 3d, false);
        Assert.Equal(frozen ? 1d : 3d, data["a"]);
        if (frozen) AssertGuestTypeError(instance, "SetPropertyStrict", "a", 4d, true);
        else
        {
            Call(instance, "SetPropertyStrict", "a", 4d, true);
            Assert.Equal(4d, data["a"]);
        }
        Call(instance, "SetProperty", "new", 5d);
        Call(instance, "SetPropertyStrict", "new", 6d, false);
        Assert.False(data.ContainsKey("new"));
        AssertGuestTypeError(instance, "SetPropertyStrict", "new", 7d, true);
        Assert.Equal(!sealedObject, Call(instance, "DeleteProperty", "a"));
        Assert.Equal(!sealedObject, Call(instance, "DeletePropertyStrict", "a", false));
        if (sealedObject) AssertGuestTypeError(instance, "DeletePropertyStrict", "a", true);
        else Assert.Equal(true, Call(instance, "DeletePropertyStrict", "a", true));
    }

    [Fact]
    public void ReusedEmitterKeepsObjectDeclarationsAndGuestStateInTheirOwnSavedAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var owners = new List<EmittedObjectStorageRuntime>();
        var types = new List<Type>();
        foreach (var source in new[] { "/x/;1n;new Date();", "const value=1;", "/x/;1n;new Date();" })
        {
            var runtime = EmitRuntime(source, emitter: emitter);
            Assert.DoesNotContain(runtime.ObjectStorage, owners);
            owners.Add(runtime.ObjectStorage);
            AssertFrozen(runtime.ObjectStorage);
            Assert.DoesNotContain(runtime.ObjectStorage.Type, types);
            types.Add(runtime.ObjectStorage.Type);
            using var bytes = Save(runtime);
            Verify(bytes);
            var assembly = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("object_storage_", StringComparison.Ordinal));
            var type = assembly.GetType("$Object")!;
            Assert.DoesNotContain(type, types);
            types.Add(type);
            Assert.Contains(assembly.GetType("$IHasFields"), type.GetInterfaces());
            var data = new Dictionary<string, object>();
            var instance = Activator.CreateInstance(type, [data])!;
            Assert.Null(Call(instance, "GetGettersDict"));
            Assert.Null(Call(instance, "GetSettersDict"));
            Assert.Equal(false, type.GetProperty("IsFrozen")!.GetValue(instance));
            Assert.Equal(false, type.GetProperty("IsSealed")!.GetValue(instance));
            Call(instance, "SetPropertyStrict", "value", owners.Count, true);
            Assert.Equal(owners.Count, data["value"]);
            Call(instance, "DefineGetter", "accessor", new object());
            Call(instance, "Freeze");
            AssertGuestTypeError(instance, "SetPropertyStrict", "value", 0, true);
        }
    }

    private static EmittedObjectStorageRuntime CreateDeclarations(string? omission = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"object_storage_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("ObjectDeclarations");
        var owner = new EmittedObjectStorageRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omission) continue;
            object value;
            if (property.PropertyType == typeof(Type)) value = type;
            else if (property.PropertyType == typeof(ConstructorBuilder))
                value = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            else
                value = type.DefineMethod(property.Name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            property.SetValue(owner, value);
        }
        return owner;
    }

    private static void AssertFrozen(EmittedObjectStorageRuntime owner)
    {
        Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(owner);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static object? Call(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethod(name)!.Invoke(instance, arguments);

    private static void AssertGuestTypeError(object instance, string name, params object?[] arguments)
    {
        var error = Assert.Throws<TargetInvocationException>(() => Call(instance, name, arguments));
        var guest = Assert.IsAssignableFrom<Exception>(error.InnerException).Data["__tsValue"];
        Assert.NotNull(guest);
        Assert.Equal("$TypeError", guest.GetType().Name);
        Assert.Same(instance.GetType().Assembly, guest.GetType().Assembly);
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"object_storage_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module,
            new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow()));
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
    }
}
