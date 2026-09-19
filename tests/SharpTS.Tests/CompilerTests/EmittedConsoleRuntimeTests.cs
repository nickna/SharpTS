using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedConsoleRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedConsoleRuntime).GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void EveryMissingDeclarationRejectsReadsAndCompletionThenAllowsRetry(string missingHandle)
    {
        var console = new EmittedConsoleRuntime();
        FillDeclarations(console, missingHandle);
        var property = typeof(EmittedConsoleRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(console));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(console.CompleteEmission).Message);
        Assert.False(console.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(console, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        var declarations = new EmittedConsoleRuntime();
        FillDeclarations(declarations);
        property.SetValue(console, property.GetValue(declarations));
        console.CompleteEmission();
        AssertFrozen(console);
    }

    [Fact]
    public void RequiredComponentIsImmediatelyAvailableAndCannotBeReplaced()
    {
        var runtime = new EmittedRuntime();
        var console = runtime.Console;
        Assert.NotNull(console);
        Assert.False(console.IsComplete);
        Assert.Same(console, runtime.Console);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Console))!.SetMethod);
        FillDeclarations(console);
        console.CompleteEmission();
        AssertFrozen(console);
    }

    [Fact]
    public void EarlyFieldAndForwardMethodDeclarationsCanBeUsedBeforeCompletion()
    {
        var console = new EmittedConsoleRuntime();
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("console_forward"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Helpers");
        console.GroupLevelField = type.DefineField("Level", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        console.GetIndent = type.DefineMethod("Indent", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), Type.EmptyTypes);
        var caller = type.DefineMethod("Call", MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), Type.EmptyTypes);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Stsfld, console.GroupLevelField);
        il.Emit(OpCodes.Call, console.GetIndent);
        il.Emit(OpCodes.Ret);
        Assert.False(console.IsComplete);
        Assert.Contains("'TimersField'", Assert.Throws<InvalidOperationException>(() => console.TimersField).Message);
        console.GetIndent.GetILGenerator().Emit(OpCodes.Ldsfld, console.GroupLevelField);
        console.GetIndent.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        Assert.Equal(2, Assembly.Load(bytes.ToArray()).GetType("Helpers")!.GetMethod("Call")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("const value = 1;", false)]
    [InlineData("console.log('value');", false)]
    [InlineData("import * as os from 'os';", false)]
    [InlineData("import * as child from 'child_process';", false)]
    [InlineData("new Uint8Array(2);", false)]
    [InlineData("const value = 1;", true)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void MinimalFeatureRichAndHostedEmissionCompleteEveryDeclaration(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        var console = runtime.Console;
        AssertFrozen(console);
        Assert.Equal(32, Handles.Count());
        foreach (var property in Handles)
        {
            var member = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(console));
            Assert.Same(runtime.RuntimeClass.Type, member.DeclaringType);
            if (member is MethodBuilder method) Assert.True(method.IsPublic && method.IsStatic);
            if (member is FieldBuilder field) Assert.True(field.IsPrivate && field.IsStatic);
        }
        Assert.True(runtime.RuntimeClass.Type.IsCreated());
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var fieldNames = reader.FieldDefinitions.Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name)).ToList();
        Assert.True(fieldNames.IndexOf("_consoleGroupLevel") < fieldNames.IndexOf("_consoleTimers"));
        Assert.True(fieldNames.IndexOf("_consoleTimers") < fieldNames.IndexOf("_consoleCounts"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedRuntimeVerifiesAndKeepsLazyConsoleStateIndependent(bool hosted)
    {
        using var bytes = Save(EmitRuntime("const value = 1;", hosted));
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var first = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        var second = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!;
        FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Null(Field(first, "_consoleTimers").GetValue(null));
        Assert.Null(Field(first, "_consoleCounts").GetValue(null));
        Assert.Equal(0, Field(first, "_consoleGroupLevel").GetValue(null));
        Assert.Equal("", first.GetMethod("GetConsoleIndent")!.Invoke(null, null));

        // These calls initialize dictionaries without redirecting process-global console streams.
        first.GetMethod("ConsoleTime")!.Invoke(null, [null]);
        first.GetMethod("ConsoleCountReset")!.Invoke(null, [null]);
        var timers = Assert.IsType<Dictionary<string, object>>(Field(first, "_consoleTimers").GetValue(null));
        var counts = Assert.IsType<Dictionary<string, object>>(Field(first, "_consoleCounts").GetValue(null));
        Assert.True(Assert.IsType<System.Diagnostics.Stopwatch>(timers["default"]).IsRunning);
        Assert.Equal(0d, counts["default"]);
        Assert.Null(Field(second, "_consoleTimers").GetValue(null));
        Assert.Null(Field(second, "_consoleCounts").GetValue(null));
        Field(first, "_consoleGroupLevel").SetValue(null, 2);
        Assert.Equal("    ", first.GetMethod("GetConsoleIndent")!.Invoke(null, null));
        Assert.Equal("", second.GetMethod("GetConsoleIndent")!.Invoke(null, null));
        first.GetMethod("ConsoleGroupEnd")!.Invoke(null, null);
        Assert.Equal("  ", first.GetMethod("GetConsoleIndent")!.Invoke(null, null));
        second.GetMethod("ConsoleTime")!.Invoke(null, ["other"]);
        Assert.NotSame(timers, Field(second, "_consoleTimers").GetValue(null));
        Assert.False(timers.ContainsKey("other"));
    }

    [Fact]
    public void ReusingEmitterKeepsEachCompilationMetadataIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime("const value = 1;", false, emitter).Console;
        var second = EmitRuntime("console.log(2);", false, emitter).Console;
        Assert.NotSame(first, second);
        foreach (var property in Handles) Assert.NotSame(property.GetValue(first), property.GetValue(second));
        AssertFrozen(first);
        AssertFrozen(second);
    }

    private static void FillDeclarations(EmittedConsoleRuntime console, string? missingHandle = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"console_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("Placeholder");
        var method = type.DefineMethod("Placeholder", MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        method.GetILGenerator().Emit(OpCodes.Ret);
        var field = type.DefineField("Placeholder", typeof(object), FieldAttributes.Public);
        foreach (var property in Handles.Where(property => property.Name != missingHandle))
            property.SetValue(console, property.PropertyType == typeof(FieldBuilder) ? (MemberInfo)field : method);
    }

    private static void AssertFrozen(EmittedConsoleRuntime console)
    {
        Assert.True(console.IsComplete);
        Assert.Throws<InvalidOperationException>(console.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(console);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(console, value));
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
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"console_metadata_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (source is null) return emitter.EmitAll(module);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        return emitter.EmitAll(module, new RuntimeFeatureDetector().Detect(statements));
    }
}
