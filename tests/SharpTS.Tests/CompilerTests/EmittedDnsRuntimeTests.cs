using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedDnsRuntimeTests
{
    [Fact]
    public void DnsAndPromiseConsumersPassILVerification()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { lookup, Resolver, getDefaultResultOrder } from 'dns';
                import { resolve4 } from 'dns/promises';
                console.log(getDefaultResultOrder());
                console.log(lookup('localhost'));
                const resolver = new Resolver();
                resolver.setServers(['127.0.0.1']);
                resolve4('localhost').then(value => console.log(value));
                """
        };
        Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
    }

    [Fact]
    public void DisabledFeatureHasNoMetadataAndReportsAccidentalUse()
    {
        var runtime = EmitRuntime(false);
        Assert.Null(runtime.Dns);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireDns).Message);
        Assert.DoesNotContain(runtime.RuntimeClass.Type.GetMethods(), method => method.Name.StartsWith("Dns", StringComparison.Ordinal));
    }

    [Fact]
    public void DeclaredHandleCanBeUsedBeforeBodyEmission()
    {
        var runtime = new EmittedRuntime();
        runtime.BeginDnsEmission();
        var dns = runtime.RequireDns();
        Assert.False(dns.IsComplete);
        Assert.Contains("Lookup", Assert.Throws<InvalidOperationException>(() => dns.Lookup).Message);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("dns_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Runtime");
        var method = type.DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        dns.Lookup = method;
        Assert.Same(method, dns.Lookup);
        Assert.Throws<InvalidOperationException>(runtime.BeginDnsEmission);
        Assert.Contains("GetDefaultResultOrder", Assert.Throws<InvalidOperationException>(dns.CompleteEmission).Message);
        Assert.False(dns.IsComplete);
    }

    [Fact]
    public void EnabledFeatureCompletesAllHandlesAndRejectsFurtherWrites()
    {
        var dns = EmitRuntime(true).RequireDns();
        Assert.True(dns.IsComplete);
        Assert.Equal("DnsLookup", dns.Lookup.Name);
        Assert.Equal(16, dns.PromisesWrapperMethods.Count);
        Assert.All(typeof(EmittedDnsRuntime).GetProperties()
            .Where(property => property.PropertyType == typeof(MethodBuilder)),
            property => Assert.NotNull(property.GetValue(dns)));
        Assert.Throws<InvalidOperationException>(() => dns.Lookup = dns.Lookup);
        Assert.Throws<InvalidOperationException>(() => dns.RegisterPromiseWrapper("extra", dns.Lookup));
        Assert.Throws<InvalidOperationException>(dns.CompleteEmission);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, MethodBuilder>>(dns.PromisesWrapperMethods);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("extra", dns.Lookup));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReusedEmitterKeepsDnsConstructionWithinEachAssembly(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var saved = new List<(Assembly Assembly, int GetOrderToken)>();
        var owners = new HashSet<EmittedDnsRuntime>();
        foreach (string? source in new[] { "import * as dns from 'dns';", "console.log(1);", null, "import * as dns from 'dns/promises';" })
        {
            var builder = new PersistedAssemblyBuilder(new AssemblyName($"dns_reuse_{Guid.NewGuid():N}"), typeof(object).Assembly);
            var module = builder.DefineDynamicModule("main");
            var features = source is null ? null : new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = features is null ? emitter.EmitAll(module) : emitter.EmitAll(module, features);
            using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
            using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
            Assert.Empty(verifier.Verify(bytes));
            var loaded = Assembly.Load(bytes.ToArray());
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), reference => reference.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(reference => reference.Name == "SharpTS.Hosting.Abstractions"));
            if (source == "console.log(1);")
            {
                Assert.Null(runtime.Dns);
                Assert.Null(loaded.GetType("$DnsDisplay1"));
                Assert.Null(loaded.GetType("$DnsAsyncCompletion"));
                continue;
            }

            var dns = runtime.RequireDns();
            Assert.True(owners.Add(dns));
            Assert.True(dns.IsComplete);
            Assert.Equal(16, dns.PromisesWrapperMethods.Count);
            Assert.All(dns.PromisesWrapperMethods.Values, method => Assert.Same(builder, method.Module.Assembly));
            saved.Add((loaded, dns.GetDefaultResultOrder.MetadataToken));
        }

        // Exercise earlier assemblies only after the emitter has constructed all later ones.
        foreach (var (assembly, getOrderToken) in saved)
        {
            var getOrder = assembly.ManifestModule.ResolveMethod(getOrderToken)!;
            Assert.Equal("verbatim", getOrder.Invoke(null, null));
            var one = Activator.CreateInstance(assembly.GetType("$DnsDisplay1")!)!;
            one.GetType().GetField("_hostname")!.SetValue(one, "first");
            one.GetType().GetField("_method")!.SetValue(one, typeof(Convert).GetMethod("ToString", [typeof(object)]));
            Assert.Equal("first", one.GetType().GetMethod("Invoke")!.Invoke(one, null));
            var two = Activator.CreateInstance(assembly.GetType("$DnsDisplay2")!)!;
            two.GetType().GetField("_arg0")!.SetValue(two, "left");
            two.GetType().GetField("_arg1")!.SetValue(two, "right");
            two.GetType().GetField("_method")!.SetValue(two, typeof(string).GetMethod("Concat", [typeof(object), typeof(object)]));
            Assert.Equal("leftright", two.GetType().GetMethod("Invoke")!.Invoke(two, null));
            one.GetType().GetField("_method")!.SetValue(one, typeof(Convert).GetMethod("ToInt32", [typeof(object)]));
            Assert.IsType<FormatException>(Assert.Throws<TargetInvocationException>(() =>
                one.GetType().GetMethod("Invoke")!.Invoke(one, null)).InnerException);

            var loopType = assembly.GetType("$EventLoop")!;
            var loop = loopType.GetMethod("GetInstance")!.Invoke(null, null)!;
            var failure = new InvalidOperationException("worker failed");
            foreach (var worker in new[] { Task.FromResult<object>("done"), Task.FromException<object>(failure), Task.FromCanceled<object>(new CancellationToken(true)) })
            {
                var completion = new TaskCompletionSource<object>();
                var closure = Activator.CreateInstance(assembly.GetType("$DnsAsyncCompletion")!, completion)!;
                loopType.GetMethod("Ref")!.Invoke(loop, null);
                closure.GetType().GetMethod("Schedule")!.Invoke(closure, [worker]);
                Assert.False(completion.Task.IsCompleted);
                Assert.Equal(true, loopType.GetMethod("HasPendingWork")!.Invoke(loop, null));
                Assert.Equal(0, loopType.GetMethod("PumpOnce")!.Invoke(loop, null));
                Assert.Equal(false, loopType.GetMethod("HasPendingWork")!.Invoke(loop, null));
                if (worker.IsCanceled)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await completion.Task);
                else if (worker.IsFaulted)
                    Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await completion.Task));
                else
                    Assert.Equal("done", await completion.Task);
            }
        }
    }

    private static EmittedRuntime EmitRuntime(bool usesDns)
    {
        var statements = new Parser(new Lexer("console.log(1);").ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        features.UsesDns = usesDns;
        features.UsesPromise = usesDns;
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"dns_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        return new RuntimeEmitter(TypeProvider.Runtime).EmitAll(assembly.DefineDynamicModule("main"), features);
    }
}
