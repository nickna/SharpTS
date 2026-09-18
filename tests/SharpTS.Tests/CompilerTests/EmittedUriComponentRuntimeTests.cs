using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedUriComponentRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly PropertyInfo[] Handles = typeof(EmittedUriComponentRuntime).GetProperties()
        .Where(p => p.PropertyType == typeof(MethodBuilder)).ToArray();
    public static IEnumerable<object[]> HandleNames => Handles.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void DeclarationsRejectMissingNullAndDuplicateValues(string name)
    {
        var owner = new EmittedRuntime().UriComponents;
        var property = Handles.Single(p => p.Name == name);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
        Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null)).InnerException);
        var value = Handle(); property.SetValue(owner, value);
        Assert.Same(value, property.GetValue(owner));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value)).InnerException);
        Assert.False(owner.IsComplete);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void FailedCompletionCanBeRepairedAndCompletedMetadataIsFrozen(string missing)
    {
        var owner = new EmittedRuntime().UriComponents;
        foreach (var property in Handles.Where(p => p.Name != missing)) property.SetValue(owner, Handle());
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        Assert.False(owner.IsComplete);
        Handles.Single(p => p.Name == missing).SetValue(owner, Handle()); Complete(owner);
        Assert.True(owner.IsComplete);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => Complete(owner)).InnerException);
        foreach (var property in Handles)
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, Handle())).InnerException);
    }

    [Fact]
    public void HelperAcceptsOnlyItsBuilderOwnerAndExactConversionInputs()
    {
        Assert.Equal(2, Handles.Length);
        Assert.NotSame(new EmittedRuntime().UriComponents, new EmittedRuntime().UriComponents);
        Assert.Null(typeof(EmittedRuntime).GetProperty("UriComponents")!.SetMethod);
        Assert.Null(typeof(EmittedRuntime).GetProperty("GlobalEncodeURIComponent"));
        Assert.Null(typeof(EmittedRuntime).GetProperty("GlobalDecodeURIComponent"));
        var input = typeof(RuntimeEmitter).GetNestedType("UriComponentInputs", BindingFlags.NonPublic)!;
        Assert.Equal(new[] { typeof(TypeBuilder), typeof(EmittedUriComponentRuntime), input },
            typeof(RuntimeEmitter).GetMethod("EmitUriComponentFunctions", Members)!.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(new[] { "PadUndefinedCtor", "ToJsString" }, input.GetProperties().Select(p => p.Name));
        Assert.Equal(typeof(ConstructorBuilder), input.GetProperty("PadUndefinedCtor")!.PropertyType);
        Assert.Equal(typeof(MethodBuilder), input.GetProperty("ToJsString")!.PropertyType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedHelperUsesSuppliedCoercionAndAttributePresence(bool hasAttribute)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        ConstructorBuilder? attribute = null;
        if (hasAttribute)
        {
            var attr = module.DefineType("PadProbeAttribute", TypeAttributes.Public | TypeAttributes.Sealed, typeof(Attribute));
            attribute = attr.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var ctorIl = attribute.GetILGenerator(); ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(Members, null, Type.EmptyTypes, null)!);
            ctorIl.Emit(OpCodes.Ret); attr.CreateType();
        }
        var type = module.DefineType("UriProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var coercion = type.DefineMethod("SuppliedCoercion", MethodAttributes.Public | MethodAttributes.Static, typeof(string), [typeof(object)]);
        var il = coercion.GetILGenerator(); il.Emit(OpCodes.Ldstr, "a%20b"); il.Emit(OpCodes.Ret);
        var owner = new EmittedRuntime().UriComponents;
        var input = typeof(RuntimeEmitter).GetNestedType("UriComponentInputs", BindingFlags.NonPublic)!;
        var arguments = Activator.CreateInstance(input, [attribute, coercion]);
        typeof(RuntimeEmitter).GetMethod("EmitUriComponentFunctions", Members)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, owner, arguments]);
        Assert.False(owner.IsComplete); Assert.Same(type, owner.Encode.DeclaringType); Assert.Same(type, owner.Decode.DeclaringType);
        Complete(owner); type.CreateType();
        var loaded = SaveVerifyLoad(builder).GetType("UriProbe")!;
        var encode = loaded.GetMethod("GlobalEncodeURIComponent")!;
        var decode = loaded.GetMethod("GlobalDecodeURIComponent")!;
        Assert.Equal("a%2520b", encode.Invoke(null, [7.0])); Assert.Equal("a b", decode.Invoke(null, [7.0]));
        foreach (var method in new[] { encode, decode })
        {
            VerifyMethod(method);
            Assert.Equal(hasAttribute, method.GetCustomAttributesData().Any(a => a.AttributeType.Name == "PadProbeAttribute"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterOwnsFreshUriMethodsAndPreservesDeployment(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedUriComponentRuntime>(); var methods = new HashSet<MethodBuilder>();
        foreach (string source in new[] { "const n=1;", "encodeURIComponent('a b');new Map();Buffer.from('x');", "const n=2;" })
        {
            var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var runtime = emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
            var owner = runtime.UriComponents;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            Assert.True(methods.Add(owner.Encode)); Assert.True(methods.Add(owner.Decode));
            Assert.Same(builder, owner.Encode.Module.Assembly); Assert.Same(owner.Encode.DeclaringType, owner.Decode.DeclaringType);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType(owner.Encode.DeclaringType!.FullName!)!;
            var encode = type.GetMethod(owner.Encode.Name)!; var decode = type.GetMethod(owner.Decode.Name)!;
            Assert.True(encode.MetadataToken < decode.MetadataToken);
            foreach (var method in new[] { encode, decode })
            {
                VerifyMethod(method);
                Assert.Contains(method.GetCustomAttributesData(), a => a.AttributeType.Name == runtime.FunctionAttributes.PadUndefinedCtor.DeclaringType!.Name);
            }
            var undefined = loaded.GetType("$Undefined")!.GetField("Instance")!.GetValue(null);
            object?[] values = ["a b", "", null, undefined, 42.0, true];
            string[] encoded = ["a%20b", "", "null", "undefined", "42", "true"];
            string[] decoded = ["a b", "", "null", "undefined", "42", "true"];
            for (int i = 0; i < values.Length; i++)
            {
                Assert.Equal(encoded[i], encode.Invoke(null, [values[i]]));
                Assert.Equal(decoded[i], decode.Invoke(null, [encoded[i]]));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    private static void VerifyMethod(MethodInfo method)
    {
        Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(typeof(string), method.ReturnType);
        Assert.Equal(typeof(object), Assert.Single(method.GetParameters()).ParameterType);
    }
    private static MethodBuilder Handle() => NewAssembly().DefineDynamicModule("main")
        .DefineType("Handles").DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static, typeof(string), [typeof(object)]);
    private static void Complete(EmittedUriComponentRuntime owner) => typeof(EmittedUriComponentRuntime).GetMethod("CompleteEmission", Members)!.Invoke(owner, null);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"uri_components_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
