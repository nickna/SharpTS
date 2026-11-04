using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class CompactClassFieldLifecycleProofTests
{
    [Theory]
    [InlineData("class C{first=1;read(){return this.first}write(value:number){this.first=value}}const c=new C();c.write(2);c.read();", true)]
    [InlineData("class C{first=(console.log(JSON.stringify(this)),1);later=7}const c=new C();c.first;", false)]
    [InlineData("class C{first:any=(this as any).later;later=7}const c=new C();c.first;", false)]
    [InlineData("let saved:any;class C{first=1;constructor(){saved=this}}const c=new C();c.first;", false)]
    [InlineData("class C{first=1;self():any{return this}}const c=new C();c.self();", false)]
    [InlineData("class C{first=1;remove(){delete this.first}}const c=new C();c.remove();", false)]
    public void ReceiverObservationsRequireLifecycleWhileTypedMemberUseStaysCompact(string source, bool compact)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"field_lifecycle_proof_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, new DeadCodeAnalyzer(typeMap).Analyze(statements));
        var bytes = compiler.SaveToBytes();
        using var input = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(input));
        var assembly = Assembly.Load(bytes);
        var storage = Assert.Single(assembly.GetType("C")!.GetFields(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic), field => field.Name == "_fields");
        Assert.Equal(compact, storage.IsStatic);
        Assert.Equal(!compact, assembly.GetType("$GuestOwnPropertyState") is not null);
    }
}
