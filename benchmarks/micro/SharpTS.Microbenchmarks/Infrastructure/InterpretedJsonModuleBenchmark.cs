using System.Reflection;
using SharpTS.Diagnostics;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Microbenchmarks.Infrastructure;

/// <summary>
/// Loads the JSON phase probes through the interpreter's real module resolver,
/// live import cells, and capturing callback boundary. The bridge publishes the
/// already-imported functions on this interpreter realm's global object only so
/// BenchmarkDotNet can invoke them repeatedly after module setup.
/// </summary>
internal sealed class InterpretedJsonModuleBenchmark : IDisposable
{
    private const string BuildName = "__sharpTSJsonImportedBuild";
    private const string StringifyName = "__sharpTSJsonImportedStringify";
    private const string ParseName = "__sharpTSJsonImportedParse";
    private const string RoundTripName = "__sharpTSJsonImportedRoundTrip";

    private readonly Interpreter _interpreter;
    private readonly RuntimeValue[] _arguments = new RuntimeValue[1];
    private readonly ISharpTSCallable _build;
    private readonly ISharpTSCallable _stringify;
    private readonly ISharpTSCallable _parse;
    private readonly ISharpTSCallable _roundTrip;

    private InterpretedJsonModuleBenchmark(
        Interpreter interpreter,
        SharpTSGlobalThis globalThis)
    {
        _interpreter = interpreter;
        _build = GetCallable(globalThis, BuildName);
        _stringify = GetCallable(globalThis, StringifyName);
        _parse = GetCallable(globalThis, ParseName);
        _roundTrip = GetCallable(globalThis, RoundTripName);
    }

    internal static InterpretedJsonModuleBenchmark Create()
    {
        const string assemblyName = "JsonImportedInterpreterModules";
        string virtualRoot = Path.Combine(
            Path.GetTempPath(), $"sharpts_benchmark_{assemblyName}");
        var virtualFiles = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, source) in JsonModuleBenchmark.LoadSources(
                     includeInterpreterBridge: true))
        {
            string fullPath = Path.GetFullPath(Path.Combine(
                virtualRoot, path.TrimStart('.', '/', '\\')));
            virtualFiles[fullPath] = source;
        }

        string entryPath = Path.GetFullPath(Path.Combine(
            virtualRoot, "json-interpreter-bridge.ts"));
        var resolver = new ModuleResolver(entryPath, virtualFiles);
        ParsedModule entryModule = resolver.LoadModule(entryPath);
        List<ParsedModule> modules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        TypeMap typeMap = checker.CheckModules(modules, resolver);
        List<Diagnostic> errors = checker.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                "Interpreter module benchmark type-check failed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, errors));
        }

        var interpreter = new Interpreter(
            stdout: TextWriter.Null,
            stderr: TextWriter.Null);
        interpreter.InterpretModules(modules, resolver, typeMap);

        PropertyInfo globalThisProperty = typeof(Interpreter).GetProperty(
            "GlobalThis", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Could not access the interpreter realm's global object");
        var globalThis = (SharpTSGlobalThis?)globalThisProperty.GetValue(interpreter)
            ?? throw new InvalidOperationException(
                "The interpreter realm did not create a global object");
        return new InterpretedJsonModuleBenchmark(interpreter, globalThis);
    }

    internal double Build(int n) => Invoke(_build, n);
    internal double Stringify(int n) => Invoke(_stringify, n);
    internal double Parse(int n) => Invoke(_parse, n);
    internal double RoundTrip(int n) => Invoke(_roundTrip, n);

    private double Invoke(ISharpTSCallable callable, int n)
    {
        _arguments[0] = RuntimeValue.FromNumber(n);
        return callable.CallV2(_interpreter, _arguments).AsNumber();
    }

    private static ISharpTSCallable GetCallable(
        SharpTSGlobalThis globalThis,
        string name)
        => globalThis.GetProperty(name) as ISharpTSCallable
            ?? throw new InvalidOperationException(
                $"Interpreter JSON bridge did not publish '{name}'");

    public void Dispose() => _interpreter.Dispose();
}
