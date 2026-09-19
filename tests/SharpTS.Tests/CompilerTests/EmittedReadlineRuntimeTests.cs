using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedReadlineRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedReadlineRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var readline = CreateDeclarations(missingHandle);
        var property = typeof(EmittedReadlineRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(readline));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(readline.CompleteEmission).Message);
        Assert.False(readline.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(readline, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(readline, property.GetValue(CreateDeclarations()));
        readline.CompleteEmission();
        AssertFrozen(readline);
    }

    [Fact]
    public void OptionalComponentHasExplicitAvailabilityAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        Assert.Null(runtime.Readline);
        Assert.Contains("not enabled", Assert.Throws<InvalidOperationException>(runtime.RequireReadline).Message);
        runtime.BeginReadlineEmission();
        var readline = runtime.RequireReadline();
        Assert.Same(runtime.Readline, readline);
        Assert.Throws<InvalidOperationException>(runtime.BeginReadlineEmission);
        Assert.True(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Readline))!.SetMethod!.IsPrivate);
        var declarations = CreateDeclarations();
        foreach (var property in Handles) property.SetValue(readline, property.GetValue(declarations));
        readline.CompleteEmission();
        AssertFrozen(readline);
        Assert.Throws<InvalidOperationException>(runtime.BeginReadlineEmission);
    }

    [Fact]
    public void ConstructorAndFieldsAreAvailableBeforeLateQuestionAndTypeFinalization()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("readline_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var baseType = module.DefineType("Events", TypeAttributes.Public);
        var events = new EmittedEventEmitterRuntime { Type = baseType };
        events.Ctor = baseType.DefineDefaultConstructor(MethodAttributes.Public);
        events.Emit = baseType.DefineMethod("Emit", MethodAttributes.Public, typeof(bool), [typeof(string), typeof(object[])]);
        events.Emit.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
        events.Emit.GetILGenerator().Emit(OpCodes.Ret);
        baseType.CreateType();
        var readline = new EmittedReadlineRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        void Emit(string name, params object[] arguments) => typeof(RuntimeEmitter)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(emitter, arguments);

        Emit("EmitReadlineInterfaceTypeDefinition", module, readline, events);
        Assert.False(readline.InterfaceType.IsCreated());
        Assert.Same(readline.InterfaceType, readline.InterfaceCtor.DeclaringType);
        Assert.Same(readline.InterfaceType, readline.PromptField.DeclaringType);
        Assert.Throws<InvalidOperationException>(() => readline.CreateInterface);
        Assert.Throws<InvalidOperationException>(readline.CompleteEmission);
        var runtimeType = module.DefineType("Helpers", TypeAttributes.Public);
        var invoke = runtimeType.DefineMethod("InvokeValue", MethodAttributes.Public | MethodAttributes.Static,
            typeof(object), [typeof(object), typeof(object[])]);
        invoke.GetILGenerator().Emit(OpCodes.Ldnull);
        invoke.GetILGenerator().Emit(OpCodes.Ret);
        Emit("EmitReadlineMethods", runtimeType, readline);
        Assert.Same(runtimeType, readline.CreateInterface.DeclaringType);
        Assert.False(readline.InterfaceType.IsCreated());
        runtimeType.CreateType();
        Emit("EmitReadlineInterfaceFinalize", readline, invoke);
        Assert.True(readline.InterfaceType.IsCreated());
        readline.CompleteEmission();
        AssertFrozen(readline);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var loaded = Assembly.Load(bytes.ToArray());
        var instance = loaded.GetType("Helpers")!.GetMethod("ReadlineCreateInterface")!.Invoke(null, [null])!;
        Assert.Equal("> ", instance.GetType().GetMethod("GetPrompt")!.Invoke(instance, null));
        Assert.NotNull(instance.GetType().GetMethod("Question"));
    }

    [Theory]
    [InlineData("const value = 1;", false, false)]
    [InlineData("Buffer.from('only');", false, false)]
    [InlineData("new TextEncoder();", false, false)]
    [InlineData("import * as os from 'os';", false, false)]
    [InlineData("import * as readline from 'readline';", true, false)]
    [InlineData("import * as readline from 'node:readline';", true, false)]
    // Bare feature detection does not resolve the standard-library facade's internal primitive import.
    [InlineData("import * as readline from 'primitive:readline';", false, false)]
    [InlineData("const readline = require('readline');", true, false)]
    [InlineData("import('readline');", true, false)]
    [InlineData("const value = 1;", false, true)]
    [InlineData("import * as readline from 'readline';", true, true)]
    [InlineData(null, true, false)]
    [InlineData(null, true, true)]
    public void MinimalEnabledHostedAndFullEmissionPreserveFeatureSelection(string? source, bool enabled, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        Assert.Equal(enabled, runtime.Readline is not null);
        if (enabled)
        {
            var readline = runtime.RequireReadline();
            AssertFrozen(readline);
            Assert.Equal(7, Handles.Count());
            Assert.True(readline.InterfaceType.IsCreated());
            Assert.Same(runtime.EventEmitter.Type, readline.InterfaceType.BaseType);
            Assert.Same(readline.InterfaceType, readline.InterfaceCtor.DeclaringType);
            Assert.Same(readline.InterfaceType, readline.ClosedField.DeclaringType);
            Assert.Same(readline.InterfaceType, readline.PausedField.DeclaringType);
            Assert.Same(readline.InterfaceType, readline.PromptField.DeclaringType);
            Assert.True(readline.ClosedField.IsPrivate && readline.PausedField.IsPrivate);
            Assert.True(readline.PromptField.IsAssembly);
            Assert.Same(runtime.RuntimeClass.Type, readline.QuestionSync.DeclaringType);
            Assert.Same(runtime.RuntimeClass.Type, readline.CreateInterface.DeclaringType);
        }
        else Assert.Throws<InvalidOperationException>(runtime.RequireReadline);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var types = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Equal(enabled, types.Contains("$ReadlineInterface"));
        var methods = reader.MethodDefinitions.Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name)).ToArray();
        Assert.Equal(enabled, methods.Contains("ReadlineCreateInterface"));
        Assert.Equal(enabled, methods.Contains("ReadlineQuestionSync"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedInterfaceVerifiesAndKeepsInstanceStateIndependent(bool hosted)
    {
        using var bytes = Save(EmitRuntime("import * as readline from 'readline';", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$ReadlineInterface")!;
        var factory = assembly.GetType("$Runtime")!.GetMethod("ReadlineCreateInterface")!;
        var first = Activator.CreateInstance(type)!;
        var second = factory.Invoke(null, [new Dictionary<string, object> { ["prompt"] = "custom> " }])!;
        FieldInfo Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(false, Field("_closed").GetValue(first));
        Assert.Equal(false, Field("_paused").GetValue(first));
        Assert.Equal("> ", type.GetMethod("GetPrompt")!.Invoke(first, null));
        Assert.Equal("custom> ", type.GetMethod("GetPrompt")!.Invoke(second, null));
        type.GetMethod("SetPrompt")!.Invoke(first, ["changed> "]);
        Assert.Equal("changed> ", type.GetMethod("GetPrompt")!.Invoke(first, null));
        Assert.Equal("custom> ", type.GetMethod("GetPrompt")!.Invoke(second, null));
        Assert.Same(first, type.GetMethod("Pause")!.Invoke(first, null));
        Assert.Equal(true, Field("_paused").GetValue(first));
        Assert.Equal(false, Field("_paused").GetValue(second));
        Assert.Same(first, type.GetMethod("Resume")!.Invoke(first, null));
        Assert.Equal(false, Field("_paused").GetValue(first));
        Assert.Same(first, type.GetMethod("Close")!.Invoke(first, null));
        Assert.Equal(true, Field("_closed").GetValue(first));
        Assert.Equal(false, Field("_closed").GetValue(second));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("import * as readline from 'readline';", false, emitter).RequireReadline();
        var minimal = EmitRuntime("const value = 1;", false, emitter);
        var second = EmitRuntime("import * as readline from 'readline';", false, emitter).RequireReadline();
        Assert.Null(minimal.Readline);
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static EmittedReadlineRuntime CreateDeclarations(string? missingHandle = null)
    {
        var readline = new EmittedReadlineRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"readline_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var ctor = type.DefineDefaultConstructor(MethodAttributes.Public);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
        {
            object handle = property.PropertyType == typeof(TypeBuilder) ? type
                : property.PropertyType == typeof(ConstructorBuilder) ? ctor
                : property.PropertyType == typeof(FieldBuilder) ? field : method;
            property.SetValue(readline, handle);
        }
        return readline;
    }

    private static void AssertFrozen(EmittedReadlineRuntime readline)
    {
        Assert.True(readline.IsComplete);
        Assert.Throws<InvalidOperationException>(readline.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(readline);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(readline, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"readline_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
