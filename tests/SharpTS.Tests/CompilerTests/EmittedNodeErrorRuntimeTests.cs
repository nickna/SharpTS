using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedNodeErrorRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedNodeErrorRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var component = CreateDeclarations(missingHandle);
        var property = typeof(EmittedNodeErrorRuntime).GetProperty(missingHandle)!;
        var read = Assert.Throws<TargetInvocationException>(() => property.GetValue(component));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(read.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(component.CompleteEmission).Message);
        Assert.False(component.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(component, property.GetValue(CreateDeclarations()));
        component.CompleteEmission();
        AssertFrozen(component);
    }

    [Fact]
    public void RequiredOwnerIsAvailableAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.NotNull(runtime.NodeErrors);
        Assert.False(runtime.NodeErrors.IsComplete);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.NodeErrors))!.SetMethod);
        Assert.Throws<InvalidOperationException>(() => runtime.NodeErrors.Type);
        Assert.Null(runtime.FileSystem);
    }

    [Fact]
    public void ClassPrecedesLateConversionAndProcessErrorUsesExplicitDependencies()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("node_error_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var component = new EmittedNodeErrorRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        void Emit(string name, params object[] arguments) => typeof(RuntimeEmitter)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);
        Emit("EmitNodeErrorClass", module, component);
        Assert.Equal("$NodeError", component.Type.Name);
        Assert.True(((TypeBuilder)component.Ctor.DeclaringType!).IsCreated());
        Assert.True(component.CodeGetter.GetILGenerator().ILOffset > 0);
        Assert.True(component.SyscallGetter.GetILGenerator().ILOffset > 0);
        Assert.True(component.PathGetter.GetILGenerator().ILOffset > 0);
        Assert.Throws<InvalidOperationException>(() => component.Throw);
        Assert.Throws<InvalidOperationException>(component.CompleteEmission);
        Assert.False(component.IsComplete);

        var helpers = module.DefineType("Helpers", TypeAttributes.Public);
        Emit("EmitNodeErrorHelpers", helpers, component);
        Assert.Same(helpers, component.Throw.DeclaringType);
        Assert.False(helpers.IsCreated());
        var createException = helpers.DefineMethod("CreateException", MethodAttributes.Public | MethodAttributes.Static,
            typeof(Exception), [typeof(object)]);
        var bridge = createException.GetILGenerator();
        bridge.Emit(OpCodes.Ldarg_0);
        bridge.Emit(OpCodes.Castclass, typeof(Exception));
        bridge.Emit(OpCodes.Ret);
        var failure = helpers.DefineMethod("Failure", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        Emit("EmitProcessPosixError", failure.GetILGenerator(), component, createException, "setuid");
        helpers.CreateType();
        component.CompleteEmission();
        AssertFrozen(component);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        Verify(bytes);
        var loaded = Assembly.Load(bytes.ToArray());
        var error = Assert.Throws<TargetInvocationException>(() => loaded.GetType("Helpers")!.GetMethod("Failure")!.Invoke(null, null)).InnerException!;
        Assert.Equal(loaded.GetType("$NodeError"), error.GetType());
        Assert.Equal("EPERM", Get(error, "Code"));
        Assert.Equal("setuid", Get(error, "Syscall"));
        Assert.Null(Get(error, "Path"));
        Assert.Null(Get(error, "Errno"));
        Assert.Equal("EPERM: setuid: setuid EPERM", error.Message);
    }

    [Theory]
    [InlineData("const value=1;", false, false)]
    [InlineData("const value=1;", true, false)]
    [InlineData("import 'fs';", false, true)]
    [InlineData("import 'fs';", true, true)]
    [InlineData("import 'process';", false, false)]
    [InlineData("import 'process';", true, false)]
    [InlineData(null, false, true)]
    [InlineData(null, true, true)]
    public void RequiredDeclarationsExistIndependentlyOfFileSystemFeatures(string? source, bool hosted, bool fileSystem)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.NodeErrors);
        Assert.Equal(fileSystem, runtime.FileSystem is not null);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var type = reader.TypeDefinitions.Select(handle => reader.GetTypeDefinition(handle))
            .Single(definition => reader.GetString(definition.Name) == "$NodeError");
        Assert.Equal(new[] { "_code", "_syscall", "_path", "_errno" },
            type.GetFields().Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)));
        Assert.Equal(new[] { ".ctor", "get_Code", "get_Syscall", "get_Path", "get_Errno" },
            type.GetMethods().Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)));
        Assert.Contains(reader.MethodDefinitions, handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == "ThrowNodeError");
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedErrorsPreserveOptionalFieldsFormattingAndExceptionConversion(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import 'fs';", hosted));
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$NodeError")!;
        var convert = assembly.GetType("$Runtime")!.GetMethod("ThrowNodeError")!;
        foreach (var (syscall, path, errno, message) in new (string?, string?, int?, string)[]
        {
            (null, null, null, "ECUSTOM: detail"),
            ("original", null, 9, "ECUSTOM: original: detail"),
            (null, "local.txt", 0, "ECUSTOM: detail 'local.txt'"),
            ("original", "local.txt", -7, "ECUSTOM: original: detail 'local.txt'")
        })
        {
            var original = Assert.IsAssignableFrom<Exception>(type.GetConstructors().Single()
                .Invoke(["ECUSTOM", "detail", syscall, path, errno]));
            Assert.Equal(message, original.Message);
            Assert.Equal("ECUSTOM", Get(original, "Code"));
            Assert.Equal(syscall, Get(original, "Syscall"));
            Assert.Equal(path, Get(original, "Path"));
            Assert.Equal(errno, Get(original, "Errno"));
            var preserved = Convert(convert, original, "replacement", "replacement.txt");
            Assert.IsType<Exception>(preserved);
            Assert.NotSame(original, preserved);
            Assert.Equal(message, preserved.Message);
            AssertMetadata(preserved, "ECUSTOM", syscall, path);
            Assert.Empty(original.Data);
        }

        foreach (var (original, code) in new (Exception, string)[]
        {
            (new FileNotFoundException("missing"), "ENOENT"),
            (new DirectoryNotFoundException("directory"), "ENOENT"),
            (new UnauthorizedAccessException("denied"), "EACCES"),
            (new IOException("io"), "EACCES"),
            (new InvalidOperationException("invalid"), "EINVAL")
        })
        {
            var converted = Convert(convert, original, "read", "fixture.txt");
            Assert.Equal($"{code}: read: {original.Message}, path 'fixture.txt'", converted.Message);
            AssertMetadata(converted, code, "read", "fixture.txt");
            Assert.NotSame(original, converted);
            Assert.Empty(original.Data);
        }
    }

    [Fact]
    public void ReusingEmitterKeepsNodeErrorTypesAndHandlesWithinEachAssembly()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import 'fs';", false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        Assert.NotSame(first.NodeErrors, second.NodeErrors);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.NodeErrors), property.GetValue(second.NodeErrors));
        AssertFrozen(first.NodeErrors);
        AssertFrozen(second.NodeErrors);
        using var firstBytes = Save(first);
        using var secondBytes = Save(second);
        Verify(firstBytes);
        Verify(secondBytes);
        var firstType = Assembly.Load(firstBytes.ToArray()).GetType("$NodeError")!;
        var secondType = Assembly.Load(secondBytes.ToArray()).GetType("$NodeError")!;
        var firstError = Assert.IsAssignableFrom<Exception>(firstType.GetConstructors().Single().Invoke(["ONE", "first", null, null, null]));
        var secondError = Assert.IsAssignableFrom<Exception>(secondType.GetConstructors().Single().Invoke(["TWO", "second", null, null, null]));
        Assert.Equal("ONE", Get(firstError, "Code"));
        Assert.Equal("TWO", Get(secondError, "Code"));
        Assert.False(firstType.IsInstanceOfType(secondError));
        Assert.False(secondType.IsInstanceOfType(firstError));
    }

    private static object? Get(Exception error, string name) => error.GetType().GetMethod("get_" + name)!.Invoke(error, null);

    private static Exception Convert(MethodInfo method, Exception error, string syscall, string path) =>
        Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [error, syscall, path])).InnerException!;

    private static void AssertMetadata(Exception error, string code, string? syscall, string? path)
    {
        Assert.Equal(4, error.Data.Count);
        Assert.Equal(true, error.Data["__nodeError"]);
        Assert.Equal(code, error.Data["__code"]);
        Assert.Equal(syscall, error.Data["__syscall"]);
        Assert.Equal(path, error.Data["__path"]);
    }

    private static EmittedNodeErrorRuntime CreateDeclarations(string? missing = null)
    {
        var component = new EmittedNodeErrorRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"node_error_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        foreach (var property in Handles.Where(property => property.Name != missing))
        {
            object handle = property.PropertyType == typeof(Type) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? type.DefineDefaultConstructor(MethodAttributes.Public)
                : type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            property.SetValue(component, handle);
        }
        return component;
    }

    private static void AssertFrozen(EmittedNodeErrorRuntime component)
    {
        Assert.True(component.IsComplete);
        Assert.Throws<InvalidOperationException>(component.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(component);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(component, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        bytes.Position = 0;
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"node_errors_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
