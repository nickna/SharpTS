using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedFunctionAttributesRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] Helpers = ["CapturesArguments", "PadUndefined", "FunctionLength", "FunctionName", "NumericRest4", "NonConstructible", "ExpectsThis"];
    public static IEnumerable<object[]> Handles => typeof(EmittedFunctionAttributesRuntime).GetProperties()
        .Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Handles))]
    public void HandlesAreRequiredSingleAssignmentAndFrozen(string name)
    {
        var source = new EmittedFunctionAttributesRuntime();
        Populate(NewAssembly().DefineDynamicModule("main"), source);
        var property = typeof(EmittedFunctionAttributesRuntime).GetProperty(name)!;
        var owner = new EmittedFunctionAttributesRuntime();
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.GetValue(owner)).InnerException);
        Assert.IsType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null)).InnerException);
        foreach (var item in typeof(EmittedFunctionAttributesRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType) && p.Name != name))
            item.SetValue(owner, item.GetValue(source));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        var handle = property.GetValue(source);
        property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, handle)).InnerException);
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, handle)).InnerException);
        Assert.Same(handle, property.GetValue(owner));
    }

    [Fact]
    public void AllSevenCreatedAttributesPreserveValuesAndOutputIdentity()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var owner = new EmittedFunctionAttributesRuntime(); Populate(module, owner); owner.CompleteEmission();
        var members = typeof(EmittedFunctionAttributesRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
        Assert.Equal(17, members.Length);
        foreach (var property in members)
        {
            var member = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner));
            Assert.Same(builder, member.Module.Assembly);
            if (member is TypeBuilder type) Assert.True(type.IsCreated());
        }
        var loaded = SaveVerifyLoad(builder);
        foreach (var name in Helpers)
        {
            var type = loaded.GetType("$" + name)!; Assert.Equal(typeof(Attribute), type.BaseType);
            object[] args = name == "FunctionLength" ? [3] : name is "FunctionName" or "NumericRest4" ? ["supplied"] : [];
            var value = Activator.CreateInstance(type, args)!;
            if (args.Length > 0)
                Assert.Equal(args[0], Assert.Single(type.GetFields(BindingFlags.Public | BindingFlags.Instance)).GetValue(value));
        }
        Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
    }

    public static IEnumerable<object[]> MarkerCases =>
        from name in new[] { "MarkPadsUndefined", "MarkFunctionLength", "MarkFunctionName", "MarkNonConstructible", "MarkExpectsThis" }
        from state in new[] { "absent", "complete", "incomplete" }
        select new object[] { name, state };

    [Theory]
    [MemberData(nameof(MarkerCases))]
    public void CompilerMarkersPreserveAbsentRuntimeAndRequirePresentDeclarations(string name, string state)
    {
        var compiler = new ILCompiler("attribute_marker"); var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var runtime = state == "absent" ? null : new EmittedRuntime();
        if (state == "complete") { Populate(module, runtime!.FunctionAttributes); runtime.FunctionAttributes.CompleteEmission(); }
        typeof(ILCompiler).GetField("_runtime", Members)!.SetValue(compiler, runtime);
        var type = module.DefineType("Probe", TypeAttributes.Public);
        var method = type.DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        object[] args = name == "MarkFunctionLength" ? [method, Array.Empty<Stmt.Parameter>()] : name == "MarkFunctionName" ? [method, "supplied"] : [method];
        var marker = typeof(ILCompiler).GetMethod(name, Members)!;
        if (state == "incomplete")
        {
            Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => marker.Invoke(compiler, args)).InnerException);
            return;
        }
        marker.Invoke(compiler, args); type.CreateType(); var loaded = SaveVerifyLoad(builder);
        var attributes = loaded.GetType("Probe")!.GetMethod("Value")!.GetCustomAttributes(false);
        if (state == "absent") Assert.Empty(attributes);
        else
        {
            var attribute = Assert.Single(attributes); var expected = name == "MarkPadsUndefined" ? "$PadUndefined" : "$" + name[4..];
            Assert.Equal(expected, attribute.GetType().Name);
            if (name == "MarkFunctionName") Assert.Equal("supplied", attribute.GetType().GetField("Name")!.GetValue(attribute));
            if (name == "MarkFunctionLength") Assert.Equal(0, attribute.GetType().GetField("Length")!.GetValue(attribute));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionCreatesFreshCompletedAttributes(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>();
        foreach (var code in new[] { "const n=1;", "function f(...xs:number[]){return xs.length;}f(1,2);", "const n=2;" })
        {
            var builder = NewAssembly(); var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features); var owner = runtime.FunctionAttributes;
            Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in typeof(EmittedFunctionAttributesRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
            foreach (var name in Helpers) Assert.NotNull(loaded.GetType("$" + name));
        }
    }

    [Fact]
    public void OwnerAndHelperBoundariesAreExplicit()
    {
        Assert.NotSame(new EmittedRuntime().FunctionAttributes, new EmittedRuntime().FunctionAttributes);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FunctionAttributes))!.SetMethod);
        foreach (var name in Helpers.Select(n => "Emit" + n + "Attribute").Concat(new[] { "EmitComputeExpectsThis", "EmitComputeCapturesArguments", "EmitComputeFunctionLength", "EmitComputeFunctionName", "EmitComputePadUndefinedMask", "EmitComputeNumericRest4" }))
        {
            var parameters = typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters();
            Assert.Contains(parameters, p => p.ParameterType == typeof(EmittedFunctionAttributesRuntime));
            Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        }
    }

    private static void Populate(ModuleBuilder module, EmittedFunctionAttributesRuntime owner)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        foreach (var name in Helpers)
            typeof(RuntimeEmitter).GetMethod("Emit" + name + "Attribute", Members)!.Invoke(emitter, [module, owner]);
    }
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"function_attributes_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
