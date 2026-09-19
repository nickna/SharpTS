using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedDescriptorStorageRuntimeTests
{
    private static PropertyInfo[] Handles => typeof(EmittedDescriptorStorageRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool)).ToArray();

    public static IEnumerable<object[]> RequiredHandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(RequiredHandleNames))]
    public void MissingDeclarationAllowsRepairAndCompletionFreezesEveryHandle(string name)
    {
        var owner = CreateDeclarations(name);
        var property = typeof(EmittedDescriptorStorageRuntime).GetProperty(name)!;
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
        var first = new EmittedRuntime().DescriptorStorage;
        Assert.NotSame(first, new EmittedRuntime().DescriptorStorage);
        Assert.False(first.IsComplete);
        Assert.Equal(35, Handles.Length);
        Assert.Equal(3, Handles.Count(property => property.PropertyType == typeof(Type)));
        Assert.Equal(10, Handles.Count(property => property.PropertyType == typeof(PropertyInfo)));
        Assert.Single(Handles, property => property.PropertyType == typeof(ConstructorInfo));
        Assert.Equal(21, Handles.Count(property => property.PropertyType == typeof(MethodBuilder)));
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.DescriptorStorage))!.SetMethod);
    }

    [Fact]
    public void FinalizedSupportTypesCanBeConsumedBeforeStorageMethodsAreDeclared()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("descriptor_staged"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var owner = new EmittedDescriptorStorageRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        foreach (var (method, count, typeProperty) in new (string, int, string)[]
        {
            ("EmitFrozenSealedStateClass", 4, "StateType"),
            ("EmitPrototypeInfoClass", 6, "PrototypeInfoType"),
            ("EmitCompiledPropertyDescriptorClass", 14, "DescriptorType")
        })
        {
            typeof(RuntimeEmitter).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(emitter, [module, owner]);
            var declared = Handles.Count(property =>
            {
                try { return property.GetValue(owner) is not null; }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return false; }
            });
            Assert.Equal(count, declared);
            var type = Assert.IsAssignableFrom<TypeBuilder>(typeof(EmittedDescriptorStorageRuntime).GetProperty(typeProperty)!.GetValue(owner));
            Assert.True(type.IsCreated());
            Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
            Assert.False(owner.IsComplete);
        }
        Assert.IsAssignableFrom<ConstructorBuilder>(owner.DescriptorConstructor);
        Assert.Same(owner.DescriptorType, owner.DescriptorConstructor.DeclaringType);
        foreach (var property in Handles.Where(property => property.PropertyType == typeof(MethodBuilder)))
        {
            var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        var caller = module.DefineType("DescriptorConsumer", TypeAttributes.Public);
        var factory = caller.DefineMethod("Create", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
        var il = factory.GetILGenerator();
        il.Emit(OpCodes.Newobj, owner.DescriptorConstructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, owner.DescriptorValue.GetSetMethod()!);
        il.Emit(OpCodes.Ret);
        caller.CreateType();
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        bytes.Position = 0;
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        Assert.Null(assembly.GetType("$PropertyDescriptorStore"));
        var value = new object();
        var descriptor = assembly.GetType("DescriptorConsumer")!.GetMethod("Create")!.Invoke(null, [value])!;
        Assert.Same(value, descriptor.GetType().GetProperty("Value")!.GetValue(descriptor));
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
    public void RequiredMetadataPreservesHostingIdentitySignaturesAndGuestStorage(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var owner = runtime.DescriptorStorage;
        AssertFrozen(owner);
        foreach (var property in Handles.Where(property => property.PropertyType == typeof(Type)))
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(property.GetValue(owner)).IsCreated());
        Assert.IsAssignableFrom<ConstructorBuilder>(owner.DescriptorConstructor);
        Assert.Same(owner.DescriptorType, owner.DescriptorConstructor.DeclaringType);
        foreach (var property in Handles.Where(property => property.PropertyType == typeof(PropertyInfo)))
            Assert.IsAssignableFrom<PropertyBuilder>(property.GetValue(owner));
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var store = assembly.GetType("$PropertyDescriptorStore")!;
        var fields = store.GetFields(BindingFlags.NonPublic | BindingFlags.Static).OrderBy(field => field.MetadataToken).ToArray();
        Assert.Equal(new[] { "_descriptors", "_frozenSealedState", "_symbolStorage", "_prototypeStore" }, fields.Select(field => field.Name));
        Assert.All(fields, field => Assert.True(field.IsPrivate && field.IsStatic && field.IsInitOnly));
        var descriptorType = assembly.GetType("$CompiledPropertyDescriptor")!;
        var methods = new (string Name, Type Result, Type[] Parameters)[]
        {
            ("Freeze", typeof(void), [typeof(object)]), ("Seal", typeof(void), [typeof(object)]),
            ("PreventExtensions", typeof(void), [typeof(object)]), ("IsExtensible", typeof(bool), [typeof(object)]),
            ("IsFrozen", typeof(bool), [typeof(object)]), ("IsSealed", typeof(bool), [typeof(object)]),
            ("CanAddProperty", typeof(bool), [typeof(object), typeof(string)]),
            ("TryGetGetter", typeof(bool), [typeof(object), typeof(string), typeof(object).MakeByRefType()]),
            ("TryGetSetter", typeof(bool), [typeof(object), typeof(string), typeof(object).MakeByRefType()]),
            ("IsWritable", typeof(bool), [typeof(object), typeof(string)]),
            ("SetPrototype", typeof(void), [typeof(object), typeof(object)]), ("GetPrototype", typeof(object), [typeof(object)]),
            ("HasPrototypeEntry", typeof(bool), [typeof(object)]),
            ("DefineProperty", typeof(bool), [typeof(object), typeof(string), descriptorType]),
            ("DeleteProperty", typeof(bool), [typeof(object), typeof(string)]),
            ("GetPropertyDescriptor", descriptorType, [typeof(object), typeof(string)]),
            ("HasPropertyDescriptors", typeof(bool), [typeof(object)]),
            ("HasIndexedOwnProperty", typeof(bool), [typeof(object), typeof(int)]),
            ("GetStaticShadow", descriptorType, [typeof(object), typeof(string)]),
            ("GetEnumerableExtraKeys", typeof(List<object>), [typeof(object), typeof(Dictionary<string, object>)]),
            ("GetAllExtraKeys", typeof(List<object>), [typeof(object), typeof(Dictionary<string, object>)])
        };
        var tokens = new List<int>();
        foreach (var (name, result, parameters) in methods)
        {
            var method = store.GetMethod(name)!;
            Assert.True(method.IsPublic && method.IsStatic);
            Assert.Equal(result, method.ReturnType);
            Assert.Equal(parameters, method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            tokens.Add(method.MetadataToken);
        }
        Assert.Equal(tokens.OrderBy(token => token).ToArray(), tokens);
        AssertGuestStorage(assembly, new Dictionary<string, object> { ["ordinary"] = 1d }, []);
        AssertCanonicalKeysAndStaticShadows(assembly, new object(), new object());
    }

    [Fact]
    public void ReusedEmitterKeepsDeclarationsAndAllFourMutableTablesInTheirOwnSavedAssemblies()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var owners = new List<EmittedDescriptorStorageRuntime>();
        var tables = new List<object>();
        var sharedKey = new Dictionary<string, object> { ["ordinary"] = 1d };
        var sharedReceiver = new object();
        var sharedSymbol = new object();
        // JSON reuse has an independent tracked cache-lifetime defect.
        foreach (var source in new[] { "/x/;1n;new Date();", "const value=1;", "/x/;1n;new Date();" })
        {
            var runtime = EmitRuntime(source, emitter: emitter);
            Assert.DoesNotContain(runtime.DescriptorStorage, owners);
            owners.Add(runtime.DescriptorStorage);
            AssertFrozen(runtime.DescriptorStorage);
            using var bytes = Save(runtime);
            Verify(bytes);
            var assembly = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("descriptor_storage_", StringComparison.Ordinal));
            AssertGuestStorage(assembly, sharedKey, tables);
            AssertCanonicalKeysAndStaticShadows(assembly, sharedReceiver, sharedSymbol);
        }
    }

    private static void AssertGuestStorage(Assembly assembly, Dictionary<string, object> sharedKey, List<object> tables)
    {
        var type = assembly.GetType("$PropertyDescriptorStore")!;
        var descriptorType = assembly.GetType("$CompiledPropertyDescriptor")!;
        foreach (string field in new[] { "_descriptors", "_frozenSealedState", "_symbolStorage", "_prototypeStore" })
        {
            var metadata = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.True(metadata.IsPrivate && metadata.IsStatic && metadata.IsInitOnly, "CWT flags");
            var table = metadata.GetValue(null)!;
            Assert.True(!tables.Any(old => ReferenceEquals(old, table)), "Fresh per-output CWT " + field);
            tables.Add(table);
        }
        Assert.True(Call(type, "GetPropertyDescriptor", sharedKey, "value") is null, "No descriptor leak");
        foreach (var name in new[] { "TryGetGetter", "TryGetSetter" })
        {
            object?[] missingArguments = [sharedKey, "missing", new object()];
            Assert.Equal(false, Call(type, name, missingArguments));
            Assert.Null(missingArguments[2]);
        }
        Assert.True(Equals(Call(type, "HasPropertyDescriptors", sharedKey), false), "No descriptor table leak");
        Assert.True(Equals(Call(type, "HasPrototypeEntry", sharedKey), false), "No prototype leak");
        Assert.True(Call(type, "GetPrototype", sharedKey) is null, "No prototype entry");
        Assert.True(Equals(Call(type, "IsFrozen", sharedKey), false) && Equals(Call(type, "IsSealed", sharedKey), false)
            && Equals(Call(type, "IsExtensible", sharedKey), true), "Default extension state");
        var descriptor = Activator.CreateInstance(descriptorType)!;
        foreach (string flag in new[] { "Writable", "Enumerable", "Configurable" })
            Assert.True(Equals(descriptorType.GetProperty(flag)!.GetValue(descriptor), true), "Native descriptor default " + flag);
        descriptorType.GetProperty("Value")!.SetValue(descriptor, 2d);
        Assert.True(Equals(Call(type, "DefineProperty", sharedKey, "value", descriptor), true), "DefineProperty");
        Assert.True(ReferenceEquals(Call(type, "GetPropertyDescriptor", sharedKey, "value"), descriptor), "Descriptor identity");
        descriptorType.GetProperty("Value")!.SetValue(descriptor, 3d);
        Assert.True(Equals(descriptorType.GetProperty("Value")!.GetValue(Call(type, "GetPropertyDescriptor", sharedKey, "value")), 3d), "Mutable descriptor identity");
        var getter = new object();
        var setter = new object();
        descriptorType.GetProperty("Getter")!.SetValue(descriptor, getter);
        descriptorType.GetProperty("Setter")!.SetValue(descriptor, setter);
        object?[] getterArgs = [sharedKey, "value", null];
        object?[] setterArgs = [sharedKey, "value", null];
        Assert.True(Equals(Call(type, "TryGetGetter", getterArgs), true) && ReferenceEquals(getterArgs[2], getter), "Getter out parameter");
        Assert.True(Equals(Call(type, "TryGetSetter", setterArgs), true) && ReferenceEquals(setterArgs[2], setter), "Setter out parameter");
        descriptorType.GetProperty("Getter")!.SetValue(descriptor, assembly.GetType("$Undefined")!.GetField("Instance")!.GetValue(null));
        Assert.True(Equals(Call(type, "TryGetGetter", getterArgs), false) && getterArgs[2] is null, "Undefined getter sentinel");
        Assert.True(((IEnumerable<object>)Call(type, "GetEnumerableExtraKeys", sharedKey, sharedKey)!).SequenceEqual(new object[] { "value" }), "Enumerable extras");
        descriptorType.GetProperty("Enumerable")!.SetValue(descriptor, false);
        Assert.True(!((IEnumerable<object>)Call(type, "GetEnumerableExtraKeys", sharedKey, sharedKey)!).Any(), "Non-enumerable filtering");
        Assert.True(((IEnumerable<object>)Call(type, "GetAllExtraKeys", sharedKey, sharedKey)!).SequenceEqual(new object[] { "value" }), "All extras");
        descriptorType.GetProperty("Configurable")!.SetValue(descriptor, false);
        Assert.True(Equals(Call(type, "DeleteProperty", sharedKey, "value"), true), "Native deletion is unconditional");
        Assert.True(Call(type, "GetPropertyDescriptor", sharedKey, "value") is null, "Deleted descriptor absent");
        Assert.True(Equals(Call(type, "HasPropertyDescriptors", sharedKey), true), "Conservative empty-table presence");
        var indexed = new Dictionary<string, object> { ["2"] = 1d };
        Assert.True(Equals(Call(type, "HasIndexedOwnProperty", indexed, 2), false) && Equals(Call(type, "HasIndexedOwnProperty", indexed, 3), true), "Exclusive index length");
        foreach (var (key, expected) in new[] { ("0", true), ("01", true), ("+1", true), (" 1 ", true), ("-1", false), ("4294967296", false), ("x", false) })
            Assert.Equal(expected, Call(type, "HasIndexedOwnProperty", new Dictionary<string, object> { [key] = 1d }, 3));
        Call(type, "SetPrototype", sharedKey, null);
        Assert.True(Equals(Call(type, "HasPrototypeEntry", sharedKey), true) && Call(type, "GetPrototype", sharedKey) is null, "Explicit null prototype");
        var prototype = new object();
        Call(type, "SetPrototype", sharedKey, prototype);
        Assert.True(ReferenceEquals(Call(type, "GetPrototype", sharedKey), prototype), "Prototype identity");
        Call(type, "PreventExtensions", sharedKey);
        Assert.True(Equals(Call(type, "CanAddProperty", sharedKey, "ordinary"), true), "Existing dictionary property");
        Assert.True(Equals(Call(type, "DefineProperty", sharedKey, "new", descriptor), false), "Closed new property");
        Call(type, "Seal", sharedKey);
        Assert.True(Equals(Call(type, "IsSealed", sharedKey), true) && Equals(Call(type, "IsFrozen", sharedKey), false), "Seal state");
        Call(type, "Freeze", sharedKey);
        Assert.True(Equals(Call(type, "IsFrozen", sharedKey), true) && Equals(Call(type, "IsWritable", sharedKey, "ordinary"), false), "Freeze state");
    }

    private static void AssertCanonicalKeysAndStaticShadows(Assembly assembly, object sharedReceiver, object sharedSymbol)
    {
        var sharedMethod = typeof(Math).GetMethod(nameof(Math.Abs), [typeof(double)])!;
        var otherMethod = typeof(Math).GetMethod(nameof(Math.Sign), [typeof(double)])!;
        var store = assembly.GetType("$PropertyDescriptorStore")!;
        var descriptorType = assembly.GetType("$CompiledPropertyDescriptor")!;
        var functionType = assembly.GetType("$TSFunction")!;
        var ctor = functionType.GetConstructor([typeof(object), typeof(MethodInfo)])!;
        var wrapper1 = ctor.Invoke([null, sharedMethod]);
        var wrapper2 = ctor.Invoke([null, sharedMethod]);
        var different = ctor.Invoke([null, otherMethod]);
        Assert.True(!ReferenceEquals(wrapper1, wrapper2), "Distinct wrappers");
        Assert.True(Call(store, "GetPropertyDescriptor", sharedMethod, "value") is null, "No shared MethodInfo state from earlier output");
        var descriptor = Activator.CreateInstance(descriptorType)!;
        Assert.True(Equals(Call(store, "DefineProperty", wrapper1, "value", descriptor), true), "Define through first wrapper");
        Assert.True(ReferenceEquals(Call(store, "GetPropertyDescriptor", wrapper2, "value"), descriptor), "Equivalent wrapper shares descriptor key");
        Assert.True(ReferenceEquals(Call(store, "GetPropertyDescriptor", sharedMethod, "value"), descriptor), "Canonical MethodInfo key");
        Assert.True(Call(store, "GetPropertyDescriptor", different, "value") is null, "Different method key");
        var methodless1 = ctor.Invoke([null, null]);
        var methodless2 = ctor.Invoke([null, null]);
        Assert.True(Equals(Call(store, "DefineProperty", methodless1, "value", descriptor), true), "Methodless first wrapper");
        Assert.True(ReferenceEquals(Call(store, "GetPropertyDescriptor", methodless1, "value"), descriptor), "Methodless wrapper retains own key");
        Assert.True(Call(store, "GetPropertyDescriptor", methodless2, "value") is null, "Methodless wrappers remain separate");
        Assert.True(Equals(Call(store, "DeleteProperty", wrapper2, "value"), true), "Delete through equivalent wrapper");
        Assert.True(Call(store, "GetPropertyDescriptor", wrapper1, "value") is null, "Canonical deletion visible");

        var baseType = typeof(FileSystemInfo);
        var childType = typeof(FileInfo);
        Assert.True(Call(store, "GetStaticShadow", childType, "shadow") is null, "No static shadow leaked from earlier output");
        var baseDescriptor = Activator.CreateInstance(descriptorType)!;
        var ownDescriptor = Activator.CreateInstance(descriptorType)!;
        Assert.True(Equals(Call(store, "DefineProperty", baseType, "shadow", baseDescriptor), true), "Base static shadow");
        Assert.True(ReferenceEquals(Call(store, "GetStaticShadow", childType, "shadow"), baseDescriptor), "Inherited nearest shadow");
        Assert.True(Equals(Call(store, "DefineProperty", childType, "shadow", ownDescriptor), true), "Own static shadow");
        Assert.True(ReferenceEquals(Call(store, "GetStaticShadow", childType, "shadow"), ownDescriptor), "Own shadow wins");
        Assert.True(ReferenceEquals(Call(store, "GetStaticShadow", baseType, "shadow"), baseDescriptor), "Base shadow unchanged");
        Assert.True(Call(store, "GetStaticShadow", new object(), "shadow") is null, "Non-Type receiver");
        Assert.True(Equals(Call(store, "DeleteProperty", childType, "shadow"), true), "Delete own shadow");
        Assert.True(ReferenceEquals(Call(store, "GetStaticShadow", childType, "shadow"), baseDescriptor), "Inherited shadow restored");

        var symbolTable = (ConditionalWeakTable<object, Dictionary<object, object>>)store
            .GetField("_symbolStorage", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.True(!symbolTable.TryGetValue(sharedReceiver, out _), "Fresh BCL-only symbol table for same CLR receiver");
        var symbols = symbolTable.GetOrCreateValue(sharedReceiver);
        symbols[sharedSymbol] = descriptor;
        Assert.True(ReferenceEquals(symbolTable.GetOrCreateValue(sharedReceiver)[sharedSymbol], descriptor), "Mutable symbol table identity");
    }

    private static EmittedDescriptorStorageRuntime CreateDeclarations(string? omission = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"descriptor_storage_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("DescriptorDeclarations");
        var owner = new EmittedDescriptorStorageRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omission) continue;
            object value;
            if (property.PropertyType == typeof(Type)) value = type;
            else if (property.PropertyType == typeof(ConstructorInfo))
                value = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            else if (property.PropertyType == typeof(PropertyInfo))
                value = type.DefineProperty(property.Name, PropertyAttributes.None, typeof(object), Type.EmptyTypes);
            else
                value = type.DefineMethod(property.Name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            property.SetValue(owner, value);
        }
        return owner;
    }

    private static void AssertFrozen(EmittedDescriptorStorageRuntime owner)
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

    private static object? Call(Type type, string name, params object?[] arguments) =>
        type.GetMethod(name)!.Invoke(null, arguments);

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"descriptor_storage_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
