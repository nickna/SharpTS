using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedHttpRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedHttpRuntime).GetProperties()
        .Where(property => property.Name != nameof(EmittedHttpRuntime.IsComplete));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Fact]
    public void AcceptWorkerDeclarationSupportsForwardCallsBeforeItsDeferredBody()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Http);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireHttp).Message);
        runtime.BeginHttpEmission();
        var http = runtime.RequireHttp();
        Assert.Same(http, runtime.Http);
        Assert.Throws<InvalidOperationException>(runtime.BeginHttpEmission);
        Assert.Contains("'AcceptWorkerMethod'", Assert.Throws<InvalidOperationException>(() => http.AcceptWorkerMethod).Message);
        Assert.Throws<ArgumentNullException>(() => http.AcceptWorkerMethod = null!);

        var assembly = new PersistedAssemblyBuilder(new AssemblyName("http_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Server");
        type.DefineDefaultConstructor(MethodAttributes.Public);
        new RuntimeEmitter(TypeProvider.Runtime).DeclareHttpServerAcceptWorker(type, http);
        var worker = http.AcceptWorkerMethod;
        var caller = type.DefineMethod("Call", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, http.AcceptWorkerMethod);
        il.Emit(OpCodes.Ret);
        Assert.False(type.IsCreated());
        Assert.False(http.IsComplete);
        worker.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        var loadedType = Assembly.Load(stream.ToArray()).GetType("Server")!;
        loadedType.GetMethod("Call")!.Invoke(Activator.CreateInstance(loadedType), null);
        Assert.Same(worker, http.AcceptWorkerMethod);
        Assert.Contains("'CreateServer'", Assert.Throws<InvalidOperationException>(http.CompleteEmission).Message);
        Assert.False(http.IsComplete);
    }

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void CompletionRejectsEachMissingDeclarationAndCanBeRetried(string missingHandle)
    {
        var http = CreateDeclarations(missingHandle);
        var property = typeof(EmittedHttpRuntime).GetProperty(missingHandle)!;
        var readError = Assert.Throws<TargetInvocationException>(() => property.GetValue(http));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(readError.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(http.CompleteEmission).Message);
        Assert.False(http.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(http, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(http, property.GetValue(CreateDeclarations()));
        http.CompleteEmission();
        AssertFrozen(http);
    }

    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("console.log(1);", true)]
    [InlineData("import * as net from 'net';", false)]
    [InlineData("import * as tls from 'tls';", false)]
    [InlineData("import * as crypto from 'crypto';", false)]
    public void DisabledFeatureOmitsHttpMetadataAndTypes(string source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Null(runtime.Http);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireHttp).Message);
        foreach (var name in new[] { "createServer", "request", "get" })
            Assert.Null(runtime.BuiltInModules.GetOptional("http", name));
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name));
        foreach (var name in new[] { "$HttpServer", "$HttpRequest", "$HttpResponse", "$HttpAcceptClosure" })
            Assert.DoesNotContain(name, names);
        Assert.DoesNotContain(reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name).StartsWith("Http", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("import * as http from 'http';", false)]
    [InlineData("import { createServer } from 'node:http';", false)]
    [InlineData("import * as https from 'https';", false)]
    [InlineData("fetch('http://127.0.0.1/');", false)]
    [InlineData("new Headers();", false)]
    [InlineData("new Request('http://127.0.0.1/');", false)]
    [InlineData("new Response('body');", false)]
    [InlineData("import * as http from 'http';", true)]
    [InlineData(null, false)]
    public void EnabledImpliedAndFullEmissionCompleteAllHandles(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var http = runtime.RequireHttp();
        AssertFrozen(http);
        Assert.True(runtime.RequireNet().IsComplete);
        Assert.True(runtime.RequirePromise().IsComplete);
        Assert.False(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Http))!.SetMethod!.IsPublic);
        Assert.Equal("$HttpServer", http.ServerType.Name);
        Assert.Equal("$HttpRequest", http.RequestType.Name);
        Assert.Equal("$HttpResponse", http.ResponseType.Name);
        Assert.Same(http.ServerType, http.ServerCtor.DeclaringType);
        Assert.Same(http.ServerType, http.AcceptWorkerMethod.DeclaringType);
        Assert.Same(http.ServerType, http.ServerRequestCompletedMethod.DeclaringType);
        Assert.Same(http.RequestType, http.RequestCtor.DeclaringType);
        Assert.Same(http.RequestType, http.RequestAbortedField.DeclaringType);
        Assert.Same(http.ResponseType, http.ResponseCtor.DeclaringType);
        Assert.Same(http.ResponseType, http.ResponseCompletionField.DeclaringType);
        Assert.Equal("$HttpAcceptClosure", http.AcceptClosureCtor.DeclaringType!.Name);
        Assert.Same(http.AcceptClosureCtor.DeclaringType, http.AcceptClosureRun.DeclaringType);
        foreach (var property in Handles)
        {
            var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(http));
            var owner = Assert.IsAssignableFrom<TypeBuilder>(handle is Type ? handle : handle.DeclaringType);
            Assert.True(owner.IsCreated(), property.Name);
        }
        foreach (var name in new[] { "createServer", "request", "get" })
            Assert.NotNull(runtime.BuiltInModules.GetOptional("http", name));
    }

    [Fact]
    public void HttpRuntimeDoesNotIntroduceGuestDependencies()
    {
        var runtime = EmitRuntime("import * as http from 'http';");
        Assert.Empty(runtime.RequiredSharpTSRuntimeReasons);
        Assert.Equal(SharpTSRuntimeRequirements.None, runtime.RequiredSharpTSRuntimeRequirements);
        using var stream = Save(runtime);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        Assert.DoesNotContain(reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == "SharpTS");
    }

    [Theory]
    [InlineData("const factory = createServer; const server = factory((req: any, res: any) => { res.writeHead(201); res.end(req.method); }); server.listen(0, '127.0.0.1', () => server.close());")]
    [InlineData("const server = http.createServer((req: any, res: any) => { req.on('end', () => res.end('done')); res.setHeader('X-Test', 'ok'); res.write('body'); }); server.closeIdleConnections(); server.closeAllConnections();")]
    [InlineData("http.request('http://127.0.0.1/', (res: any) => {}); http.get('http://127.0.0.1/', (res: any) => {});")]
    [InlineData("http.validateHeaderName('X-Test'); http.validateHeaderValue('X-Test', 'ok'); http.setMaxIdleHTTPParsers(10); console.log(http.METHODS, http.STATUS_CODES);")]
    [InlineData("const agent = new http.Agent({ keepAlive: true }); console.log(agent.getName({ host: 'localhost', port: 80 })); agent.destroy(); console.log(http.globalAgent.keepAlive);")]
    [InlineData("async function main() { const response = await fetch('http://127.0.0.1/'); console.log(await response.text()); } main();")]
    public void ModuleConsumersAndDeferredBodiesPassILVerification(string body)
    {
        var source = "import * as http from 'http'; import { createServer } from 'http';\n" + body;
        var errors = TestHarness.CompileModulesAndVerifyOnly(new() { ["main.ts"] = source }, "main.ts");
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static EmittedHttpRuntime CreateDeclarations(string? missingHandle = null)
    {
        var http = new EmittedHttpRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("http_incomplete"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(http, handle);
        }
        return http;
    }

    private static void AssertFrozen(EmittedHttpRuntime http)
    {
        Assert.True(http.IsComplete);
        Assert.Throws<InvalidOperationException>(http.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(http);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(http, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var stream = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(stream);
        stream.Position = 0;
        return stream;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"http_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null)
            return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
