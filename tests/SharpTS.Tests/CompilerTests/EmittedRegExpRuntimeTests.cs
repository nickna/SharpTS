using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedRegExpRuntimeTests
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => new[] { typeof(EmittedRegExpRuntime), typeof(EmittedRegExpImplementation) }
        .SelectMany(type => Handles(type).Select(property => new object[] { type, property.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationIsRepairableAndCompletionFreezesBothOwners(Type type, string missing)
    {
        var root = new EmittedRegExpRuntime();
        root.BeginImplementationEmission();
        var regExp = root.RequireImplementation();
        object owner = type == typeof(EmittedRegExpRuntime) ? root : regExp;
        var property = type.GetProperty(missing)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        Fill(root, type == typeof(EmittedRegExpRuntime) ? missing : null);
        Fill(regExp, type == typeof(EmittedRegExpImplementation) ? missing : null);
        MarkProtocols(regExp);
        root.MarkPrototypeBodyEmitted();
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete);
        Assert.False(regExp.IsComplete);
        var handle = Handle(property.PropertyType);
        property.SetValue(owner, handle);
        Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property.PropertyType)));
        Assert.Same(handle, property.GetValue(owner));
        root.CompleteEmission();
        AssertFrozen(root);
        AssertFrozen(regExp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrototypeBodyIsRequiredEvenWithoutRegExpOperations(bool enabled)
    {
        var root = new EmittedRegExpRuntime(); Fill(root);
        Assert.Null(root.Implementation);
        Assert.Throws<InvalidOperationException>(root.RequireImplementation);
        if (enabled)
        {
            root.BeginImplementationEmission(); Fill(root.RequireImplementation()); MarkProtocols(root.RequireImplementation());
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
    public void RequiredOwnerIsPerCompilationAndRegExpSelectionIsExplicit()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.RegExps, second.RegExps);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.RegExps))!.SetMethod);
        Assert.Equal(2, Handles(typeof(EmittedRegExpRuntime)).Length);
        Assert.Equal(66, Handles(typeof(EmittedRegExpImplementation)).Length);
        Assert.Null(first.RegExps.Implementation);
        first.RegExps.BeginImplementationEmission();
        Assert.NotNull(first.RegExps.Implementation);
        Assert.Null(second.RegExps.Implementation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletionRequiresBothLateProtocolBodies(bool omitSplit)
    {
        var root = new EmittedRegExpRuntime(); root.BeginImplementationEmission();
        var child = root.RequireImplementation(); Fill(root); Fill(child); root.MarkPrototypeBodyEmitted();
        if (omitSplit) child.MarkMatchAllProtocolBodyEmitted(); else child.MarkSplitProtocolBodyEmitted();
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete); Assert.False(child.IsComplete);
        if (omitSplit) child.MarkSplitProtocolBodyEmitted(); else child.MarkMatchAllProtocolBodyEmitted();
        Assert.Throws<InvalidOperationException>(child.MarkSplitProtocolBodyEmitted);
        Assert.Throws<InvalidOperationException>(child.MarkMatchAllProtocolBodyEmitted);
        root.CompleteEmission(); AssertFrozen(root); AssertFrozen(child);
        Assert.Throws<InvalidOperationException>(child.MarkSplitProtocolBodyEmitted);
        Assert.Throws<InvalidOperationException>(child.MarkMatchAllProtocolBodyEmitted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PrototypePopulationFollowsSuppliedImplementation(bool supplied, bool globalFlag)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var features = Detect("const value = /a/g;");
        var runtime = emitter.EmitAll(module, features); features.UsesRegExp = globalFlag;
        var root = new EmittedRegExpRuntime();
        if (supplied)
        {
            root.BeginImplementationEmission(); var child = root.RequireImplementation();
            foreach (var property in Handles(typeof(EmittedRegExpImplementation)))
                property.SetValue(child, property.GetValue(runtime.RegExps.RequireImplementation()));
            MarkProtocols(child);
        }
        var type = runtime.RuntimeClass.Type.DefineNestedType("RegExpPrototypeProbe", TypeAttributes.NestedPublic);
        root.Prototype = type.DefineField("Prototype", typeof(Dictionary<string, object>), FieldAttributes.Public | FieldAttributes.Static);
        MethodBuilder? forward = null;
        if (supplied)
            root.PopulatePrototype = forward = type.DefineMethod("Populate", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        Invoke(emitter, "DefineRegExpPrototypePopulateShell", type, root);
        if (supplied)
        {
            Assert.Same(forward, root.PopulatePrototype);
            var method = typeof(RuntimeEmitter).GetMethod("EmitRegExpPrototypePopulate", InstanceMembers)!;
            var inputs = Activator.CreateInstance(method.GetParameters()[2].ParameterType,
                runtime.DescriptorStorage, runtime.ObjectPrototypes.Prototype, runtime.Symbols, runtime.FunctionConstruction.CachedConstructor, runtime.Sentinels.UndefinedInstance)!;
            method.Invoke(emitter, [type, root, inputs]);
        }
        root.CompleteEmission(); type.CreateType();
        var loaded = SaveVerifyLoad(builder); var probe = loaded.GetType(type.FullName!)!;
        var dictionary = new Dictionary<string, object>(); probe.GetField("Prototype")!.SetValue(null, dictionary);
        var populate = probe.GetMethod(root.PopulatePrototype.Name)!; populate.Invoke(null, null);
        Assert.Equal(supplied ? 4 : 0, dictionary.Count);
        if (supplied)
        {
            foreach (string key in new[] { "constructor", "exec", "test", "toString" }) Assert.True(dictionary.ContainsKey(key));
            dictionary["mutation"] = 7d; populate.Invoke(null, null); Assert.Equal(7d, dictionary["mutation"]);
        }
        else Assert.Equal(new byte[] { 0x2a }, populate.GetMethodBody()!.GetILAsByteArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedPrototypeRequiresItsEarlyForwardDeclaration(bool globalFlag)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var features = Detect("const value = 1;");
        emitter.EmitAll(module, features); features.UsesRegExp = globalFlag;
        var root = new EmittedRegExpRuntime(); root.BeginImplementationEmission();
        var type = module.DefineType("ForwardProbe", TypeAttributes.Public);
        Expect<InvalidOperationException>(() => Invoke(emitter, "DefineRegExpPrototypePopulateShell", type, root));
        Assert.Throws<InvalidOperationException>(() => root.PopulatePrototype);
        var forward = type.DefineMethod("Populate", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        root.PopulatePrototype = forward;
        Invoke(emitter, "DefineRegExpPrototypePopulateShell", type, root);
        Assert.Same(forward, root.PopulatePrototype); Assert.False(root.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterIsolatesMetadataAndPreservesRegExpCacheAndInstanceState(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<object>(); var types = new List<Type>(); var caches = new List<object>();
        foreach (bool enabled in new[] { false, true, false, true })
        {
            var builder = NewAssembly();
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(enabled ? "const value = /a/g;" : "const value = 1;"));
            Assert.DoesNotContain(runtime.RegExps, owners); owners.Add(runtime.RegExps); AssertFrozen(runtime.RegExps);
            Assert.Equal(enabled, runtime.RegExps.Implementation is not null);
            if (runtime.RegExps.Implementation is { } child)
            {
                AssertFrozen(child);
                foreach (var property in Handles(typeof(EmittedRegExpImplementation)))
                    Assert.Same(builder, Assert.IsAssignableFrom<MemberInfo>(property.GetValue(child)).Module.Assembly);
            }
            var loaded = SaveVerifyLoad(builder); var references = loaded.GetReferencedAssemblies().Select(r => r.Name).ToArray();
            Assert.DoesNotContain("SharpTS", references); Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
            Assert.DoesNotContain(references, name => name!.StartsWith("regexp_runtime_", StringComparison.Ordinal));
            var regexType = loaded.GetType("$RegExp"); Assert.Equal(enabled, regexType is not null);
            var runtimeType = loaded.GetType("$Runtime")!;
            var populate = runtimeType.GetMethod(runtime.RegExps.PopulatePrototype.Name)!;
            var stringPopulate = runtimeType.GetMethod("_StringPrototypePopulate")!;
            var functionPopulate = runtimeType.GetMethod("_FunctionPrototypePopulate")!;
            Assert.True(enabled ? populate.MetadataToken < stringPopulate.MetadataToken : populate.MetadataToken > functionPopulate.MetadataToken);
            var prototype = Assert.IsAssignableFrom<IDictionary>(runtimeType.GetField(runtime.RegExps.Prototype.Name)!.GetValue(null));
            if (!enabled)
            {
                Assert.Empty(prototype); Assert.Equal(new byte[] { 0x2a }, populate.GetMethodBody()!.GetILAsByteArray());
                continue;
            }
            Assert.DoesNotContain(regexType!, types); types.Add(regexType!); Assert.Equal(2, regexType!.GetConstructors().Length);
            var ctor = regexType.GetConstructor([typeof(string), typeof(string)])!;
            var value = ctor.Invoke(["a", "g"]); var peer = ctor.Invoke(["a", "g"]);
            var regexField = regexType.GetField("_regex", InstanceMembers)!;
            Assert.Same(regexField.GetValue(value), regexField.GetValue(peer));
            var cache = regexType.GetField("_compileCache", StaticMembers)!.GetValue(null)!;
            Assert.DoesNotContain(cache, caches); caches.Add(cache);
            object? Instance(object target, string name, params object?[] args) => regexType.GetMethod(name, InstanceMembers)!.Invoke(target, args);
            Assert.Equal("a", Instance(value, "get_Source")); Assert.Equal("g", Instance(value, "get_Flags"));
            Assert.Equal(true, Instance(value, "Test", "aba")); Assert.Equal(1, Instance(value, "get_LastIndex"));
            Assert.Equal(0, Instance(peer, "get_LastIndex"));
            Assert.Equal(true, Instance(value, "Test", "aba")); Assert.Equal(3, Instance(value, "get_LastIndex"));
            Assert.Equal(false, Instance(value, "Test", "aba")); Assert.Equal(0, Instance(value, "get_LastIndex"));
            var boxed = regexType.GetField("_lastIndexBoxed", InstanceMembers)!; boxed.SetValue(value, "2");
            Instance(value, "set_LastIndex", 1); Assert.Null(boxed.GetValue(value)); Assert.Equal(1, Instance(value, "get_LastIndex"));
            var clone = Call(runtimeType, "StructuredClone", value, null)!;
            Assert.NotSame(value, clone); Assert.Equal(regexType, clone.GetType());
            Assert.Equal("a", Instance(clone, "get_Source")); Assert.Equal("g", Instance(clone, "get_Flags"));
            Assert.Equal(0, Instance(clone, "get_LastIndex"));
            Assert.Equal(regexType, prototype["constructor"]); Assert.Equal(4, prototype.Count);
            prototype["mutation"] = 7d; populate.Invoke(null, null); Assert.Equal(7d, prototype["mutation"]);
        }
    }

    [Fact]
    public void FamilyBoundariesUseOwnersWithoutFlatAliasesOrPersistentConstructionHandles()
    {
        foreach (string name in new[] { "EmitTSRegExpClass", "EmitTSRegExpCompileCacheCctor", "EmitTSRegExpGetCachedRegex", "EmitTSRegExpExpandShorthand", "EmitTSRegExpRewriteShorthands", "EmitTSRegExpNormalizeFlags", "EmitAppendFlagIfContains", "EmitTSRegExpHasNamedGroups", "EmitTSRegExpSkipCharClass", "EmitTSRegExpValidateModifierFlags", "EmitTSRegExpValidateModifiers", "EmitTSRegExpValidateFlags", "EmitTSRegExpFindGroupClose", "EmitTSRegExpValidateUnicodePattern", "EmitTSRegExpCtorPattern", "EmitTSRegExpCtorPatternFlags", "EmitTSRegExpSourceGetter", "EmitTSRegExpFlagsGetter", "EmitTSRegExpGlobalGetter", "EmitTSRegExpIgnoreCaseGetter", "EmitTSRegExpMultilineGetter", "EmitTSRegExpLastIndexGetter", "EmitTSRegExpLastIndexSetter", "EmitTSRegExpTest", "EmitTSRegExpSetLastIndexStrict", "EmitTSRegExpResolveLastIndex", "EmitTSRegExpExec", "EmitTSRegExpToStringMethod", "EmitTSRegExpEscape", "EmitTSRegExpMatchAll", "EmitTSRegExpEscapeJsReplacement", "EmitTSRegExpReplace", "EmitTSRegExpReplaceWithFn", "EmitTSRegExpSearch", "EmitTSRegExpSplit", "EmitTSRegExpBuildNamedGroups", "EmitTSRegExpSymMatchHelper", "EmitSymMatchSlowPath", "EmitTSRegExpSymMatchAllHelper", "EmitTSRegExpAdvanceStringIndexSpec", "EmitTSRegExpExpandReplacementSpec", "EmitTSRegExpSymReplaceHelper", "EmitBranchIfIntrinsicRegExpExec", "EmitSymReplaceSpecPath", "EmitTSRegExpSymSearchHelper", "EmitSymSearchSlowPath", "EmitStrictWritableCheck", "EmitRegExpExecSlow", "EmitIsNumericZero", "EmitTSRegExpSymSplitHelper", "EmitTSRegExpProtoAccessors", "EmitProtoFlagsAccessor", "EmitProtoAccessor", "EmitProtoBoolAccessor", "EmitProtoAccessorPrologue", "EmitTSRegExpProtoMethods", "EmitProtoExecMethod", "EmitProtoTestMethod", "EmitProtoToStringMethod", "EmitArgToJsString", "EmitRequireObjectArg", "EmitStringSymbolDispatchPreamble", "EmitRegExpMethods", "EmitStableRegExpReplace", "EmitRegExpCoerceArg", "EmitStringReplaceAllRegExp", "EmitCreateRegExpWithFlags", "EmitRegExpFromArgs", "EmitRegExpTest", "EmitRegExpExec", "EmitRegExpGetSource", "EmitRegExpGetFlags", "EmitRegExpGetGlobal", "EmitRegExpGetIgnoreCase", "EmitRegExpGetMultiline", "EmitRegExpGetFlagBool", "EmitRegExpGetLastIndex", "EmitRegExpSetLastIndex", "EmitStringMatchRegExp", "EmitStringMatchAllRegExp", "EmitStringReplaceRegExp", "EmitStringReplaceWithFunction", "EmitStringSearchRegExp", "EmitStringSplitRegExp", "EmitStringSplitProto", "DefineRegExpPrototypePopulateShell", "EmitRegExpPrototypePopulate", "EmitRegExpSymbolSplitProtocol", "EmitRejectPrimitive", "EmitToLengthInt", "EmitSetLastIndex", "EmitReturnIfLimitReached", "EmitReturnArray", "EmitRegExpSymbolMatchAllProtocol" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, InstanceMembers | BindingFlags.Static)!;
            Assert.NotNull(method);
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        }
        foreach (string name in new[] { "RegExpPrototypeField", "RegExpPrototypePopulateMethod", "BuildNamedGroups", "CreateRegExpWithFlags", "RegExpCoerceArg", "RegExpExec", "RegExpFromArgs", "RegExpGetDotAll", "RegExpGetFlags", "RegExpGetGlobal", "RegExpGetHasIndices", "RegExpGetIgnoreCase", "RegExpGetMultiline", "RegExpGetSource", "RegExpGetSticky", "RegExpGetUnicode", "RegExpGetUnicodeSets", "RegExpSymbolMatchAllProtocol", "RegExpSymbolSplitProtocol", "StableRegExpReplace", "StringMatchAllRegExp", "StringMatchAllRegExpPrepared", "StringMatchRegExp", "StringReplaceAllRegExp", "StringReplaceRegExp", "StringReplaceWithFunction", "StringSearchRegExp", "StringSplitProto", "StringSplitRegExp", "TSRegExpAdvanceStringIndexSpec", "TSRegExpCtorPattern", "TSRegExpCtorPatternFlags", "TSRegExpEscapeMethod", "TSRegExpExecMethod", "TSRegExpFlagsGetter", "TSRegExpGlobalGetter", "TSRegExpIgnoreCaseGetter", "TSRegExpLastIndexGetter", "TSRegExpLastIndexSetter", "TSRegExpMultilineGetter", "TSRegExpNormalizeFlags", "TSRegExpProtoExec", "TSRegExpProtoGetDotAll", "TSRegExpProtoGetFlags", "TSRegExpProtoGetGlobal", "TSRegExpProtoGetHasIndices", "TSRegExpProtoGetIgnoreCase", "TSRegExpProtoGetMultiline", "TSRegExpProtoGetSource", "TSRegExpProtoGetSticky", "TSRegExpProtoGetUnicode", "TSRegExpProtoGetUnicodeSets", "TSRegExpProtoTest", "TSRegExpProtoToString", "TSRegExpReplaceMethod", "TSRegExpSourceGetter", "TSRegExpSymMatchAllHelper", "TSRegExpSymMatchHelper", "TSRegExpSymReplaceHelper", "TSRegExpSymSearchHelper", "TSRegExpSymSplitHelper", "TSRegExpTestMethod", "TSRegExpType", "RegExpTest", "RegExpGetLastIndex", "RegExpSetLastIndex" }) Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        Assert.DoesNotContain(typeof(RuntimeEmitter).GetFields(InstanceMembers), f => f.Name.StartsWith("_tsRegExp", StringComparison.Ordinal));
    }

    private static void MarkProtocols(EmittedRegExpImplementation owner)
    {
        owner.MarkSplitProtocolBodyEmitted(); owner.MarkMatchAllProtocolBodyEmitted();
    }
    private static void Fill(object owner, string? omitted = null)
    {
        foreach (var property in Handles(owner.GetType()).Where(property => property.Name != omitted)) property.SetValue(owner, Handle(property.PropertyType));
    }
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
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"regexp_runtime_{Guid.NewGuid():N}"), typeof(object).Assembly);
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
