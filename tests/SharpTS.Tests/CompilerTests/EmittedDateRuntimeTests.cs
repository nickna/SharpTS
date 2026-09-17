using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Compilation.CallHandlers;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedDateRuntimeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] InstanceMethodNames = ["GetTime", "GetFullYear", "GetMonth", "GetDate", "GetDay", "GetHours", "GetMinutes", "GetSeconds", "GetMilliseconds", "GetTimezoneOffset", "GetUTCFullYear", "GetUTCMonth", "GetUTCDate", "GetUTCDay", "GetUTCHours", "GetUTCMinutes", "GetUTCSeconds", "GetUTCMilliseconds", "GetYear", "SetTime", "SetFullYear", "SetMonth", "SetDate", "SetHours", "SetMinutes", "SetSeconds", "SetMilliseconds", "SetUTCFullYear", "SetUTCMonth", "SetUTCDate", "SetUTCHours", "SetUTCMinutes", "SetUTCSeconds", "SetUTCMilliseconds", "SetYear", "ToString", "ToISOString", "ToDateString", "ToTimeString", "ToUTCString", "ToLocaleDateString", "ToLocaleTimeString", "ToLocaleString", "ValueOf"];
    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => new[] { typeof(EmittedDateRuntime), typeof(EmittedDateImplementation) }
        .SelectMany(type => Handles(type).Select(property => new object[] { type, property.Name }));
    public static IEnumerable<object[]> InstanceMethods => InstanceMethodNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesBothOwners(Type type, string missing)
    {
        var root = new EmittedDateRuntime();
        root.BeginImplementationEmission();
        var date = root.RequireImplementation();
        object owner = type == typeof(EmittedDateRuntime) ? root : date;
        var property = type.GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(root, type == typeof(EmittedDateRuntime) ? missing : null);
        Fill(date, type == typeof(EmittedDateImplementation) ? missing : null);
        FillRegistry(date);
        root.MarkPrototypeBodyEmitted();
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete);
        Assert.False(date.IsComplete);
        property.SetValue(owner, Handle(property.PropertyType));
        root.CompleteEmission();
        AssertFrozen(root);
        AssertFrozen(date);
    }

    [Theory]
    [MemberData(nameof(InstanceMethods))]
    public void InstanceRegistryChecksMissingDuplicateAndFrozenDeclarations(string omitted)
    {
        var root = new EmittedDateRuntime();
        root.BeginImplementationEmission();
        var date = root.RequireImplementation();
        Fill(root); Fill(date); root.MarkPrototypeBodyEmitted();
        Assert.Throws<InvalidOperationException>(() => date.GetInstanceMethod(omitted));
        Assert.Throws<ArgumentNullException>(() => date.DeclareInstanceMethod(omitted, null!));
        Assert.Throws<ArgumentException>(() => date.DeclareInstanceMethod("UnknownMethod", Method()));
        FillRegistry(date, omitted);
        var view = Assert.IsAssignableFrom<IDictionary<string, MethodBuilder>>(date.InstanceMethods);
        Assert.Throws<NotSupportedException>(() => view.Add(omitted, Method()));
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete);
        Assert.False(date.IsComplete);
        var method = Method();
        date.DeclareInstanceMethod(omitted, method);
        Assert.Same(method, date.GetInstanceMethod(omitted));
        Assert.Throws<InvalidOperationException>(() => date.DeclareInstanceMethod(omitted, Method()));
        root.CompleteEmission();
        AssertFrozen(root); AssertFrozen(date);
        Assert.Throws<InvalidOperationException>(() => date.DeclareInstanceMethod(omitted, Method()));
        Assert.Throws<NotSupportedException>(() => view[omitted] = Method());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrototypeBodyIsRequiredEvenWithoutDateOperations(bool enabled)
    {
        var root = new EmittedDateRuntime(); Fill(root);
        Assert.Null(root.Implementation);
        Assert.Throws<InvalidOperationException>(root.RequireImplementation);
        if (enabled)
        {
            root.BeginImplementationEmission(); Fill(root.RequireImplementation()); FillRegistry(root.RequireImplementation());
            Assert.Throws<InvalidOperationException>(root.BeginImplementationEmission);
        }
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete);
        Assert.NotEqual(true, root.Implementation?.IsComplete);
        root.MarkPrototypeBodyEmitted();
        Assert.Throws<InvalidOperationException>(root.MarkPrototypeBodyEmitted);
        root.CompleteEmission(); AssertFrozen(root);
        Assert.Throws<InvalidOperationException>(root.BeginImplementationEmission);
        Assert.Throws<InvalidOperationException>(root.MarkPrototypeBodyEmitted);
    }

    [Fact]
    public void RequiredOwnerIsPerCompilationAndDateSelectionIsExplicit()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.Dates, second.Dates);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Dates))!.SetMethod);
        Assert.Equal(2, Handles(typeof(EmittedDateRuntime)).Length);
        Assert.Equal(58, Handles(typeof(EmittedDateImplementation)).Length);
        Assert.Null(first.Dates.Implementation);
        first.Dates.BeginImplementationEmission();
        Assert.NotNull(first.Dates.Implementation);
        Assert.Null(second.Dates.Implementation);
    }

    [Fact]
    public void DateClassPublishesConstructorsAndRegistryBeforeRuntimeWrappers()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var runtime = new EmittedRuntime();
        Invoke(emitter, "EmitNonConstructibleAttribute", module, runtime.FunctionAttributes);
        runtime.Dates.BeginImplementationEmission(); var date = runtime.Dates.RequireImplementation();
        Invoke(emitter, "EmitTSDateClass", module, date, runtime.FunctionAttributes.NonConstructibleCtor);
        Assert.True(date.Type.IsCreated());
        Assert.Equal(InstanceMethodNames.Order(StringComparer.Ordinal), date.InstanceMethods.Keys.Order(StringComparer.Ordinal));
        Assert.NotNull(date.NoArgsConstructor); Assert.NotNull(date.MillisecondsConstructor);
        Assert.NotNull(date.StringConstructor); Assert.NotNull(date.ComponentsConstructor);
        Assert.NotNull(date.StaticNow); Assert.NotNull(date.StaticUTC); Assert.NotNull(date.StaticParse);
        Assert.Throws<InvalidOperationException>(() => date.Now);
        Assert.Throws<InvalidOperationException>(() => date.GetTime);
        Assert.Throws<InvalidOperationException>(date.CompleteEmission);
        Assert.False(date.IsComplete);
        var loaded = SaveVerifyLoad(builder);
        var value = loaded.GetType("$TSDate")!.GetConstructor([typeof(double)])!.Invoke([0d]);
        Assert.Equal(0d, value.GetType().GetMethod("GetTime")!.Invoke(value, null));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PrototypePopulationFollowsProvidedImplementation(bool supplied, bool globalFlag)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var features = Detect("const value=new Date(0);");
        var runtime = emitter.EmitAll(module, features);
        features.UsesDate = globalFlag;
        var root = new EmittedDateRuntime();
        if (supplied)
        {
            root.BeginImplementationEmission(); var date = root.RequireImplementation();
            foreach (var property in Handles(typeof(EmittedDateImplementation))) property.SetValue(date, property.GetValue(runtime.Dates.RequireImplementation()));
            foreach (var (name, method) in runtime.Dates.RequireImplementation().InstanceMethods) date.DeclareInstanceMethod(name, method);
        }
        var type = module.DefineType("DatePrototypeProbe", TypeAttributes.Public);
        root.Prototype = type.DefineField("Prototype", typeof(Dictionary<string, object>), FieldAttributes.Public | FieldAttributes.Static);
        Invoke(emitter, "DefineDatePrototypePopulateShell", type, root);
        var methodInfo = typeof(RuntimeEmitter).GetMethod("EmitDatePrototypePopulate", InstanceMembers)!;
        var inputsType = methodInfo.GetParameters()[2].ParameterType;
        var inputs = Activator.CreateInstance(inputsType, runtime.DescriptorStorage, runtime.ObjectPrototypes.Prototype, runtime.TSFunctionGetOrCreate)!;
        methodInfo.Invoke(emitter, [type, root, inputs]); root.CompleteEmission(); type.CreateType();
        var loaded = SaveVerifyLoad(builder); var probe = loaded.GetType(type.Name)!;
        var dictionary = new Dictionary<string, object>(); probe.GetField("Prototype")!.SetValue(null, dictionary);
        var populate = probe.GetMethod(root.PopulatePrototype.Name)!;
        populate.Invoke(null, null);
        Assert.Equal(supplied ? 44 : 0, dictionary.Count);
        if (supplied)
        {
            Assert.True(dictionary.ContainsKey("constructor")); Assert.True(dictionary.ContainsKey("getTime"));
            dictionary["mutation"] = 7d; populate.Invoke(null, null); Assert.Equal(7d, dictionary["mutation"]);
        }
        else Assert.Equal(new byte[] { 0x2a }, populate.GetMethodBody()!.GetILAsByteArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterIsolatesDateMetadataAndPreservesGuestState(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<object>(); var types = new List<Type>();
        foreach (bool enabled in new[] { false, true, false, true })
        {
            var builder = NewAssembly();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(enabled ? "const value=new Date(0);" : "const value=1;"));
            Assert.DoesNotContain(runtime.Dates, owners); owners.Add(runtime.Dates); AssertFrozen(runtime.Dates);
            Assert.Equal(enabled, runtime.Dates.Implementation is not null);
            if (runtime.Dates.Implementation is { } date)
            {
                AssertFrozen(date); Assert.Equal(44, date.InstanceMethods.Count);
                foreach (var property in Handles(typeof(EmittedDateImplementation)))
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(date)).Module.Assembly);
            }
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("date_runtime_", StringComparison.Ordinal));
            var dateType = loaded.GetType("$TSDate"); Assert.Equal(enabled, dateType is not null);
            var runtimeType = loaded.GetType("$Runtime")!;
            var populate = runtimeType.GetMethod(runtime.Dates.PopulatePrototype.Name)!;
            var prototype = Assert.IsAssignableFrom<IDictionary>(runtimeType.GetField(runtime.Dates.Prototype.Name)!.GetValue(null));
            if (!enabled)
            {
                Assert.Empty(prototype); Assert.Equal(new byte[] { 0x2a }, populate.GetMethodBody()!.GetILAsByteArray());
                continue;
            }
            Assert.DoesNotContain(dateType!, types); types.Add(dateType!);
            Assert.Equal(4, dateType!.GetConstructors().Length);
            var value = dateType.GetConstructor([typeof(double)])!.Invoke([0d]);
            Assert.Equal(0d, Call(runtimeType, "DateGetTime", value));
            Assert.Equal("1970-01-01T00:00:00.000Z", Call(runtimeType, "DateToISOString", value));
            Assert.Equal(1000d, Call(runtimeType, "DateSetTime", value, 1000d));
            var clone = Call(runtimeType, "StructuredClone", value, null)!;
            Assert.NotSame(value, clone); Assert.Equal(dateType, clone.GetType());
            Assert.Equal(1000d, Call(runtimeType, "DateGetTime", clone));
            Call(runtimeType, "DateSetTime", value, 2000d);
            Assert.Equal(1000d, Call(runtimeType, "DateGetTime", clone));
            var invalid = dateType.GetConstructor([typeof(double)])!.Invoke([double.NaN]);
            Assert.True(double.IsNaN((double)Call(runtimeType, "DateGetTime", invalid)!));
            Assert.Equal("Invalid Date", Call(runtimeType, "DateToString", invalid));
            Assert.Equal(44, prototype.Count); prototype["mutation"] = 7d;
            populate.Invoke(null, null); Assert.Equal(7d, prototype["mutation"]);
        }
    }

    [Fact]
    public void FamilyBoundariesUseOwnedMetadataWithoutPersistentDateConstructionFields()
    {
        foreach (string name in new[] { "EmitTSDateClass", "EmitTSDateStaticConstructor", "EmitTSDateCtorNoArgs", "EmitTSDateCtorMilliseconds", "EmitTSDateCtorString", "EmitTSDateCtorComponents", "EmitTSDateNowStatic", "EmitTSDateUTCStatic", "EmitAddIntComponent", "EmitTSDateParseStatic", "EmitTSDateGetTime", "EmitTSDateGetFullYear", "EmitTSDateGetDate", "EmitTSDateGetHours", "EmitTSDateGetMinutes", "EmitTSDateGetSeconds", "EmitTSDateGetMilliseconds", "EmitSimpleDateGetter", "EmitTSDateGetTimezoneOffset", "EmitTSDateSetTime", "EmitDateComponentSetter", "EmitTSDateSetYear", "EmitTSDateToUTCString", "EmitTSDateLocaleString", "EmitTSDateToString", "EmitTSDateToISOString", "EmitTSDateToDateString", "EmitTSDateToTimeString", "EmitTSDateValueOf", "EmitDateMethods", "EmitDateToLocaleWithOptions", "EmitArgOrNull", "EmitDateDoubleGetter", "EmitDateDoubleArgSetter", "EmitDateArgsArraySetter", "EmitDateStringMethod", "EmitDateNow", "EmitCreateDateNoArgs", "EmitCreateDateFromValue", "EmitCreateDateFromComponents", "EmitDateToString", "EmitDateInstanceMethodCall", "EmitDateGetTime", "EmitDateGetFullYear", "EmitDateGetMonth", "EmitDateGetDate", "EmitDateGetDay", "EmitDateGetHours", "EmitDateGetMinutes", "EmitDateGetSeconds", "EmitDateGetMilliseconds", "EmitDateGetTimezoneOffset", "EmitDateSetTime", "EmitDateSetDate", "EmitDateSetMilliseconds", "EmitDateToISOString", "EmitDateToDateString", "EmitDateToTimeString", "EmitDateToJSON", "EmitDateValueOf", "DefineDatePrototypePopulateShell", "EmitDatePrototypePopulate" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, InstanceMembers | BindingFlags.Static)!;
            Assert.NotNull(method);
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        }
        foreach (string name in new[] { "DatePrototypeField", "DatePrototypePopulateMethod", "CreateDateFromComponents", "CreateDateFromValue", "CreateDateNoArgs", "DateGetDate", "DateGetDay", "DateGetFullYear", "DateGetHours", "DateGetMilliseconds", "DateGetMinutes", "DateGetMonth", "DateGetSeconds", "DateGetTime", "DateGetTimezoneOffset", "DateGetUTCDate", "DateGetUTCDay", "DateGetUTCFullYear", "DateGetUTCHours", "DateGetUTCMilliseconds", "DateGetUTCMinutes", "DateGetUTCMonth", "DateGetUTCSeconds", "DateGetYear", "DateNow", "DateSetDate", "DateSetFullYear", "DateSetHours", "DateSetMilliseconds", "DateSetMinutes", "DateSetMonth", "DateSetSeconds", "DateSetTime", "DateSetUTCDate", "DateSetUTCFullYear", "DateSetUTCHours", "DateSetUTCMilliseconds", "DateSetUTCMinutes", "DateSetUTCMonth", "DateSetUTCSeconds", "DateSetYear", "DateToDateString", "DateToISOString", "DateToJSON", "DateToLocaleDateString", "DateToLocaleString", "DateToLocaleTimeString", "DateToLocaleWithOptions", "DateToString", "DateToTimeString", "DateToUTCString", "DateValueOf", "TSDateCtorComponents", "TSDateCtorMilliseconds", "TSDateCtorNoArgs", "TSDateCtorString", "TSDateNowStatic", "TSDateParseStatic", "TSDateType", "TSDateUTCStatic", "TSDateMethods" }) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        foreach (string name in new[] { "_tsDateGetTimeMethod", "_tsDateUtcDateTimeField", "_tsDateIsInvalidField", "_tsDateUnixEpochField" })
            Assert.Null(typeof(RuntimeEmitter).GetField(name, InstanceMembers));
    }

    [Theory]
    [InlineData("now", false)]
    [InlineData("now", true)]
    [InlineData("UTC", false)]
    [InlineData("UTC", true)]
    [InlineData("parse", false)]
    [InlineData("parse", true)]
    public void StaticCallsFallThroughWithoutDateImplementation(string name, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var features = Detect(supplied ? "const value=new Date(0);" : "const value=1;");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, features);
        features.UsesDate = !supplied;
        var type = module.DefineType("DateCallProbe", TypeAttributes.Public);
        var method = type.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static, typeof(double), Type.EmptyTypes);
        var il = method.GetILGenerator();
        var context = new CompilationContext(il, new TypeMapper(module), [], [], null, null)
        {
            Runtime = runtime, RuntimeFeatures = features
        };
        var statement = Assert.IsType<Stmt.Expression>(Assert.Single(new Parser(new Lexer($"Date.{name}();").ScanTokens()).ParseOrThrow()));
        var call = Assert.IsType<Expr.Call>(statement.Expr);
        Assert.Equal(supplied, new DateStaticHandler().TryHandle(new ILEmitter(context), call));
        if (!supplied)
        {
            Assert.Equal(0, il.ILOffset);
            il.Emit(OpCodes.Ldc_R8, -1d);
        }
        il.Emit(OpCodes.Ret); type.CreateType();
        var result = Assert.IsType<double>(Call(SaveVerifyLoad(builder).GetType(type.Name)!, "Invoke"));
        if (!supplied) Assert.Equal(-1d, result);
        else if (name == "now") Assert.True(double.IsFinite(result) && result > 0);
        else Assert.True(double.IsNaN(result));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConstructorValuesFallThroughWithoutDateImplementation(bool globalThis, bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var features = Detect(supplied ? "const value=new Date(0);" : "const value=1;");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, features);
        features.UsesDate = !supplied;
        var type = module.DefineType("DateTypeProbe", TypeAttributes.Public);
        var method = type.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static, typeof(Type), Type.EmptyTypes);
        var il = method.GetILGenerator();
        var context = new CompilationContext(il, new TypeMapper(module), [], [], null, null)
        {
            Runtime = runtime, RuntimeFeatures = features
        };
        object? emitted = globalThis
            ? typeof(GlobalThisStaticEmitter).GetMethod("TryEmitBuiltInClassType", StaticMembers)!.Invoke(null, [il, context, "Date"])
            : typeof(ExpressionEmitterBase).GetMethod("TryEmitBuiltInClassType", InstanceMembers)!.Invoke(new ILEmitter(context), ["Date"]);
        Assert.Equal(supplied, Assert.IsType<bool>(emitted));
        if (!supplied)
        {
            Assert.Equal(0, il.ILOffset);
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Ret); type.CreateType();
        var loaded = SaveVerifyLoad(builder);
        var result = Call(loaded.GetType(type.Name)!, "Invoke");
        if (supplied) Assert.Same(loaded.GetType("$TSDate"), result);
        else Assert.Null(result);
    }

    private static void Fill(object owner, string? omitted = null)
    {
        foreach (var property in Handles(owner.GetType()).Where(property => property.Name != omitted)) property.SetValue(owner, Handle(property.PropertyType));
    }
    private static void FillRegistry(EmittedDateImplementation owner, string? omitted = null)
    {
        foreach (string name in InstanceMethodNames.Where(name => name != omitted)) owner.DeclareInstanceMethod(name, Method());
    }
    private static MethodBuilder Method() => (MethodBuilder)Handle(typeof(MethodBuilder));
    private static object Handle(Type kind)
    {
        var builder = NewAssembly().DefineDynamicModule("main").DefineType("Declaration");
        if (kind == typeof(TypeBuilder)) return builder;
        if (kind == typeof(FieldBuilder)) return builder.DefineField("Value", typeof(object), FieldAttributes.Public);
        if (kind == typeof(ConstructorBuilder)) return builder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        if (kind == typeof(MethodBuilder)) return builder.DefineMethod("Invoke", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        throw new InvalidOperationException(kind.Name);
    }
    private static void AssertFrozen(object owner)
    {
        Assert.Equal(true, owner.GetType().GetProperty("IsComplete")!.GetValue(owner));
        Expect<InvalidOperationException>(() => owner.GetType().GetMethod("CompleteEmission", InstanceMembers)!.Invoke(owner, null));
        foreach (var property in Handles(owner.GetType()))
        {
            Assert.False(property.SetMethod!.IsPublic);
            Expect<InvalidOperationException>(() => property.SetValue(owner, property.GetValue(owner)));
        }
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"date_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    private static void Invoke(RuntimeEmitter emitter, string name, params object?[] values) => typeof(RuntimeEmitter).GetMethod(name, InstanceMembers)!.Invoke(emitter, values);
    private static object? Call(Type type, string name, params object?[] values) => type.GetMethod(name, StaticMembers)!.Invoke(null, values);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        return Assembly.Load(bytes.ToArray());
    }
}
