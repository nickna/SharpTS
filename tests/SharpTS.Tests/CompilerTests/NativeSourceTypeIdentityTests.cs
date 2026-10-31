using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Compilation.Symbols;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeSourceTypeIdentityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-reproduction", "class $SharedArrayBuffer { GetBuffer(){console.log('fake-getter');throw new Error('fake');} } try{new Uint8Array(new $SharedArrayBuffer() as any);}catch(e){console.log((e as any).message);}", ""];
        yield return ["runtime-colliders", "class $Program { value=7; } class $Array { value=8; } class $TSFunction { value=9; } console.log(new $Program().value,new $Array().value,new $TSFunction().value,$Program.name,$Array.name,$TSFunction.name); console.log($Program.prototype.constructor===$Program);", "7 8 9 $Program $Array $TSFunction\ntrue\n"];
        yield return ["generic-private-inheritance", "class $Array<T>{#value:T;constructor(value:T){this.#value=value;}read(){return this.#value;}} class $TSFunction extends $Array<number>{} const value=new $TSFunction(7);console.log(value.read(),value instanceof $Array,value instanceof $TSFunction,$TSFunction.name);", "7 true true $TSFunction\n"];
        yield return ["bound-reflect", "class $Program {value:number;constructor(value=3,...tail:number[]){this.value=value+tail.length;}} const Bound:any=$Program.bind(null,5); const a=new Bound(8,9);const b:any=Reflect.construct($Program,[7]);console.log(a.value,b.value,a instanceof $Program,b.constructor===$Program);", "7 7 true true\n"];
        yield return ["nested-expression-private-owner", "class $Program{#value=7;make(){const owner=this;return class $Array{read(){return owner.#value;}};}} const Type=new $Program().make();console.log(new Type().read(),Type.name);", "7 $Array\n"];
        yield return ["generated-spelling", "class $BlockClass_0_C{value=1;} {class C{value=2;}console.log(new C().value);}console.log(new $BlockClass_0_C().value);", "2\n1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativeIdentitiesKeepGuestSemanticsWithoutDuplicateTypeDefinitions(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var artifacts = Compile(source);
        AssertUniqueTypes(artifacts.Assembly);
        Assert.Equal(expected, Execute(artifacts.Assembly));
    }

    [Theory]
    [InlineData("$SharedArrayBuffer")]
    [InlineData("ArrayBuffer")]
    [InlineData("OrdinaryArrayBufferSource")]
    public void OwnedObjectsWithBufferLikeNamesUseOrdinaryArrayLikeReads(string name)
    {
        foreach (string fields in new[] { "", "length=0;", "length=2;" })
        {
            string source = "let calls=0;class " + name + "{" + fields + "GetBuffer(){calls++;throw new Error('wrong bridge');}}" +
                "const Source:any=" + name + ";const value:any=new Source();value[0]=7;value[1]=9;const array=new Uint8Array(value);" +
                "let output='';for(let i=0;i<array.length;i++)output+=array[i]+',';console.log(array.length+':'+output+':'+calls);";
            Assert.Equal(fields == "length=2;" ? "2:7,9,:0\n" : "0::0\n", Execute(Compile(source).Assembly));
        }
    }

    [Fact]
    public void ForeignClrBufferNameLookalikeKeepsTheExistingForeignPath()
    {
        var bytes = Compile("new Uint8Array([1]);").Assembly;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        var assembly = Assembly.Load(bytes);
        var fromObject = Assert.Single(assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static)),
            method => method.Name == "CreateUint8ArrayFromObject");
        var foreign = new ForeignArrayBufferLookalike();
        var error = Assert.Throws<TargetInvocationException>(() => fromObject.Invoke(null, [foreign])).InnerException;
        Assert.Equal("TypedArray constructor requires emitted ArrayBuffer/SharedArrayBuffer.", Assert.IsType<InvalidOperationException>(error).Message);
        Assert.Equal(0, foreign.Reads);
    }

    private sealed class ForeignArrayBufferLookalike
    {
        public int Reads;
        public int Length { get { Reads++; return 2; } }
    }

    [Fact]
    public void NamespaceImitationsAndRepeatedModuleNamesKeepExactBindings()
    {
        const string directory = "/native-source-identity";
        var files = new Dictionary<string, string>
        {
            [directory + "/left.ts"] = "@Namespace(\"<SharpTS.Source>.Declared\") export class $SharedArrayBuffer {value=1;}",
            [directory + "/right.ts"] = "@Namespace(\"<SharpTS.Source>.Expression\") export class $SharedArrayBuffer {value=2;}",
            [directory + "/main.ts"] = "import {$SharedArrayBuffer as Left} from './left';import {$SharedArrayBuffer as Right} from './right';console.log(new Left().value,new Right().value,Left.name,Right.name);"
        };
        var resolver = new ModuleResolver(directory + "/main.ts", files);
        var entry = resolver.LoadModule(directory + "/main.ts", DecoratorMode.Legacy);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.CheckModules(modules, resolver);
        var compiler = Compiler(); compiler.CompileModules(modules, resolver, types);
        byte[] bytes = compiler.SaveToBytes();
        AssertUniqueTypes(bytes);
        Assert.Equal("1 2 $SharedArrayBuffer $SharedArrayBuffer\n", Execute(bytes));
    }

    [Theory]
    [InlineData("a-b", "a_b")]
    [InlineData("left/same", "right/same")]
    public void CollidingModuleSpellingsKeepDistinctSourceAliases(string left, string right)
    {
        const string directory = "/native-source-module-names";
        var files = new Dictionary<string, string>
        {
            [directory + "/" + left + ".ts"] = "export class Same {value=1;}",
            [directory + "/" + right + ".ts"] = "export class Same {value=2;}",
            [directory + "/main.ts"] = "import {Same as Left} from './" + left + "';import {Same as Right} from './" + right + "';console.log(new Left().value,new Right().value,Left.name,Right.name);"
        };
        var resolver = new ModuleResolver(directory + "/main.ts", files);
        var entry = resolver.LoadModule(directory + "/main.ts", DecoratorMode.Legacy);
        var modules = resolver.GetModulesInOrder(entry);
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.CheckModules(modules, resolver);
        var compiler = Compiler(); compiler.CompileModules(modules, resolver, types);
        byte[] bytes = compiler.SaveToBytes(); AssertUniqueTypes(bytes);
        Assert.Equal("1 2 Same Same\n", Execute(bytes));
    }

    [Fact]
    public void OnlyActualClassBodiesCanRegisterAliasesAndStorageNamesRemainRaw()
    {
        var policy = new NativeSourceTypeNames();
        var body = Assert.IsType<Stmt.Class>(Assert.Single(Parse("class C {}")));
        var ambient = Assert.IsType<Stmt.Class>(Assert.Single(Parse("declare class ExternalAlias {}")));
        Assert.Throws<InvalidOperationException>(() => policy.Declare(ambient, "ExternalAlias", true));
        string emitted = policy.Declare(body, "C", true);
        Assert.StartsWith(NativeSourceTypeNames.DeclarationDomain, emitted);
        Assert.Equal(emitted, policy.ResolveClassCandidate("C"));
        foreach (string unknown in new[] { "ExternalAlias", "$SharedArrayBuffer", "<SharpTS.Source>.Expression.C" })
            Assert.Equal(unknown, policy.ResolveClassCandidate(unknown));
        var module = new PersistedAssemblyBuilder(new AssemblyName("source_name_role"), typeof(object).Assembly).DefineDynamicModule("main");
        var context = new CompilationContext(module.DefineGlobalMethod("Body", MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator(),
            new TypeMapper(module, TypeProvider.Runtime), [], [], [], [], TypeProvider.Runtime) { NativeSourceTypeNames = policy };
        Assert.Equal(emitted, context.GetQualifiedClassName("C"));
        Assert.Equal(emitted, context.ResolveClassName("C"));
        Assert.Equal("C", context.GetRawQualifiedName("C"));
        Assert.Equal("ExternalAlias", context.GetQualifiedClassName("ExternalAlias"));
    }

    [Fact]
    public void RenamedOwnerKeepsOriginalDocumentAndClassMethodSequencePoints()
    {
        const string source = "class $Program {\n  read() {\n    const value=7;\n    return value;\n  }\n}\nconsole.log(new $Program().read());";
        var artifacts = Compile(source, true);
        Assert.NotNull(artifacts.Pdb);
        using var pe = new PEReader(new MemoryStream(artifacts.Assembly));
        var metadata = pe.GetMetadataReader();
        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(artifacts.Pdb!));
        var pdb = provider.GetMetadataReader();
        var owner = Assert.Single(metadata.TypeDefinitions.Select(metadata.GetTypeDefinition), type =>
            metadata.GetString(type.Name) == "$Program" && metadata.GetString(type.Namespace).StartsWith(NativeSourceTypeNames.DeclarationDomain, StringComparison.Ordinal));
        var points = owner.GetMethods().SelectMany(method => pdb.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(method))).GetSequencePoints())
            .Where(point => !point.IsHidden).ToArray();
        Assert.Contains(points, point => point.StartLine == 3);
        Assert.Contains(points, point => point.StartLine == 4);
        Assert.All(points, point => Assert.Equal("/native-source-identity/main.ts", pdb.GetString(pdb.GetDocument(point.Document).Name)));
        Assert.Equal("7\n", Execute(artifacts.Assembly));
    }

    private static List<Stmt> Parse(string source) => new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
    private static ILCompiler Compiler()
    {
        var compiler = new ILCompiler("native_source_names_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); return compiler;
    }
    private static CompilationArtifacts Compile(string source, bool debug = false)
    {
        var document = new SourceDocument("/native-source-identity/main.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).WithSourceDocument(document).ParseOrThrow();
        var checker = new TypeChecker(); checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.Check(statements);
        var compiler = Compiler(); compiler.EmitDebugSymbols = debug; compiler.SetSourceDocument(document);
        compiler.Compile(statements, types); return compiler.SaveArtifacts();
    }
    private static string Execute(byte[] bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(new MemoryStream(bytes)));
        return TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20));
    }
    private static void AssertUniqueTypes(byte[] bytes)
    {
        using var pe = new PEReader(new MemoryStream(bytes)); var reader = pe.GetMetadataReader();
        var identities = reader.TypeDefinitions.Select(handle =>
        {
            var type = reader.GetTypeDefinition(handle);
            return (Parent: MetadataTokens.GetToken(type.GetDeclaringType()), Namespace: reader.GetString(type.Namespace), Name: reader.GetString(type.Name));
        }).ToArray();
        Assert.Equal(identities.Length, identities.Distinct().Count());
    }
}
