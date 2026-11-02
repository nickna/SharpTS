using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericInterfaceCompletionTests
{
    private static string[] CheckProjection(string source)
    {
        string directory = Path.GetFullPath("generic-interface-completion");
        static List<Stmt> Parse(string text) => new Parser(new Lexer(text).ScanTokens()).ParseOrThrow();
        var library = new ParsedModule(Path.Combine(directory, "lib.base.d.ts"), Parse("""
            interface View<T> { readonly value: T; }
            interface ViewConstructor { new<T>(value: T): View<T>; }
            declare var View: ViewConstructor;
            """)) { IsDefaultLibrary = true, IsDeclarationFile = true };
        var extension = new ParsedModule(Path.Combine(directory, "lib.extension.d.ts"), Parse("""
            interface View<T> { later: T; readonly stable: T; convert(value: T): T; }
            """)) { IsDefaultLibrary = true, IsDeclarationFile = true };
        var entry = new ParsedModule(Path.Combine(directory, "main.ts"), Parse(source));
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { RespectLoadedLibraries = true });
        checker.CheckModules([library, extension, entry], new ModuleResolver(directory));
        return checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode ?? "").ToArray();
    }

    [Theory]
    [InlineData("const view=new View<number>(1);const n:number=view.later;view.later=2;const m:number=view.convert(3);")]
    [InlineData("const view=new View<string>('text');const s:string=view['later'];view.later='next';")]
    [InlineData("const view=new View(1);function read(){interface View<T>{unrelated:T;}const n:number=view.later;}")]
    public void RetainedConstructorResultsFollowOnlyTheirCompletedGenericDefinition(string source)
        => Assert.Empty(CheckProjection(source));

    [Theory]
    [InlineData("const view=new View(1);const s:string=view.later;", "TS2322")]
    [InlineData("const view=new View<string>('text');const n:number=view['later'];", "TS2322")]
    [InlineData("const view=new View(1);view.later='wrong';", "TS2322")]
    [InlineData("const view=new View(1);view.stable=2;", "TS2540")]
    [InlineData("const view=new View(1);view.convert('wrong');", "TS2345")]
    [InlineData("const view=new View(1);view.missing;", "TS2339")]
    [InlineData("function read(){interface View<T>{unrelated:T;}const local:View<number>={unrelated:1};local.later;}", "TS2339")]
    public void CompletionPreservesMemberTypesReadonlyAndDeclarationIdentity(string source, string code)
        => Assert.Contains(code, CheckProjection(source));

    private static string[] CheckDataView(string source, bool respectLibraries, params string[] libraries)
    {
        string path = Path.GetFullPath(Path.Combine("dataview-generic-completion", "main.ts"));
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            TypeScriptProgramOptions.Default with { Lib = libraries });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict with { RespectLoadedLibraries = respectLibraries });
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode ?? "").ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Es2022AugmentationKeepsTheConstructedBufferArgument(bool respectLibraries)
        => Assert.Empty(CheckDataView("""
            const buffer=new ArrayBuffer(8);const view=new DataView(buffer);
            view.setBigInt64(0,7n,true);view.setBigUint64(0,7n);
            const signed:bigint=view.getBigInt64(0);const unsigned:bigint=view['getBigUint64'](0);
            const same:ArrayBuffer=view.buffer;
            """, respectLibraries, "lib.es2022.d.ts", "lib.dom.d.ts"));

    [Fact]
    public void AuthoritativeEs5DoesNotInventBigIntMembers()
        => Assert.Contains("TS2339", CheckDataView(
            "const view=new DataView(new ArrayBuffer(8));view.getBigInt64(0);", true, "lib.es5.d.ts"));

    [Fact]
    public void RuntimeCompatibilityHasTypedBigIntMethodsOnTheActualLibraryDefinition()
        => Assert.Empty(CheckDataView("""
            const buffer=new ArrayBuffer(8);const view=new DataView(buffer);
            view.setBigInt64(0,7n);const value:bigint=view.getBigInt64(0);const same:ArrayBuffer=view.buffer;
            """, false, "lib.es5.d.ts"));

    [Theory]
    [InlineData("new DataView(1);", "TS2769")]
    [InlineData("new DataView(new ArrayBuffer(8),'bad');", "TS2769")]
    [InlineData("const view=new DataView(new ArrayBuffer(8));view.setBigInt64(0,1);", "TS2345")]
    [InlineData("const view=new DataView(new ArrayBuffer(8));const wrong:string=view.getBigInt64(0);", "TS2322")]
    [InlineData("const view=new DataView(new ArrayBuffer(8));view.missing;", "TS2339")]
    [InlineData("function read(){interface DataView<T>{value:T;}const view:DataView<number>={value:1};view.getBigInt64(0);}", "TS2339")]
    public void CompatibilityDoesNotWeakenConstructorsOrOtherInterfaces(string source, string code)
        => Assert.Contains(code, CheckDataView(source, false, "lib.es5.d.ts"));

    [Theory]
    [InlineData("const view=new DataView('buffer');const text:string=view.getBigInt64(0);const value:bigint=view.getBigUint64(0);", null)]
    [InlineData("const view=new DataView('buffer');const wrong:bigint=view.getBigInt64(0);", "TS2322")]
    [InlineData("const view=new DataView('buffer');view.getBigInt64=(offset:number):string=>'replacement';", "TS2540")]
    public void RealInheritedMembersWinAndNamespaceNamesCannotReplaceLibraryIdentity(string source, string? code)
    {
        string directory = Path.GetFullPath("generic-dataview-inherited-completion");
        static List<Stmt> Parse(string text) => new Parser(new Lexer(text).ScanTokens()).ParseOrThrow();
        var library = new ParsedModule(Path.Combine(directory, "lib.base.d.ts"), Parse("""
            interface NativeBase<T> { readonly getBigInt64: (offset: number) => T; }
            interface DataView<T> extends NativeBase<T> { readonly buffer: T; }
            interface DataViewConstructor { new<T>(buffer: T): DataView<T>; }
            declare var DataView: DataViewConstructor;
            declare namespace Collision { interface DataView<T> { unrelated: T; } }
            """)) { IsDefaultLibrary = true, IsDeclarationFile = true };
        var entry = new ParsedModule(Path.Combine(directory, "main.ts"), Parse(source));
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules([library, entry], new ModuleResolver(directory));
        var codes = checker.GetDiagnostics().Select(diagnostic => diagnostic.TsCode).ToArray();
        if (code is null) Assert.Empty(codes);
        else Assert.Contains(code, codes);
    }
}
