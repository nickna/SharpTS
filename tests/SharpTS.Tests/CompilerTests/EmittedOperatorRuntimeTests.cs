using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedOperatorRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type[] OwnerTypes = [typeof(EmittedOperatorRuntime)];
    private static PropertyInfo[] Handles(Type type) => type.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => OwnerTypes.SelectMany(type => Handles(type).Select(p => new object[] { type, p.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesMetadata(Type type, string missing)
    {
        var owner = Activator.CreateInstance(type, nonPublic: true)!; var property = type.GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(owner, missing);
        Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission")); Assert.False(IsComplete(owner));
        var handle = Handle(property); property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
        Invoke(owner, "MarkEqualityBodyEmitted"); Invoke(owner, "CompleteEmission"); AssertFrozen(owner);
    }

    [Fact]
    public void RequiredOwnerBelongsToOneCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.Operators, second.Operators);
        Assert.False(first.Operators.IsComplete); Assert.False(second.Operators.IsComplete);
        Assert.Equal(11, Handles(typeof(EmittedOperatorRuntime)).Length);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Operators))!.SetMethod);
    }

    [Fact]
    public void ForwardDeclarationMustHaveOneBodyBeforeCompletion()
    {
        var owner = new EmittedOperatorRuntime();
        Assert.Throws<InvalidOperationException>(owner.MarkEqualityBodyEmitted);
        Fill(owner);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        owner.MarkEqualityBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.MarkEqualityBodyEmitted);
        owner.CompleteEmission();
        Assert.Throws<InvalidOperationException>(owner.MarkEqualityBodyEmitted);
        AssertFrozen(owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionPreservesOperatorsAndPerOutputStorage(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        foreach (bool optional in new[] { false, true, false, true })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(optional));
            Assert.True(owners.Add(runtime.Operators)); AssertFrozen(runtime.Operators);
            foreach (var property in Handles(typeof(EmittedOperatorRuntime)))
                Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(runtime.Operators)).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("operators_runtime_", StringComparison.Ordinal));
            var rt=loaded.GetType("$Runtime")!;
            object? Call(string methodName,params object?[] args)=>rt.GetMethod(methodName,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
            var names=new[]{"JsLessThan","JsLessOrEqual","UpdateNumeric","TypeOf","Add","Equals","StrictEquals","InstanceOf","HasIn","ProxyOrdinaryHas","WarnSloppyDeleteVariable"};
            var tokens=names.Select(n=>rt.GetMethod(n, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.MetadataToken).ToArray();
            Assert.True(tokens.SequenceEqual(tokens.Order()),"Operator declaration order changed");
            Assert.True(Equals(Call("UpdateNumeric","4",true),5d)&&Equals(Call("UpdateNumeric",new System.Numerics.BigInteger(4),false),new System.Numerics.BigInteger(3)),"Numeric update changed");
            Assert.True(Equals(Call("JsLessThan","10","2"),true)&&Equals(Call("JsLessOrEqual",2d,2d),true),"Relational comparison changed");
            Assert.True(Equals(Call("JsLessThan",double.NaN,1d),false)&&Equals(Call("JsLessOrEqual",double.NaN,1d),false),"Unordered comparison changed");
            Assert.True(Equals(Call("Add",2d,3d),5d)&&Equals(Call("Add","x",2d),"x2")&&Equals(Call("Add",new System.Numerics.BigInteger(2),new System.Numerics.BigInteger(3)),new System.Numerics.BigInteger(5)),"Addition changed");
            Assert.True(Equals(Call("Equals",null,System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(loaded.GetType(runtime.Sentinels.UndefinedType.FullName!)!)),true)&&Equals(Call("StrictEquals",1d,"1"),false)&&Equals(Call("StrictEquals",double.NaN,double.NaN),false),"Equality changed");
            Assert.True(Equals(Call("TypeOf",new object?[]{null}),"object")&&Equals(Call("TypeOf",1d),"number")&&Equals(Call("TypeOf","x"),"string")&&Equals(Call("TypeOf",new System.Numerics.BigInteger(1)),"bigint"),"Type classification changed");
            var symbol=Activator.CreateInstance(loaded.GetType("$TSSymbol")!,new object?[]{"operator"})!;
            Assert.True(Equals(Call("TypeOf",symbol),"symbol"),"Symbol classification changed");
            var receiver=new Dictionary<string,object?>{{"x",1d}};
            Assert.True(Equals(Call("HasIn","x",receiver),true)&&Equals(Call("HasIn","y",receiver),false)&&Equals(Call("ProxyOrdinaryHas",receiver,"x"),true),"Membership changed");
            receiver["y"]=2d;Assert.True(Equals(Call("HasIn","y",receiver),true),"Guest mutation was frozen with metadata");
            Assert.True(Equals(Call("InstanceOf",receiver,typeof(object)),true)&&Equals(Call("InstanceOf",1d,typeof(object)),false),"Object instance classification changed");
            if(optional)
            {
                var promise=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(loaded.GetType(runtime.Promise!.Type.FullName!)!);
                Assert.True(Equals(Call("InstanceOf",promise,typeof(Task<object>)),true),"Promise instance classification changed");
                foreach(var callbackType in new[]{runtime.Promise.ResolveCallbackType,runtime.Promise.RejectCallbackType})
                {
                    var callback=System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(loaded.GetType(callbackType.FullName!)!);
                    Assert.True(Equals(Call("TypeOf",callback),"function"),"Promise callback classification changed");
                }
            }
            Assert.True(Equals(Call("WarnSloppyDeleteVariable","operatorProbe"),false),"Sloppy delete result changed");

        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PromiseClassificationFollowsSuppliedMetadata(bool globalFlag, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main"); var features = Detect(true);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = emitter.EmitAll(module, features);
        features.UsesPromise = globalFlag;
        var probe = runtime.RuntimeClass.Type.DefineNestedType("SuppliedPromise", TypeAttributes.NestedPublic);
        var owner = new EmittedOperatorRuntime();
        foreach (var name in new[] { "EmitTypeOf", "EmitInstanceOf" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            var constructor = Assert.Single(method.GetParameters()[2].ParameterType.GetConstructors());
            var inputs = constructor.Invoke(constructor.GetParameters().Select(parameter => parameter.Name == "Promise"
                ? supplied ? runtime.Promise : null : (parameter.Name switch { "BoundAnyFunctionType" => runtime.FunctionBindings.AnyType, "BoundTSFunctionType" => runtime.FunctionBindings.BoundType, "FunctionApplyWrapperType" => runtime.FunctionBindings.ApplyType, "FunctionBindWrapperType" => runtime.FunctionBindings.BindType, "FunctionCallWrapperType" => runtime.FunctionBindings.CallType, "TSFunctionType" => runtime.FunctionValues.Type, _ => (parameter.Name switch { "GetFunctionMethod" => runtime.FunctionIntrospection.GetProperty, _ => (parameter.Name switch { "InvokeMethodUnwrapped" => runtime.ReflectedMethods.InvokeUnwrapped, "ToPascalCase" => runtime.ReflectedMethods.ToPascalCase, _ => (parameter.Name switch { "InvokeValue" => runtime.Invocation.Value, "InvokeMethodValue" => runtime.Invocation.Method, "InvokeMethodValue0" => runtime.Invocation.Method0, _ => (parameter.Name! switch { "IUnionTypeInterface" => runtime.UnionValues.Interface, "IUnionTypeValueGetter" => runtime.UnionValues.ValueGetter, "UndefinedType" => runtime.Sentinels.UndefinedType, "UndefinedInstance" => runtime.Sentinels.UndefinedInstance, _ => typeof(EmittedRuntime).GetProperty(parameter.Name!)!.GetValue(runtime) }) }) }) }) })).ToArray());
            method.Invoke(emitter, [probe, owner, inputs]);
        }
        probe.CreateType(); var loaded = SaveVerifyLoad(builder); var saved = loaded.GetType(probe.FullName!)!;
        var typeOf = saved.GetMethod("TypeOf")!; var instanceOf = saved.GetMethod("InstanceOf")!;
        foreach (var type in new[] { runtime.Promise!.ResolveCallbackType, runtime.Promise.RejectCallbackType })
        {
            Assert.Equal(supplied, ReadOperands(typeOf).Any(p => p.OpCode == OpCodes.Isinst && p.Member.Name == type.Name));
            var callback = RuntimeHelpers.GetUninitializedObject(loaded.GetType(type.FullName!)!);
            Assert.Equal(supplied ? "function" : "object", typeOf.Invoke(null, [callback]));
        }
        Assert.Equal(supplied, ReadOperands(instanceOf).Any(p => p.OpCode == OpCodes.Isinst && p.Member.Name == runtime.Promise.Type.Name));
        var promise = RuntimeHelpers.GetUninitializedObject(loaded.GetType(runtime.Promise.Type.FullName!)!);
        Assert.Equal(supplied, instanceOf.Invoke(null, [promise, typeof(Task<object>)]));
        Assert.Equal("number", typeOf.Invoke(null, [1d]));
        Assert.Equal(true, instanceOf.Invoke(null, [new Dictionary<string, object?>(), typeof(object)]));
    }

    public static IEnumerable<object[]> Comparisons => from operation in new[] { "lt", "gt", "le", "ge" }
        from wiring in new[] { "absent", "constructor", "late" } select new object[] { operation, wiring };

    [Theory]
    [MemberData(nameof(Comparisons))]
    public void StateMachineComparisonsPreserveRuntimeWiringAndFallback(string operation, string wiring)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var runtime = wiring == "absent" ? null : new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Detect(false));
        var probe = module.DefineType("Comparison", TypeAttributes.Public);
        var method = probe.DefineMethod("Compare", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(object), typeof(object)]);
        var il = method.GetILGenerator(); var helper = new StateMachineEmitHelpers(il, TypeProvider.Runtime, wiring == "constructor" ? runtime : null);
        if (wiring == "late") helper.SetRuntime(runtime!);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); EmitComparison(helper, operation);
        Assert.Equal(StackType.Boolean, helper.StackType); il.Emit(OpCodes.Ret); probe.CreateType();
        var saved = SaveVerifyLoad(builder).GetType("Comparison")!.GetMethod("Compare")!;
        bool ascending = operation is "lt" or "le";
        Assert.Equal(ascending, saved.Invoke(null, [2d, 10d]));
        Assert.Equal(operation is "le" or "ge", saved.Invoke(null, [2d, 2d]));
        Assert.Equal(wiring == "absent" ? ascending : !ascending, saved.Invoke(null, ["2", "10"]));
        var calls = ReadOperands(saved).Where(p => p.OpCode == OpCodes.Call).Select(p => p.Member.Name).ToArray();
        if (wiring == "absent") { Assert.Equal(2, calls.Count(n => n == "ToDouble")); Assert.DoesNotContain(calls, n => n.StartsWith("JsLess", StringComparison.Ordinal)); }
        else { Assert.Contains(operation is "lt" or "gt" ? "JsLessThan" : "JsLessOrEqual", calls); Assert.DoesNotContain("ToDouble", calls); }
    }

    [Theory]
    [InlineData("lt", "LessThan")]
    [InlineData("gt", "LessThan")]
    [InlineData("le", "LessThanOrEqual")]
    [InlineData("ge", "LessThanOrEqual")]
    public void SuppliedRuntimeRequiresDeclaredComparisonMetadata(string operation, string declaration)
    {
        var method = new DynamicMethod("Missing", typeof(bool), [typeof(object), typeof(object)]);
        var helper = new StateMachineEmitHelpers(method.GetILGenerator(), TypeProvider.Runtime, new EmittedRuntime());
        Assert.Contains(declaration, Assert.Throws<InvalidOperationException>(() => EmitComparison(helper, operation)).Message);
    }

    private static void EmitComparison(StateMachineEmitHelpers helper, string operation)
    {
        switch (operation)
        {
            case "lt": helper.EmitNumericComparison(OpCodes.Clt); break;
            case "gt": helper.EmitNumericComparison(OpCodes.Cgt); break;
            case "le": helper.EmitNumericComparisonLe(); break;
            case "ge": helper.EmitNumericComparisonGe(); break;
            default: throw new ArgumentException("Unknown comparison", nameof(operation));
        }
    }

    [Fact]
    public void ScopedHelpersDoNotRetainTheWholeRuntimeHolderOrFlatAliases()
    {
        foreach (var name in ScopedMethods)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
        foreach (var name in FormerHandles) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static readonly string[] ScopedMethods = ["EmitUpdateNumeric", "EmitJsLessThan", "EmitJsLessOrEqual", "EmitTypeOf", "EmitInstanceOf", "EmitHasIn", "EmitAdd", "DeclareEquals", "EmitEquals", "EmitStrictEquals", "EmitStrictModeHelpers", "EmitWarnSloppyDeleteVariable", "EmitProxyHasCheck", "EmitProxyHasResult"];
    private static readonly string[] FormerHandles = ["UpdateNumeric", "JsLessThan", "JsLessOrEqual", "TypeOf", "InstanceOf", "HasIn", "ProxyOrdinaryHas", "Add", "Equals", "StrictEquals", "WarnSloppyDeleteVariable"];
    private static void Fill(object owner, string? missing = null)
    {
        foreach (var property in Handles(owner.GetType()).Where(p => p.Name != missing)) property.SetValue(owner, Handle(property));
    }
    private static object Handle(PropertyInfo property)
    {
        if (property.PropertyType == typeof(Type)) return typeof(object);
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Declaration");
        return property.PropertyType == typeof(FieldBuilder) ? type.DefineField("Value", typeof(object), FieldAttributes.Public)
            : type.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
    }
    private static bool IsComplete(object owner) => (bool)owner.GetType().GetProperty("IsComplete")!.GetValue(owner)!;
    private static object? Invoke(object owner, string method) => owner.GetType().GetMethod(method, Members)!.Invoke(owner, null);
    private static void AssertFrozen(object owner)
    {
        Assert.True(IsComplete(owner)); Expect<InvalidOperationException>(() => Invoke(owner, "CompleteEmission"));
        foreach (var property in Handles(owner.GetType()))
        {
            Assert.False(property.SetMethod!.IsPublic); Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"operators_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(bool optional) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(optional
        ? "Promise.resolve(1); JSON.stringify({a:1}); new Date(0); /x/.test('x'); new Map(); new Set(); 1n; new Proxy({}, {}); new Uint8Array(2); new AbortController(); Buffer.from('x');" : "const value = 1;").ScanTokens()).ParseOrThrow());
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); var errors = verifier.Verify(bytes);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
    private static IEnumerable<(OpCode OpCode, MemberInfo Member)> ReadOperands(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            if (opCode.OperandType is OperandType.InlineType or OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)
                yield return (opCode, method.Module.ResolveMember(BitConverter.ToInt32(il, offset))!);
            offset += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException($"Unsupported IL operand type {opCode.OperandType}.")
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opCode => opCode.Value);
}
