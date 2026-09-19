using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedBuiltInStaticDispatchRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void CheckedForwardDeclarationSupportsConsumersBeforeCompletion()
    {
        var runtime = new EmittedRuntime();
        var owner = runtime.BuiltInStatics;
        Assert.NotSame(owner, new EmittedRuntime().BuiltInStatics);
        Assert.Null(typeof(EmittedRuntime).GetProperty("BuiltInStatics")!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("LookupBuiltInStaticMember"));
        Assert.Throws<InvalidOperationException>(() => owner.Lookup);
        var property = typeof(EmittedBuiltInStaticDispatchRuntime).GetProperty("Lookup")!;
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.False(owner.IsComplete);

        var builder = NewAssembly();
        var type = builder.DefineDynamicModule("main").DefineType("Forward", TypeAttributes.Public);
        typeof(RuntimeEmitter).GetMethod("DefineLookupBuiltInStaticMember", Members)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner]);
        var forward = owner.Lookup;
        Assert.False(owner.IsComplete);
        Assert.Same(type, forward.DeclaringType);
        Expect<InvalidOperationException>(() => Complete(owner));
        Assert.False(owner.IsComplete);
        var undeclared = new EmittedRuntime().BuiltInStatics;
        Expect<InvalidOperationException>(() => MarkBody(undeclared));
        Assert.False(undeclared.IsComplete);
        Expect<InvalidOperationException>(() => property.SetValue(owner, forward));
        var consumer = type.DefineMethod("Consumer", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(Type), typeof(string)]);
        var il = consumer.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, forward); il.Emit(OpCodes.Ret);
        il = forward.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        MarkBody(owner);
        Expect<InvalidOperationException>(() => MarkBody(owner));
        Complete(owner);
        Assert.True(owner.IsComplete); Assert.Same(forward, owner.Lookup);
        Expect<InvalidOperationException>(() => property.SetValue(owner, forward));
        Expect<InvalidOperationException>(() => MarkBody(owner));
        Expect<InvalidOperationException>(() => Complete(owner));
        type.CreateType();
        var loaded = SaveVerifyLoad(builder);
        Assert.Null(loaded.GetType("Forward")!.GetMethod("Consumer")!.Invoke(null, [typeof(object), "missing"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesLookupIdentityArityAndOptionalFamilies(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedBuiltInStaticDispatchRuntime>();
        var handles = new HashSet<MethodBuilder>();
        foreach (int mask in new[] { 0, 1, 2, 4, 3, 5, 6, 7, 0 })
        {
            var builder = NewAssembly();
            var source = "const n=1;" + ((mask & 1) != 0 ? "BigInt(1);" : "")
                + ((mask & 2) != 0 ? "Promise.resolve(1);" : "") + ((mask & 4) != 0 ? "Date.now();" : "");
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            var owner = runtime.BuiltInStatics;
            Assert.True(owners.Add(owner)); Assert.True(handles.Add(owner.Lookup)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.Lookup.Module.Assembly); Assert.Same(runtime.RuntimeClass.Type, owner.Lookup.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var lookup = type.GetMethod("LookupBuiltInStaticMember")!;
            Assert.True(lookup.IsPublic && lookup.IsStatic); Assert.Equal(typeof(object), lookup.ReturnType);
            Assert.Equal(new[] { typeof(Type), typeof(string) }, lookup.GetParameters().Select(p => p.ParameterType));
            // GetProperty is declared in phase one; the lookup is declared before its body.
            Assert.True(lookup.MetadataToken > type.GetMethod(runtime.ObjectRead.Property.Name)!.MetadataToken);
            var getOrCreate = loaded.GetType(runtime.FunctionConstruction.GetOrCreate.DeclaringType!.FullName!)!
                .GetMethod(runtime.FunctionConstruction.GetOrCreate.Name)!;
            Type Resolve(Type t) => t.Assembly == builder ? loaded.GetType(t.FullName!)! : t;
            void Check(Type target, string name, MethodInfo backing, int length)
            {
                object wrapper = lookup.Invoke(null, [Resolve(target), name])!;
                Assert.NotNull(wrapper);
                Assert.Same(wrapper, lookup.Invoke(null, [Resolve(target), name]));
                var method = loaded.GetType(backing.DeclaringType!.FullName!)!.GetMethod(backing.Name)!;
                Assert.Same(wrapper, getOrCreate.Invoke(null, [method, name, length]));
                Assert.Equal(name, wrapper.GetType().GetProperty("Name")!.GetValue(wrapper));
                Assert.Equal(length, wrapper.GetType().GetProperty("Length")!.GetValue(wrapper));
            }
            Check(typeof(IList<object>), "isArray", runtime.ArrayOperations.IsArray, 1);
            Check(typeof(double), "isNaN", runtime.Numbers.IsNaN, 1);
            Check(typeof(double), "isFinite", runtime.Numbers.IsFinite, 1);
            Check(typeof(double), "isInteger", runtime.Numbers.IsInteger, 1);
            Check(typeof(double), "isSafeInteger", runtime.Numbers.IsSafeInteger, 1);
            Check(typeof(string), "fromCharCode", runtime.Strings.FromCharCode, 1);
            Check(typeof(string), "fromCodePoint", runtime.Strings.FromCodePoint, 1);
            Check(typeof(string), "raw", runtime.Templates.Raw, 1);
            Check(typeof(object), "keys", runtime.ObjectKeys.Keys, 1);
            Check(typeof(object), "assign", runtime.ObjectOperations.Assign, 2);
            Check(typeof(object), "defineProperty", runtime.ObjectDescriptors.DefineProperty, 3);
            Check(typeof(object), "create", runtime.ObjectPrototypes.CreateValueForm, 2);
            Check(runtime.Symbols.Type, "for", runtime.Symbols.For, 1);
            Check(runtime.Symbols.Type, "keyFor", runtime.Symbols.KeyFor, 1);
            Check(runtime.Errors.Type, "isError", runtime.Errors.IsError, 1);
            if (features.UsesBigInt)
            {
                Check(typeof(System.Numerics.BigInteger), "asIntN", runtime.BigInt.RequireImplementation().AsIntN, 2);
                Check(typeof(System.Numerics.BigInteger), "asUintN", runtime.BigInt.RequireImplementation().AsUintN, 2);
            }
            else Assert.Null(lookup.Invoke(null, [typeof(System.Numerics.BigInteger), "asIntN"]));
            // Hosted emission enables Promise infrastructure even for an otherwise minimal source.
            if (features.UsesPromise)
            {
                var promise = runtime.RequirePromise();
                Check(typeof(Task<object>), "resolve", promise.ResolveStatic, 1);
                Check(promise.Type, "resolve", promise.ResolveStatic, 1);
                Check(typeof(Task<object>), "all", promise.AllStatic, 1);
                Check(promise.Type, "allKeyed", promise.AllKeyedStatic, 1);
            }
            else Assert.Null(lookup.Invoke(null, [typeof(Task<object>), "resolve"]));
            if (features.UsesDate)
            {
                var date = runtime.Dates.RequireImplementation();
                Check(date.Type, "now", date.Now, 0);
                Check(date.Type, "UTC", date.StaticUTC, 7);
                Check(date.Type, "parse", date.StaticParse, 1);
            }
            foreach (Type target in new[] { typeof(object), typeof(string), typeof(double), typeof(Uri) })
                Assert.Null(lookup.Invoke(null, [target, "missing"]));
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedBodyUsesSuppliedHandlesAndOptionalOwnersInsteadOfGlobalFlags(bool optional)
    {
        var builder = NewAssembly();
        var type = builder.DefineDynamicModule("main").DefineType("Scoped", TypeAttributes.Public);
        MethodBuilder Stub(string name)
        {
            var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret); return method;
        }
        object Owner(Type ownerType, params string[] names)
        {
            var owner = Activator.CreateInstance(ownerType, nonPublic: true)!;
            foreach (string name in names) ownerType.GetProperty(name)!.SetValue(owner, Stub(ownerType.Name + "_" + name));
            return owner;
        }
        var numbers = Owner(typeof(EmittedNumberRuntime), "IsNaN", "IsFinite", "IsInteger", "IsSafeInteger");
        var keys = Owner(typeof(EmittedObjectKeysRuntime), "Keys", "Names", "Symbols");
        var operations = Owner(typeof(EmittedObjectOperationsRuntime), "Values", "Entries", "FromEntries", "Assign", "Is", "GroupBy");
        var state = Owner(typeof(EmittedObjectStateRuntime), "Freeze", "Seal", "PreventExtensions", "IsExtensible", "IsFrozen", "IsSealed");
        var prototypes = Owner(typeof(EmittedObjectPrototypeRuntime), "GetPrototypeOf", "SetPrototypeOf", "CreateValueForm");
        var descriptors = Owner(typeof(EmittedObjectDescriptorRuntime), "DefineProperty", "DefineProperties", "GetOwnPropertyDescriptor", "GetOwnPropertyDescriptors");
        object? bigInt = null, promise = null, date = null;
        if (optional)
        {
            bigInt = Owner(typeof(EmittedBigIntImplementation), "AsIntN", "AsUintN");
            promise = Owner(typeof(EmittedPromiseRuntime), "ResolveStatic", "RejectStatic", "AllStatic", "AllKeyedStatic", "RaceStatic", "AllSettledStatic", "AllSettledKeyedStatic", "AnyStatic");
            typeof(EmittedPromiseRuntime).GetProperty("Type")!.SetValue(promise, typeof(decimal));
            date = Owner(typeof(EmittedDateImplementation), "Now", "StaticUTC", "StaticParse");
            typeof(EmittedDateImplementation).GetProperty("Type")!.SetValue(date, type);
        }
        var inputsType = typeof(RuntimeEmitter).GetNestedType("BuiltInStaticDispatchInputs", BindingFlags.NonPublic)!;
        var input = Assert.Single(inputsType.GetConstructors()).Invoke([
            typeof(EmittedBuiltInStaticDispatchRuntimeTests).GetMethod(nameof(DescribeSuppliedMethod))!, Stub("ArrayCheck"), numbers,
            Stub("CharCode"), Stub("CodePoint"), Stub("Raw"), keys, operations, state, prototypes, descriptors, Stub("Own"),
            typeof(Uri), Stub("SymbolFor"), Stub("SymbolKeyFor"), bigInt, promise, typeof(Version), Stub("ErrorCheck"), date]);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetField("_features", Members)!.SetValue(emitter, new RuntimeFeatureSet { UsesPromise = !optional });
        var owner = new EmittedRuntime().BuiltInStatics;
        typeof(RuntimeEmitter).GetMethod("DefineLookupBuiltInStaticMember", Members)!.Invoke(emitter, [type, owner]);
        typeof(RuntimeEmitter).GetMethod("EmitLookupBuiltInStaticMemberBody", Members)!.Invoke(emitter, [owner, input]);
        Complete(owner); type.CreateType();
        var loaded = SaveVerifyLoad(builder); var lookup = loaded.GetType("Scoped")!.GetMethod("LookupBuiltInStaticMember")!;
        object? Lookup(Type target, string name) => lookup.Invoke(null, [target, name]);
        Assert.Equal("ArrayCheck:isArray:1", Lookup(typeof(IList<object>), "isArray"));
        Assert.Equal("CharCode:fromCharCode:1", Lookup(typeof(string), "fromCharCode"));
        Assert.Equal("EmittedObjectDescriptorRuntime_DefineProperty:defineProperty:3", Lookup(typeof(object), "defineProperty"));
        Assert.Equal("SymbolFor:for:1", Lookup(typeof(Uri), "for"));
        Assert.Equal("ErrorCheck:isError:1", Lookup(typeof(Version), "isError"));
        Assert.Null(Lookup(typeof(object), "missing"));
        if (optional)
        {
            Assert.Equal("EmittedBigIntImplementation_AsIntN:asIntN:2", Lookup(typeof(System.Numerics.BigInteger), "asIntN"));
            Assert.Equal("EmittedPromiseRuntime_ResolveStatic:resolve:1", Lookup(typeof(Task<object>), "resolve"));
            Assert.Equal("EmittedPromiseRuntime_ResolveStatic:resolve:1", Lookup(typeof(decimal), "resolve"));
            Assert.Equal("EmittedDateImplementation_StaticUTC:UTC:7", Lookup(loaded.GetType("Scoped")!, "UTC"));
        }
        else
        {
            Assert.Null(Lookup(typeof(System.Numerics.BigInteger), "asIntN"));
            Assert.Null(Lookup(typeof(Task<object>), "resolve"));
            Assert.Null(Lookup(loaded.GetType("Scoped")!, "UTC"));
        }
    }

    public static object DescribeSuppliedMethod(MethodInfo method, string name, int length) => method.Name + ":" + name + ":" + length;

    private static void Complete(EmittedBuiltInStaticDispatchRuntime owner) =>
        typeof(EmittedBuiltInStaticDispatchRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static void MarkBody(EmittedBuiltInStaticDispatchRuntime owner) =>
        typeof(EmittedBuiltInStaticDispatchRuntime).GetMethod("MarkLookupBodyEmitted", Members)!.Invoke(owner, null);
    private static void Expect<T>(Action action) where T : Exception =>
        Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() =>
        new(new AssemblyName($"builtin_static_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
