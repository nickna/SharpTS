using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedDnsRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedDnsRuntime).GetProperties()
        .Where(property => property.PropertyType == typeof(MethodBuilder));

    private static readonly string[] WrapperNames =
    [
        "DnsPromisesResolve4", "DnsPromisesResolve6", "DnsPromisesResolveMx",
        "DnsPromisesResolveTxt", "DnsPromisesResolveSrv", "DnsPromisesResolveCname",
        "DnsPromisesResolveNs", "DnsPromisesResolveSoa", "DnsPromisesResolvePtr",
        "DnsPromisesResolveCaa", "DnsPromisesResolveNaptr", "DnsPromisesLookup",
        "DnsPromisesLookupService", "DnsPromisesResolve", "DnsPromisesReverse",
        "DnsResolverResolveAsync"
    ];

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });
    public static IEnumerable<object[]> RequiredWrappers => WrapperNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryDeclarationRejectsMissingNullAndDuplicateHandlesBeforeCompletion(string missingHandle)
    {
        var dns = new EmittedDnsRuntime();
        FillDeclarations(dns, missingHandle: missingHandle);
        var property = typeof(EmittedDnsRuntime).GetProperty(missingHandle)!;
        var missing = Assert.Throws<TargetInvocationException>(() => property.GetValue(dns));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(missing.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(dns.CompleteEmission).Message);
        Assert.False(dns.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(dns, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var original = CreateDeclaration();
        property.SetValue(dns, original);
        var duplicate = Assert.Throws<TargetInvocationException>(() => property.SetValue(dns, CreateDeclaration()));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(duplicate.InnerException).Message);
        Assert.Same(original, property.GetValue(dns));
        dns.CompleteEmission();
        AssertFrozen(dns);
    }

    [Theory]
    [MemberData(nameof(RequiredWrappers))]
    public void EveryRequiredWrapperRejectsMissingNullAndDuplicateDeclarationsThenAllowsCompletion(string missingWrapper)
    {
        var dns = new EmittedDnsRuntime();
        FillDeclarations(dns, missingWrapper: missingWrapper);
        Assert.Contains($"'{missingWrapper}'", Assert.Throws<InvalidOperationException>(() => dns.RequirePromiseWrapper(missingWrapper)).Message);
        Assert.Contains($"'{missingWrapper}'", Assert.Throws<InvalidOperationException>(dns.CompleteEmission).Message);
        Assert.False(dns.IsComplete);
        Assert.Throws<ArgumentNullException>(() => dns.RegisterPromiseWrapper(missingWrapper, null!));
        Assert.DoesNotContain(missingWrapper, dns.PromisesWrapperMethods.Keys);
        var original = CreateDeclaration();
        dns.RegisterPromiseWrapper(missingWrapper, original);
        Assert.Same(original, dns.RequirePromiseWrapper(missingWrapper));
        Assert.Throws<ArgumentException>(() => dns.RegisterPromiseWrapper(missingWrapper, CreateDeclaration()));
        Assert.Same(original, dns.PromisesWrapperMethods[missingWrapper]);
        dns.CompleteEmission();
        AssertFrozen(dns);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void InvalidWrapperNamesCannotCreateDeclarations(string? name)
    {
        var dns = new EmittedDnsRuntime();
        Assert.ThrowsAny<ArgumentException>(() => dns.RegisterPromiseWrapper(name!, CreateDeclaration()));
        Assert.Empty(dns.PromisesWrapperMethods);
    }

    [Fact]
    public void PromiseWrapperCanBeReferencedBeforeItsBodyExists()
    {
        var dns = new EmittedDnsRuntime();
        var builder = new PersistedAssemblyBuilder(new AssemblyName("dns_wrapper_forward"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var target = module.DefineType("Target", TypeAttributes.Public);
        var declaration = target.DefineMethod("Lookup", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        dns.RegisterPromiseWrapper("DnsPromisesLookup", declaration);
        var caller = module.DefineType("Caller", TypeAttributes.Public);
        var call = caller.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var il = call.GetILGenerator();
        il.Emit(OpCodes.Call, dns.RequirePromiseWrapper("DnsPromisesLookup"));
        il.Emit(OpCodes.Ret);
        caller.CreateType();
        Assert.False(dns.IsComplete);
        il = declaration.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, 27);
        il.Emit(OpCodes.Ret);
        target.CreateType();
        using var bytes = new MemoryStream();
        builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var loaded = Assembly.Load(bytes.ToArray());
        Assert.Equal(27, loaded.GetType("Caller")!.GetMethod("Run")!.Invoke(null, null));
    }

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
        var saved = new List<(Assembly Assembly, int GetOrderToken, string Marker)>();
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
            Assert.All(loaded.GetTypes().Where(type => type.Name.StartsWith("$Dns", StringComparison.Ordinal))
                .GroupBy(type => type.FullName), group => Assert.Single(group));
            var runtimeType = loaded.GetType("$Runtime")!;
            var methods = runtimeType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.All(methods.Where(method => method.Name.StartsWith("Dns", StringComparison.Ordinal))
                .GroupBy(method => method.Name + "(" + string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType)) + ")"),
                group => Assert.Single(group));
            var resultOrder = Assert.Single(runtimeType.GetFields(BindingFlags.Static | BindingFlags.NonPublic),
                field => field.Name == "_dnsResultOrder");
            var getOrder = Assert.Single(methods, method => method.Name == "DnsGetDefaultResultOrder");
            Assert.Equal(getOrder.MetadataToken, dns.GetDefaultResultOrder.MetadataToken);
            Assert.Same(dns.GetDefaultResultOrder, runtime.BuiltInModules.GetOptional("dns", "getDefaultResultOrder"));
            Assert.Same(dns.GetDefaultResultOrder, runtime.BuiltInModules.GetOptional("dns/promises", "getDefaultResultOrder"));
            string marker = $"assembly-{saved.Count}";
            resultOrder.SetValue(null, marker);
            saved.Add((loaded, dns.GetDefaultResultOrder.MetadataToken, marker));
        }

        // Exercise earlier assemblies only after the emitter has constructed all later ones.
        foreach (var (assembly, getOrderToken, marker) in saved)
        {
            var getOrder = assembly.ManifestModule.ResolveMethod(getOrderToken)!;
            Assert.Equal(marker, getOrder.Invoke(null, null));
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

    private static MethodBuilder CreateDeclaration()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName($"dns_declaration_{Guid.NewGuid():N}"), typeof(object).Assembly);
        return builder.DefineDynamicModule("main").DefineType("Runtime")
            .DefineMethod("Declared", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
    }

    private static void FillDeclarations(EmittedDnsRuntime dns, string? missingHandle = null, string? missingWrapper = null)
    {
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
            property.SetValue(dns, CreateDeclaration());
        foreach (string name in WrapperNames.Where(name => name != missingWrapper))
            dns.RegisterPromiseWrapper(name, CreateDeclaration());
    }

    private static void AssertFrozen(EmittedDnsRuntime dns)
    {
        Assert.True(dns.IsComplete);
        Assert.Throws<InvalidOperationException>(dns.CompleteEmission);
        foreach (var property in Handles)
        {
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(dns, property.GetValue(dns)));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
        foreach (string name in WrapperNames)
            Assert.Throws<InvalidOperationException>(() => dns.RegisterPromiseWrapper(name, dns.RequirePromiseWrapper(name)));
        var view = Assert.IsAssignableFrom<IDictionary<string, MethodBuilder>>(dns.PromisesWrapperMethods);
        Assert.Throws<NotSupportedException>(() => view[WrapperNames[0]] = CreateDeclaration());
        Assert.Throws<NotSupportedException>(view.Clear);
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
