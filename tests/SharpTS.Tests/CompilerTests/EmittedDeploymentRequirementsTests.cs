using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedDeploymentRequirementsTests
{
    [Fact]
    public void EmptySelectionCompletesWithoutADeploymentDependency()
    {
        var owner = new EmittedRuntime().Deployment;
        var other = new EmittedRuntime().Deployment;
        Assert.Empty(owner.Reasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, owner.Requirements);
        owner.CompleteEmission();
        Assert.True(owner.IsComplete);
        Assert.False(other.IsComplete);
        Assert.Throws<InvalidOperationException>(() => owner.Require("late"));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        other.Require("other");
        Assert.Empty(owner.Reasons);
    }

    [Fact]
    public void ReasonsAreOrdinalImmutableSnapshotsAndCapabilitiesAccumulate()
    {
        var owner = new EmittedRuntime().Deployment;
        owner.Require("z", SharpTSRuntimeRequirements.FullDependencyClosure);
        var snapshot = owner.Reasons;
        owner.Require("A");
        owner.Require("a");
        owner.Require(new string('z', 1), SharpTSRuntimeRequirements.ManagedCompilerHost);
        Assert.Equal(new[] { "z" }, snapshot);
        Assert.Equal(new[] { "A", "a", "z" }, owner.Reasons);
        Assert.Equal((SharpTSRuntimeRequirements)7, owner.Requirements);
        var collection = Assert.IsAssignableFrom<ICollection<string>>(owner.Reasons);
        Assert.True(collection.IsReadOnly);
        Assert.Throws<NotSupportedException>(collection.Clear);
        Assert.Throws<NotSupportedException>(() => collection.Add("injected"));
        Assert.Throws<NotSupportedException>(() => collection.Remove("z"));
        owner.CompleteEmission();
        Assert.Throws<InvalidOperationException>(() => owner.Require("z"));
        Assert.Equal(new[] { "A", "a", "z" }, collection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void InvalidReasonsLeaveStateUnchangedAndAllowRetry(string? reason)
    {
        var owner = new EmittedRuntime().Deployment;
        Assert.ThrowsAny<ArgumentException>(() => owner.Require(reason!));
        Assert.Empty(owner.Reasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, owner.Requirements);
        Assert.False(owner.IsComplete);
        owner.Require("valid");
        owner.CompleteEmission();
        Assert.Equal(new[] { "valid" }, owner.Reasons);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    [InlineData(64)]
    public void UnknownCapabilitiesCannotPartiallyRecordAReason(int flags)
    {
        var owner = new EmittedRuntime().Deployment;
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.Require("invalid", (SharpTSRuntimeRequirements)flags));
        Assert.Empty(owner.Reasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, owner.Requirements);
        owner.CompleteEmission();
        Assert.True(owner.IsComplete);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryCapabilityCombinationImpliesTheRuntimeAssembly(int flags)
    {
        var owner = new EmittedRuntime().Deployment;
        owner.Require("dependency", (SharpTSRuntimeRequirements)flags);
        owner.CompleteEmission();
        Assert.Equal((SharpTSRuntimeRequirements)(flags | 1), owner.Requirements);
    }

    public static IEnumerable<object[]> CompilationPaths => Enumerable.Range(0, 8)
        .Select(mask => new object[] { (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0 });

    [Theory]
    [MemberData(nameof(CompilationPaths))]
    public void CompilerFreezesEarlyAndLateRequirementsAfterGuestEmission(bool modules, bool timed, bool hosted)
    {
        foreach (bool dependencies in new[] { true, false })
        {
            string definitions = dependencies
                ? "function use(){ const source: string = '1 + 2'; return eval(source); } function construct(){ return new Worker('worker.ts'); } const proxy: any = new Proxy({}, {});"
                : "function use(){ return 3; }";
            var compiler = new ILCompiler($"deployment_lifetime_{Guid.NewGuid():N}");
            if (timed) compiler.SetTimingCollector(new ExecutionTimingCollector());
            if (hosted) compiler.EnableHostedOutput();
            using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
            if (modules)
            {
                directory.CreateFile("use.ts", definitions.Replace("function use", "export function use"));
                var entry = directory.CreateFile("main.ts", "import {use} from './use'; console.log(use());");
                var resolver = new ModuleResolver(entry);
                var ordered = resolver.GetModulesInOrder(resolver.LoadModule(entry));
                compiler.CompileModules(ordered, resolver, TestHarness.CheckModulesOrThrow(new TypeChecker(), ordered, resolver));
            }
            else
            {
                var statements = new Parser(new Lexer(definitions + "console.log(use());").ScanTokens()).ParseOrThrow();
                compiler.Compile(statements, new TypeChecker().Check(statements));
            }
            var runtime = (EmittedRuntime)typeof(ILCompiler).GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compiler)!;
            var owner = runtime.Deployment;
            Assert.True(owner.IsComplete);
            Assert.Equal(dependencies ? new[] { "Proxy", "Worker", "eval()" } : Array.Empty<string>(), owner.Reasons);
            Assert.Equal(dependencies ? (SharpTSRuntimeRequirements)7 : SharpTSRuntimeRequirements.None, owner.Requirements);
            Assert.Same(owner.Reasons, compiler.RequiredSharpTSRuntimeReasons);
            Assert.Equal(owner.Requirements, compiler.RequiredSharpTSRuntimeRequirements);
            Assert.Throws<NotSupportedException>(() => ((ICollection<string>)compiler.RequiredSharpTSRuntimeReasons).Clear());
            Assert.Throws<InvalidOperationException>(() => owner.Require("after finalization"));
            Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(new MemoryStream(compiler.SaveToBytes())));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeEmitterLeavesLateRecordingOpenAndDoesNotReuseOwners(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var previous = new List<EmittedDeploymentRequirements>();
        foreach (string source in new[] { "const p = new Proxy({}, {});", "const n = 1;", "const p = new Proxy({}, {});" })
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName($"deployment_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
            var owner = emitter.EmitAll(assembly.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements)).Deployment;
            Assert.DoesNotContain(owner, previous);
            Assert.False(owner.IsComplete);
            Assert.Equal(source.Contains("Proxy"), owner.Reasons.Contains("Proxy"));
            owner.Require("late guest call");
            owner.CompleteEmission();
            previous.Add(owner);
        }
        Assert.All(previous, owner => Assert.True(owner.IsComplete));
        Assert.Equal(new[] { "Proxy", "late guest call" }, previous[0].Reasons);
        Assert.Equal(new[] { "late guest call" }, previous[1].Reasons);
    }
}
