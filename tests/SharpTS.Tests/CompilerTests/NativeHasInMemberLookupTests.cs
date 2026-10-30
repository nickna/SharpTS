using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeHasInMemberLookupTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryClassesConstructorsFunctionsAndArraysKeepTheirDescriptorPrototypeWalk(bool native)
    {
        const string source = """
            let reads=0;
            const symbol=Symbol('field');
            class Parent { static inherited=7; inheritedMethod(){return 1;} }
            class Child extends Parent {
                static own=8;
                static [symbol]=9;
                static get staticRead(){reads++;return 1;}
                [symbol]=3;
                method(){return 2;}
                get label(){reads++;return 'tag';}
            }
            const child:any=new Child();
            console.log('method' in child,'inheritedMethod' in child,'label' in child,'missing' in child,symbol in child);
            console.log('name' in Child,'own' in Child,'inherited' in Child,'staticRead' in Child,'missing' in Child,'makeGenericType' in Child,symbol in Child);
            const typedChild:Child=new Child();
            console.log('method' in typedChild,'inheritedMethod' in typedChild,symbol in typedChild,'missing' in typedChild);
            const prototype=Object.create(Object.getPrototypeOf(child));
            Object.defineProperty(prototype,'extra',{get(){reads++;return 1;}});
            Object.setPrototypeOf(child,prototype);
            console.log('extra' in child,'missing' in child,reads);
            const fn:any=function callMe(){};
            console.log('name' in fn,'call' in fn,'missing' in fn,'invokeConstructed' in fn);
            function rest(...values:any[]){const array:any=values;console.log('length' in array,'slice' in array,'missing' in array,'getValue' in array);}
            rest(1,2);
            """;
        var saved=Compile(source,native,out var compiler);
        Assert.Equal("true true true false true\ntrue true true true false false true\ntrue true true false\ntrue false 0\ntrue true false false\ntrue true false false\n",
            TestHarness.RunCompiledWithTimeout(()=>saved,TimeSpan.FromSeconds(20)));
        if(native)Assert.False(compiler.NativeHasInMembers!.UsesLegacy);
        else {Assert.Null(compiler.NativeHasInMembers);Assert.Null(saved.GetType("$NativeHasInMemberLookup"));}
    }

    [Fact]
    public void EveryFiniteOwnerUsesTheLoadedPropertyThenMethodBinderIncludingAmbiguity()
    {
        var saved=Compile("class Source { method(){} get label(){return 'tag';} }",true,out var compiler);
        var catalog=compiler.NativeHasInMembers!;
        Assert.True(catalog.IsComplete);Assert.False(catalog.UsesLegacy);Assert.NotEmpty(catalog.Owners);
        foreach(var original in catalog.Owners)
        {
            var owner=original.Module.ModuleVersionId==saved.ManifestModule.ModuleVersionId?saved.GetType(original.FullName!)!:original;
            foreach(string name in owner.GetProperties(Flags).Select(property=>property.Name)
                .Concat(owner.GetMethods(Flags).Select(method=>method.Name)).Append("__missing_member__")
                .SelectMany(name=>new[]{name,name.ToUpperInvariant()}).Distinct(StringComparer.Ordinal))
            {
                Exception? failure=null;bool expected=false;
                try{expected=Legacy(owner,name);}catch(Exception error){failure=error;}
                if(failure is null)Assert.Equal(expected,Helper(saved).Invoke(null,[owner,name]));
                else Assert.IsType(failure.GetType(),Assert.Throws<TargetInvocationException>(()=>Helper(saved).Invoke(null,[owner,name])).InnerException);
            }
        }
        var missingName=Assert.Throws<TargetInvocationException>(()=>Helper(saved).Invoke(null,[typeof(object),null]));
        Assert.Equal("name",Assert.IsType<ArgumentNullException>(missingName.InnerException).ParamName);
        var arrayAmbiguity=Assert.Throws<TargetInvocationException>(()=>Helper(saved).Invoke(null,[typeof(object[]),"GetValue"]));
        Assert.IsType<AmbiguousMatchException>(arrayAmbiguity.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovedGenericMetadataRetainsTheWholeLegacyOperation(bool generic)
    {
        var saved=Compile(generic?"class Base<T>{} class Child<T> extends Base<T>{}":"console.log(42);",true,out var compiler);
        Assert.Equal(generic,compiler.NativeHasInMembers!.UsesLegacy);
        if(generic)Assert.Equal(true,Helper(saved).Invoke(null,[typeof(Foreign),"Value"]));
        else
        {
            var failure=Assert.Throws<TargetInvocationException>(()=>Helper(saved).Invoke(null,[typeof(Foreign),"Value"]));
            Assert.Contains(NativeHasInMemberLookup.ForeignOwnerMessage,failure.InnerException!.Message);
            var unknownSubclass=Assert.Throws<TargetInvocationException>(()=>Helper(saved).Invoke(null,[typeof(ForeignList),"Count"]));
            Assert.Contains(NativeHasInMemberLookup.ForeignOwnerMessage,unknownSubclass.InnerException!.Message);
        }
    }

    [Fact]
    public void OptionalHookRejectsWrongModuleOrSignatureBeforeEmission()
    {
        var first=new PersistedAssemblyBuilder(new AssemblyName("in_first"),typeof(object).Assembly).DefineDynamicModule("main");
        var helper=new NativeHasInMemberLookup(first,TypeProvider.Runtime);
        Assert.Throws<InvalidOperationException>(()=>helper.Owners);
        var second=new PersistedAssemblyBuilder(new AssemblyName("in_second"),typeof(object).Assembly).DefineDynamicModule("main");
        Assert.Throws<InvalidOperationException>(()=>new RuntimeEmitter(TypeProvider.Runtime,false,true,false,nativeHasInMemberLookup:helper.HasMember).EmitAll(second));
        var bad=second.DefineType("Wrong").DefineMethod("Lookup",MethodAttributes.Public|MethodAttributes.Static,typeof(object),[typeof(Type),typeof(string)]);
        Assert.Throws<InvalidOperationException>(()=>new RuntimeEmitter(TypeProvider.Runtime,false,true,false,nativeHasInMemberLookup:bad).EmitAll(second));
    }

    [Fact]
    public void SyntheticNativeProbePreservesPresenceAmbiguityAndInitializerTiming()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("Guest"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main"); var types = TypeProvider.Runtime;
        var properties = new NativePropertyLookupCatalog(module, types);
        var methods = new NativeMethodLookupCatalog(module, types);
        var sources = new NativeSourceOwnerCatalog(module, types);
        var members = new NativeHasInMemberLookup(module, types);
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer("console.log(42);").ScanTokens()).ParseOrThrow());
        var runtime = new RuntimeEmitter(types, false, true, false, nativeHasInMemberLookup: members.HasMember)
            .EmitAll(module, features, FunctionEmissionPolicy.Full, sources,
                nativePropertyGetter: properties.FindGetter, nativeMethodLookup: methods.Handles);
        var state = module.DefineType("State", TypeAttributes.Public);
        var count = state.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static); state.CreateType();
        var owner = module.DefineType("Carrier", TypeAttributes.Public, typeof(object));
        owner.DefineDefaultConstructor(MethodAttributes.Public);
        var initialize = owner.DefineTypeInitializer().GetILGenerator();
        initialize.Emit(OpCodes.Ldc_I4_1); initialize.Emit(OpCodes.Stsfld, count); initialize.Emit(OpCodes.Ret);
        var property = owner.DefineProperty("Mixed", PropertyAttributes.None, typeof(int), Type.EmptyTypes);
        var getter = owner.DefineMethod("get_Mixed", MethodAttributes.Private | MethodAttributes.SpecialName, typeof(int), Type.EmptyTypes);
        getter.GetILGenerator().Emit(OpCodes.Ldc_I4_7); getter.GetILGenerator().Emit(OpCodes.Ret); property.SetGetMethod(getter);
        var setter = owner.DefineMethod("set_Mixed", MethodAttributes.Public | MethodAttributes.SpecialName, typeof(void), [typeof(int)]);
        setter.GetILGenerator().Emit(OpCodes.Ret); property.SetSetMethod(setter);
        foreach (var name in new[] { "Read", "rEAD" })
        {
            var method = owner.DefineMethod(name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
        }
        owner.CreateType(); properties.RegisterOutputOwner(owner);
        var foreign = module.DefineType("Unregistered", TypeAttributes.Public); foreign.DefineDefaultConstructor(MethodAttributes.Public); foreign.CreateType();
        var probe = module.DefineType("NativeInProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (var name in new[] { "Mixed", "Missing", "Ambiguous", "ArrayAmbiguous", "Foreign" })
        {
            var method = probe.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(bool), Type.EmptyTypes);
            var il = method.GetILGenerator();
            bool catches = name is "Ambiguous" or "ArrayAmbiguous" or "Foreign";
            LocalBuilder? result = catches ? il.DeclareLocal(typeof(bool)) : null;
            var done = il.DefineLabel();
            if (catches) il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldtoken, name == "ArrayAmbiguous" ? typeof(object[]) : name == "Foreign" ? foreign : owner);
            il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
            il.Emit(OpCodes.Ldstr, name == "Ambiguous" ? "READ" : name == "ArrayAmbiguous" ? "GetValue" : name);
            il.Emit(OpCodes.Call, members.HasMember);
            if (catches)
            {
                il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, result!); il.Emit(OpCodes.Leave, done);
                il.BeginCatchBlock(typeof(Exception));
                if (name == "Foreign") { il.Emit(OpCodes.Call, runtime.Errors.WrapException); il.Emit(OpCodes.Isinst, runtime.Errors.TypeErrorType); }
                else il.Emit(OpCodes.Isinst, typeof(AmbiguousMatchException));
                il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Cgt_Un); il.Emit(OpCodes.Stloc, result!); il.Emit(OpCodes.Leave, done);
                il.EndExceptionBlock(); il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result!);
            }
            il.Emit(OpCodes.Ret);
        }
        var reads = probe.DefineMethod("InitializerCount", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        reads.GetILGenerator().Emit(OpCodes.Ldsfld, count); reads.GetILGenerator().Emit(OpCodes.Ret); probe.CreateType();
        var parameters = new ClassGenericParameterRegistry(); parameters.CompleteEmission([]); sources.CompleteEmission(parameters, []);
        properties.CompleteEmission(sources, runtime); methods.CompleteEmission(properties, sources, runtime.Errors, runtime.FunctionValues.Type);
        members.CompleteEmission(methods, sources, runtime);
        using var image = new MemoryStream(); assembly.Save(image);
        byte[] bytes = AssemblyReferenceRewriteCompatibility.Rewrite(image.ToArray(), PEPacker.EmbeddedReferenceAssemblyIndex.Default,
            PEPacker.ReferencePolicy.DroppingReferences("SharpTS"));
        using var verify = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(verify));
        var saved = Assembly.Load(bytes); var savedProbe = saved.GetType("NativeInProbe")!;
        foreach (var name in new[] { "Mixed", "Ambiguous", "ArrayAmbiguous", "Foreign" }) Assert.Equal(true, savedProbe.GetMethod(name)!.Invoke(null, null));
        Assert.Equal(false, savedProbe.GetMethod("Missing")!.Invoke(null, null));
        Assert.Equal(0, savedProbe.GetMethod("InitializerCount")!.Invoke(null, null));
        if (Environment.GetEnvironmentVariable("SHARPTS_HAS_IN_NATIVE_FIXTURE_PATH") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllBytes(path, bytes);
        }
    }

    public sealed class Foreign { public int Value=>7; }
    public sealed class ForeignList:List<object>{}
    private static bool Legacy(Type owner,string name)=>owner.GetProperty(name,Flags) is not null||owner.GetMethod(name,Flags) is not null;
    private static MethodInfo Helper(Assembly saved)=>saved.GetType("$NativeHasInMemberLookup")!.GetMethod("HasMember",BindingFlags.Static|BindingFlags.NonPublic)!;
    private static Assembly Compile(string source,bool native,out ILCompiler compiler)
    {
        var statements=new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();var checker=new TypeChecker();var types=checker.Check(statements);
        compiler=new ILCompiler("has_in_"+Guid.NewGuid().ToString("N"),false,true,null,null,null,OutputTarget.Dll);
        if(native)compiler.EnableNativeAotProjectOutput();compiler.Compile(statements,types);byte[] bytes=compiler.SaveToBytes();
        using var stream=new MemoryStream(bytes);using var verifier=new ILVerifier(extraProbeDirectories:[AppContext.BaseDirectory]);Assert.Empty(verifier.Verify(stream));return Assembly.Load(bytes);
    }
}