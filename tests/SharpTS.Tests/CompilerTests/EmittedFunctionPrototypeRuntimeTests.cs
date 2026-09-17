using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedFunctionPrototypeRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [Theory]
    [InlineData("Prototype")]
    [InlineData("Populate")]
    public void MetadataRequiresDeclarationAndFreezesAfterBodyCompletion(string name)
    {
        var owner = new EmittedFunctionPrototypeRuntime(); var property = typeof(EmittedFunctionPrototypeRuntime).GetProperty(name)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Handles");
        var field = type.DefineField("Prototype", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var method = type.DefineMethod("Populate", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        if (name == "Prototype") owner.Populate = method; else owner.Prototype = field;
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        object value = name == "Prototype" ? field : method;
        property.SetValue(owner, value); Assert.Same(value, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        owner.MarkPopulateBodyEmitted(); owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.MarkPopulateBodyEmitted);
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
    }

    [Fact]
    public void PopulateShellIsAvailableBeforeItsBodyAndCannotCompleteEarly()
    {
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("PrototypeShell", TypeAttributes.Public);
        var owner = new EmittedFunctionPrototypeRuntime();
        Assert.Throws<InvalidOperationException>(owner.MarkPopulateBodyEmitted);
        owner.Prototype = type.DefineField("Prototype", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        typeof(RuntimeEmitter).GetMethod("DefineFunctionPrototypePopulateShell", Members)!.Invoke(emitter, [type, owner]);
        var shell = owner.Populate; Assert.False(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        shell.GetILGenerator().Emit(OpCodes.Ret); owner.MarkPopulateBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.MarkPopulateBodyEmitted);
        owner.CompleteEmission(); Assert.Same(shell, owner.Populate);
        type.CreateType(); var loaded = SaveVerifyLoad(builder);
        loaded.GetType("PrototypeShell")!.GetMethod(shell.Name)!.Invoke(null, null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionKeepsPrototypeDictionariesAndWrappersIndependent(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>(); var dictionaries = new HashSet<object>(); var wrappers = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(a:number){return a;} f.bind(null,1)();", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.FunctionPrototypes; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            Assert.Same(builder, owner.Prototype.Module.Assembly); Assert.Same(builder, owner.Populate.Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var prototype = Assert.IsAssignableFrom<IDictionary>(type.GetField(owner.Prototype.Name)!.GetValue(null));
            Assert.True(dictionaries.Add(prototype)); Assert.Equal(5, prototype.Count);
            Assert.Same(loaded.GetType("$TSFunction"), prototype["constructor"]);
            foreach (var (name, length) in new[] { ("call", 1), ("apply", 2), ("bind", 1), ("toString", 0) })
            {
                var wrapper = prototype[name]!; Assert.True(wrappers.Add(wrapper));
                Assert.Equal(name, wrapper.GetType().GetProperty("Name")!.GetValue(wrapper));
                Assert.Equal(length, wrapper.GetType().GetProperty("Length")!.GetValue(wrapper));
                type.GetMethod(owner.Populate.Name)!.Invoke(null, null);
                Assert.Same(wrapper, prototype[name]); Assert.Equal(5, prototype.Count);
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void PrototypeOwnerAndHelpersUseExplicitDependencies()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.FunctionPrototypes, second.FunctionPrototypes);
        Assert.Null(typeof(EmittedRuntime).GetProperty("FunctionPrototypes")!.SetMethod);
        foreach (string old in new[] { "FunctionPrototypeField", "FunctionPrototypePopulateMethod" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "DefineFunctionPrototypePopulateShell", "EmitFunctionPrototypePopulate", "EmitFunctionProtoCallHelper", "EmitFunctionProtoApplyHelper", "EmitFunctionProtoBindHelper", "EmitFunctionProtoToStringHelper" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"function_prototype_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
