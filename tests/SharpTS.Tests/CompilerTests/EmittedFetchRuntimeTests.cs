using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedFetchRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => new[]
        { typeof(EmittedFetchRuntime), typeof(EmittedFetchImplementation), typeof(EmittedFetchClientRuntime) }
        .SelectMany(type => Handles(type).Select(property => new object[] { type, property.Name }));

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEveryMissingHandleAndCanBeRetried(Type ownerType, string missingHandle)
    {
        var runtime = CreateDeclarations(ownerType, missingHandle);
        var fetch = runtime.Fetch;
        var owner = Owners(fetch).Single(value => value.GetType() == ownerType);
        var property = ownerType.GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(owner));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(fetch.CompleteEmission).Message);
        Assert.False(fetch.IsComplete);
        Assert.False(fetch.RequireImplementation().IsComplete);
        Assert.False(fetch.RequireImplementation().RequireClient().IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var completeOwner = Owners(CreateDeclarations().Fetch).Single(value => value.GetType() == ownerType);
        property.SetValue(owner, property.GetValue(completeOwner));
        fetch.CompleteEmission();
        AssertFrozen(fetch);
    }

    [Fact]
    public void AvailabilityStartsOnceAndCompletedCacheCannotEnableImplementation()
    {
        var fetch = new EmittedRuntime().Fetch;
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(fetch.RequireImplementation).Message);
        Assert.Contains("'CachedFunction'", Assert.Throws<InvalidOperationException>(fetch.CompleteEmission).Message);
        fetch.CachedFunction = CreateDeclarations().Fetch.CachedFunction;
        fetch.CompleteEmission();
        AssertFrozen(fetch);
        Assert.Throws<InvalidOperationException>(fetch.BeginImplementationEmission);

        var enabled = new EmittedRuntime().Fetch;
        enabled.BeginImplementationEmission();
        Assert.Throws<InvalidOperationException>(enabled.BeginImplementationEmission);
        var implementation = enabled.RequireImplementation();
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(implementation.RequireClient).Message);
        implementation.BeginClientEmission();
        Assert.Throws<InvalidOperationException>(implementation.BeginClientEmission);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Fetch))!.SetMethod);
        Assert.False(typeof(EmittedFetchRuntime).GetProperty(nameof(EmittedFetchRuntime.Implementation))!.SetMethod!.IsPublic);
        Assert.False(typeof(EmittedFetchImplementation).GetProperty(nameof(EmittedFetchImplementation.Client))!.SetMethod!.IsPublic);
    }

    [Fact]
    public void DeclaredHelperSupportsCallsBeforeItsBodyAndCompletion()
    {
        var fetch = new EmittedRuntime().Fetch;
        fetch.BeginImplementationEmission();
        var implementation = fetch.RequireImplementation();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("fetch_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        implementation.Invoke = type.DefineMethod("Fetch", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), Type.EmptyTypes);
        caller.GetILGenerator().Emit(OpCodes.Call, implementation.Invoke);
        caller.GetILGenerator().Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(implementation.IsComplete);
        implementation.Invoke.GetILGenerator().Emit(OpCodes.Ldstr, "later body");
        implementation.Invoke.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        Assert.Equal("later body", Assembly.Load(stream.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Fact]
    public void UnavailableHttpClientCallsRejectionHelperAndCompletesWithoutClientMetadata()
    {
        var runtime = CreateDeclarations(includeClient: false);
        var fetch = runtime.Fetch.RequireImplementation();
        var type = (TypeBuilder)runtime.Fetch.CachedFunction.DeclaringType!;
        runtime.BeginPromiseEmission();
        var reject = type.DefineMethod("Reject", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object)]);
        reject.GetILGenerator().Emit(OpCodes.Ldarg_0);
        reject.GetILGenerator().Emit(OpCodes.Ret);
        runtime.RequirePromise().TypeReject = reject;
        // HttpClient BCL lookups remain unavailable until the HTTP emitter initializes them.
        new RuntimeEmitter(TypeProvider.Runtime).EmitFetch(type, runtime);
        Assert.Null(fetch.Client);
        Assert.Equal("Fetch", fetch.Invoke.Name);
        type.CreateType();
        using var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)type.Assembly).Save(stream);
        var loadedType = Assembly.Load(stream.ToArray()).GetType(type.Name)!;
        Assert.Equal("HttpClient not available", loadedType.GetMethod(fetch.Invoke.Name)!.Invoke(null, [null, null]));
        runtime.Fetch.CompleteEmission();
        AssertFrozen(runtime.Fetch);
        Assert.Throws<InvalidOperationException>(fetch.BeginClientEmission);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(fetch.RequireClient).Message);
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("import * as net from 'net';", false)]
    [InlineData("import * as tls from 'tls';", false)]
    [InlineData("import * as crypto from 'crypto';", false)]
    public void DisabledFeatureCompletesOnlyTheRequiredCacheField(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.Fetch.Implementation);
        AssertFrozen(runtime.Fetch);
        Assert.Equal("_cachedFetchFunction", runtime.Fetch.CachedFunction.Name);
        Assert.Same(runtime.RuntimeType, runtime.Fetch.CachedFunction.DeclaringType);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name));
        foreach (var name in new[] { "$Headers", "$Request", "$Response", "$FetchResponse", "$FetchDisplayClass" })
            Assert.DoesNotContain(name, types);
        Assert.Contains(reader.FieldDefinitions,
            handle => reader.GetString(reader.GetFieldDefinition(handle).Name) == "_cachedFetchFunction");
        Assert.DoesNotContain(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "Fetch");
    }

    [Theory]
    [InlineData("fetch('http://127.0.0.1/');", false)]
    [InlineData("new Headers();", false)]
    [InlineData("new Request('http://127.0.0.1/');", false)]
    [InlineData("new Response('body');", false)]
    [InlineData("import * as http from 'http';", false)]
    [InlineData("import * as https from 'node:https';", false)]
    [InlineData("fetch('http://127.0.0.1/');", true)]
    [InlineData(null, false)]
    public void EnabledImpliedHostedAndFullEmissionCompleteAllHandles(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var implementation = runtime.Fetch.RequireImplementation();
        var client = implementation.RequireClient();
        AssertFrozen(runtime.Fetch);
        Assert.True(runtime.RequireHttp().IsComplete);
        Assert.True(runtime.RequireNet().IsComplete);
        Assert.True(runtime.RequirePromise().IsComplete);
        Assert.Same(implementation.HeadersType, implementation.HeadersCtor.DeclaringType);
        Assert.Same(implementation.HeadersType, implementation.HeadersSetMethod.DeclaringType);
        Assert.Same(implementation.ResponseType, implementation.ResponseCtor.DeclaringType);
        Assert.Same(client.DisplayClass, client.DisplayCtor.DeclaringType);
        Assert.Same(client.DisplayClass, client.DisplayInvoke.DeclaringType);
        Assert.Same(client.DisplayClass, client.DisplayHelperField.DeclaringType);
        foreach (var owner in Owners(runtime.Fetch))
        foreach (var property in Handles(owner.GetType()))
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(owner));
            Assert.True(Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType).IsCreated(), property.Name);
        }
    }

    [Fact]
    public void FunctionAndClientCachesRetainIdentityAndShareTheCookieJarWithoutGuestDependencies()
    {
        var runtime = EmitRuntime("fetch('http://127.0.0.1/');");
        var client = runtime.Fetch.RequireImplementation().RequireClient();
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
        var loaded = Assembly.Load(stream.ToArray()).GetType(runtime.RuntimeType.Name)!;
        var globalGet = loaded.GetMethod(runtime.GlobalObject.GetProperty.Name)!;
        var function = globalGet.Invoke(null, ["fetch"]);
        Assert.NotNull(function);
        Assert.Same(function, globalGet.Invoke(null, ["fetch"]));
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Assert.Same(function, loaded.GetField(runtime.Fetch.CachedFunction.Name, flags)!.GetValue(null));
        var getter = loaded.GetMethod(client.GetOrCreateHttpClient.Name, flags)!;
        var instances = new List<System.Net.Http.HttpClient>();
        try
        {
            foreach (var (redirect, cookies, field) in new[]
            {
                ("follow", false, client.FollowClientField), ("manual", false, client.NoRedirectClientField),
                ("follow", true, client.FollowCookiesClientField), ("manual", true, client.NoRedirectCookiesClientField)
            })
            {
                var instance = Assert.IsType<System.Net.Http.HttpClient>(getter.Invoke(null, [redirect, cookies]));
                Assert.DoesNotContain(instance, instances);
                instances.Add(instance);
                Assert.Same(instance, getter.Invoke(null, [redirect, cookies]));
                Assert.Same(instance, loaded.GetField(field.Name, flags)!.GetValue(null));
            }
            var jar = Assert.IsType<CookieContainer>(loaded.GetField(client.CookieContainerField.Name, flags)!.GetValue(null));
            loaded.GetMethod(client.CookieJarSetCookie.Name)!.Invoke(null, ["session=value; Path=/", "http://localhost/"]);
            Assert.Equal("session=value", jar.GetCookieHeader(new Uri("http://localhost/")));
            Assert.Equal("session=value", loaded.GetMethod(client.CookieJarGetCookies.Name)!.Invoke(null, ["http://localhost/"]));
            loaded.GetMethod(client.CookieJarClear.Name)!.Invoke(null, null);
            Assert.Empty(jar.GetAllCookies());
            Assert.Same(jar, loaded.GetField(client.CookieContainerField.Name, flags)!.GetValue(null));
        }
        finally
        {
            foreach (var instance in instances)
                instance.Dispose();
        }
    }

    [Theory]
    [InlineData("const headers = new Headers({ 'X-Test': 'one' }); headers.set('X-Test', 'two'); console.log(headers.get('x-test'), headers.entries());")]
    [InlineData("async function main() { const request = new Request('http://localhost/', { method: 'POST', body: 'body' }); console.log(await request.clone().text(), await request.text()); } main();")]
    [InlineData("async function main() { const response = new Response('body', { status: 201 }); console.log(await response.clone().arrayBuffer(), await response.text()); } main();")]
    [InlineData("Response.json({ ok: true }); Response.redirect('http://localhost/', 302); Response.error();")]
    [InlineData("fetch.cookieJar.setCookie('session=value', 'http://localhost/'); console.log(fetch.cookieJar.getCookies('http://localhost/')); fetch.cookieJar.clear();")]
    [InlineData("async function main() { const response = await fetch('http://localhost/', { redirect: 'manual', credentials: 'omit' }); console.log(await response.text()); } main();")]
    [InlineData("async function main() { try { await fetch('invalid-url'); } catch (error) { console.log('rejected'); } } main();")]
    public void WebApiConsumersAndPromiseDispatchPassILVerification(string source)
    {
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static EmittedRuntime CreateDeclarations(Type? missingOwner = null, string? missingHandle = null, bool includeClient = true)
    {
        var runtime = new EmittedRuntime();
        runtime.Fetch.BeginImplementationEmission();
        if (includeClient)
            runtime.Fetch.RequireImplementation().BeginClientEmission();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"fetch_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public | FieldAttributes.Static);
        foreach (var owner in Owners(runtime.Fetch))
        foreach (var property in Handles(owner.GetType()))
        {
            if (owner.GetType() == missingOwner && property.Name == missingHandle)
                continue;
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(owner, handle);
        }
        return runtime;
    }

    private static IEnumerable<object> Owners(EmittedFetchRuntime fetch)
    {
        yield return fetch;
        if (fetch.Implementation is not { } implementation)
            yield break;
        yield return implementation;
        if (implementation.Client is { } client)
            yield return client;
    }

    private static void AssertFrozen(EmittedFetchRuntime fetch)
    {
        foreach (var owner in Owners(fetch))
        {
            var type = owner.GetType();
            Assert.Equal(true, type.GetProperty("IsComplete")!.GetValue(owner));
            var completeError = Assert.Throws<TargetInvocationException>(() => type.GetMethod("CompleteEmission", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null));
            Assert.IsType<InvalidOperationException>(completeError.InnerException);
            foreach (var property in Handles(type))
            {
                var value = property.GetValue(owner);
                Assert.NotNull(value);
                Assert.False(property.SetMethod!.IsPublic);
                var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(owner, value));
                Assert.IsType<InvalidOperationException>(error.InnerException);
            }
        }
        Assert.Throws<InvalidOperationException>(fetch.BeginImplementationEmission);
        if (fetch.Implementation is { } implementation)
            Assert.Throws<InvalidOperationException>(implementation.BeginClientEmission);
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"fetch_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
