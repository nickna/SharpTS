using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericAliasRuntimeHeritageTests
{
    public const string OriginalSource = "class Box<T>{constructor(public value:T){}} const Parent:any=Box; class Child extends Parent{constructor(value:number){super(value);}} console.log((new Child(7) as any).value);";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", OriginalSource, "7\n"];
        yield return ["generic-class-expression", "const Parent:any=class Box<T>{constructor(public value:T){}read(){return this.value;}};class Child extends Parent{constructor(v:number){super(v);}}const value:any=new Child(7);console.log(value.value,value.read(),Object.getPrototypeOf(Child)===Parent);", "7 7 true\n"];
        yield return ["number-and-string", "class Box<T>{constructor(public value:T){}}class Numbers extends Box<number>{}class Strings extends Box<string>{}const N:any=Numbers,S:any=Strings;class A extends N{constructor(v:number){super(v);}}class B extends S{constructor(v:string){super(v);}}console.log((new A(7) as any).value,(new B('s') as any).value,Object.getPrototypeOf(A)===N,Object.getPrototypeOf(B)===S);", "7 s true true\n"];
        yield return ["method-and-accessor", "class Box<T>{constructor(public value:T){}read():T{return this.value;}get current():T{return this.value;}}const Parent:any=Box;class Child extends Parent{constructor(v:any){super(v);}}const a:any=new Child(7),b:any=new Child('s');console.log(a.read(),b.read(),a.current,b.current);", "7 s 7 s\n"];
        yield return ["identity-new-target-and-hidden-adapter", "class Box<T>{target:any;constructor(public value:T){this.target=new.target;}}const Parent:any=Box;class Child extends Parent{constructor(v:number){super(v);}}const value:any=new Child(7);console.log(value.value,value.target===Child,value.constructor===Child,Object.getPrototypeOf(Child)===Parent,Object.getOwnPropertyNames(Parent).includes('$InitializeReceiver'));", "7 true true true false\n"];
        yield return ["defaults-rest-and-order", "const events:string[]=[];function note(v:string){events.push(v);return v;}class Box<T>{field=note('field');constructor(public value:T,extra=note('default'),...rest:any[]){events.push('body:'+rest.join(','));}}const Parent:any=Box;class Child extends Parent{constructor(v:number){events.push('before');super(v,undefined,3,4);events.push('after');}}const value:any=new Child(7);console.log(value.value,value.field,events.join(','));", "7 field before,field,default,body:3,4,after\n"];
        yield return ["per-evaluation-parent", "class A<T>{constructor(public value:T){}tag(){return 'a';}}class B<T>{constructor(public value:T){}tag(){return 'b';}}let Parent:any=A;function make(){return class extends Parent{constructor(v:any){super(v);}};}const First=make();Parent=B;const Second=make();const a:any=new First(7),b:any=new Second('s');console.log(a.value,a.tag(),b.value,b.tag(),Object.getPrototypeOf(First)===A,Object.getPrototypeOf(Second)===B);", "7 a s b true true\n"];
        yield return ["direct-constructor-value-and-reflect", "class Box<T>{constructor(public value:T){}}const C:any=Box;const a:any=new C(7),b:any=Reflect.construct(C,['s']);console.log(a.value,b.value,a.constructor===Box,b.constructor===Box);", "7 s true true\n"];
        yield return ["invalid-parent-has-no-effects", "let reads=0;const bad:any={get prototype(){reads++;return {};}};try{class C extends bad{}}catch(e){console.log(e instanceof TypeError,reads);}try{const arrow:any=()=>{};class D extends arrow{}}catch(e){console.log(e instanceof TypeError);}", "true 0\ntrue\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void GenericAliasParentsPreserveSourceBehavior(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativePreparedParentsVerifyBeforeExecution(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var saved = Build(source, native: true);
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForeignAndDelegatingParentsDoNotGainSourceNormalization(bool native)
    {
        var saved = Build(OriginalSource, native);
        var validate = saved.GetType("$ClassDefinition")!.GetMethod("ValidateParent")!;
        foreach (var foreign in new[] { typeof(object), typeof(List<>), typeof(List<string>) })
            Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [foreign]));
        var delegating = new LookupProbe(saved.GetType("Box")!);
        Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, [delegating]));
        Assert.Equal(0, delegating.ModuleReads);
        Assert.Equal(0, delegating.GenericReads);
        Assert.Equal(native ? 0 : 1, delegating.MethodReads);
    }

    private sealed class LookupProbe(Type wrapped) : TypeDelegator(wrapped)
    {
        internal int ModuleReads, GenericReads, MethodReads;
        public override Module Module { get { ModuleReads++; throw new InvalidOperationException("foreign module query"); } }
        public override bool ContainsGenericParameters { get { GenericReads++; throw new InvalidOperationException("foreign generic query"); } }
        protected override MethodInfo? GetMethodImpl(string name, BindingFlags bindingAttr, Binder? binder,
            CallingConventions callConvention, Type[]? types, ParameterModifier[]? modifiers)
        { MethodReads++; return null; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelegatingConstructorsKeepTheExistingAdmissionBoundary(bool native)
    {
        var saved = Build(OriginalSource, native);
        var construct = saved.GetType("$Runtime")!.GetMethod("ConstructWithNewTarget")!;
        var delegating = new ConstructorProbe(saved.GetType("Box")!);
        Assert.Throws<TargetInvocationException>(() => construct.Invoke(null, [delegating, Array.Empty<object>(), delegating]));
        Assert.Equal(0, delegating.ModuleReads);
        Assert.Equal(0, delegating.GenericReads);
        Assert.Equal(native ? 0 : 1, delegating.AttributeReads);
    }

    private sealed class ConstructorProbe(Type wrapped) : TypeDelegator(wrapped)
    {
        internal int ModuleReads, GenericReads, AttributeReads;
        public override Module Module { get { ModuleReads++; throw new InvalidOperationException("foreign module query"); } }
        public override bool ContainsGenericParameters { get { GenericReads++; throw new InvalidOperationException("foreign generic query"); } }
        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            AttributeReads++;
            throw new InvalidOperationException("original attribute query");
        }
    }

    private static Assembly Build(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        var compiler = new ILCompiler("generic_parent_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        var bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return Assembly.Load(bytes);
    }
}
