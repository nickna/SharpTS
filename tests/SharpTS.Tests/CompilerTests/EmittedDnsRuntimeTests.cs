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
        Assert.DoesNotContain(runtime.RuntimeType.GetMethods(), method => method.Name.StartsWith("Dns", StringComparison.Ordinal));
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
