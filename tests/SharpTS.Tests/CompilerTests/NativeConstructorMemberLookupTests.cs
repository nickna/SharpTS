using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeConstructorMemberLookupTests
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructorKeysKeepOrderEnumerabilityDeletionAndOwnIdentity(bool native)
    {
        const string source = """
            class Parent { static inherited=1; }
            class Child extends Parent { static first=2; static method() {} static get label(){return 'tag';} static last=3; }
            const child:any=Child;
            console.log(Object.keys(child).join(','));
            console.log(Object.getOwnPropertyNames(child).join(','));
            delete child.first; child.first=4;
            Object.defineProperty(child,'method',{enumerable:true});
            console.log(Object.keys(child).join(','));
            console.log(Object.hasOwn(child,'inherited'));
            """;
        var saved = Compile(source, native, out var compiler);
        Assert.Equal("first,last\nlength,name,prototype,method,label,first,last\nmethod,last,first\nfalse\n",
            TestHarness.RunCompiledWithTimeout(() => saved, TimeSpan.FromSeconds(20)));
        if (native) Assert.False(compiler.NativeConstructorMembers!.UsesLegacy);
        else { Assert.Null(compiler.NativeConstructorMembers); Assert.Null(saved.GetType("$NativeConstructorMemberLookup")); }
    }

    [Fact]
    public void ExactSourceEnumerationMatchesTheLoadedMetadataOrder()
    {
        var saved = Compile("class Parent { static one=1; } class Child extends Parent { static field=2; static method(){} static get label(){return 'x';} }", true, out var compiler);
        foreach (var original in compiler.NativeConstructorMembers!.Owners)
        {
            var owner = saved.GetType(original.FullName!)!;
            var actual = Assert.IsType<MemberInfo[]>(Helper(saved).Invoke(null, [owner, Flags]));
            Assert.Equal(owner.GetMembers(Flags), actual);
        }
        var error = Assert.Throws<TargetInvocationException>(() => Helper(saved).Invoke(null, [typeof(object), Flags]));
        Assert.Contains(NativeConstructorMemberLookup.UnsupportedOwnerMessage, error.InnerException!.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UnprovedGenericInventoriesRetainTheOriginalEnumeration(bool generic, bool unproved)
    {
        var saved = Compile((generic ? "class Owner<T> { static field=7; }" : "class Owner { static field=7; }") + (unproved ? " const C:any=Owner; new C();" : ""), true, out var compiler);
        Assert.Equal(unproved, compiler.NativeConstructorMembers!.UsesLegacy);
        if (unproved) Assert.Equal(typeof(Foreign).GetMembers(Flags), Assert.IsType<MemberInfo[]>(Helper(saved).Invoke(null, [typeof(Foreign), Flags])));
        else
        {
            var error = Assert.Throws<TargetInvocationException>(() => Helper(saved).Invoke(null, [saved.GetType("Owner"), BindingFlags.Public | BindingFlags.Instance]));
            Assert.Contains(NativeConstructorMemberLookup.UnsupportedOwnerMessage, error.InnerException!.Message);
        }
    }

    public sealed class Foreign { public static int Field=7; }
    private static MethodInfo Helper(Assembly saved) => saved.GetType("$NativeConstructorMemberLookup")!.GetMethod("GetMembers", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static Assembly Compile(string source, bool native, out ILCompiler compiler)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        compiler = new ILCompiler("native_constructor_members_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
