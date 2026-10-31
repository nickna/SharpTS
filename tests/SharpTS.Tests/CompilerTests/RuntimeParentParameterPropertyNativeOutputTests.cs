using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.IntegrationTests;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class RuntimeParentParameterPropertyNativeOutputTests
{
    [Fact]
    public void IntactModuleAliasesRunFromVerifiedNativeModeSavedOutput()
    {
        using var directory = CliTestHelper.CreateTempDirectory();
        string entry = directory.GetPath("main.ts");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [directory.GetPath("lib.ts")] = """
                export class Base<T> { constructor(public value:T){} }
                export class Parent { constructor(public value:any=7,...rest:any[]){this.value+=":"+rest.join(",");} }
                """,
            [entry] = """
                import {Base as Generic, Parent as Imported} from "./lib";
                class Child extends Imported {}
                const C:any=Child;
                const G:any=Generic;
                console.log(new C(undefined,1,2).value, new G("generic").value);
                """,
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry));
        var checker = new TypeChecker();
        var types = checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("receiver_parameter_properties_" + Guid.NewGuid().ToString("N"), false, true,
            null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput();
        compiler.CompileModules(modules, resolver, types);
        byte[] bytes = compiler.SaveToBytes();
        using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image));
        Assert.Equal("7:1,2 generic\n", TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
