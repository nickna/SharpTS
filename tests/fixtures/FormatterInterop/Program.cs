#pragma warning disable SHARPTS_HOSTING001

using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Execution;
using SharpTS.FormatterInteropEvidence;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.References;
using SharpTS.TypeSystem;

if (args.Length != 3)
    throw new ArgumentException("Usage: <source-root> <formatted-root> <report-path>");

string sourceRoot = Path.GetFullPath(args[0]);
string formattedRoot = Path.GetFullPath(args[1]);
string reportPath = Path.GetFullPath(args[2]);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
FixtureCase[] cases = JsonSerializer.Deserialize<FixtureCase[]>(
    File.ReadAllText(Path.Combine(sourceRoot, "fixtures.json")), jsonOptions)
    ?? throw new InvalidOperationException("The formatter fixture manifest is empty.");
if (cases.Length == 0 || cases.Select(item => item.Name).Distinct().Count() != cases.Length)
    throw new InvalidOperationException("The fixture manifest must have unique, nonempty cases.");

// Load an implementation assembly through the same reference seam as run/compile. The import
// fixture calls a real method on this type; an unresolved, any-typed import cannot pass execution.
string referencePath = typeof(InteropProbe).Assembly.Location;
DotNetReferences.Load(sourceRoot, [referencePath]);
var observations = new List<CaseObservation>();
foreach (FixtureCase fixture in cases)
{
    if (!Enum.TryParse(fixture.DecoratorMode, out DecoratorMode decoratorMode) ||
        decoratorMode is not (DecoratorMode.Stage3 or DecoratorMode.Legacy))
        throw new InvalidOperationException($"Unsupported decorator mode for {fixture.Name}.");
    if (fixture.Execute && fixture.ExpectedOutput is null)
        throw new InvalidOperationException($"Executable fixture {fixture.Name} needs expectedOutput.");

    string originalPath = FixturePath(sourceRoot, fixture.Path);
    string formattedPath = FixturePath(formattedRoot, fixture.Path);
    string original = File.ReadAllText(originalPath);
    string formatted = File.ReadAllText(formattedPath);
    string[] lostComments = Regex.Matches(original, @"formatter-comment:[^\r\n*]+")
        .Select(match => match.Value.Trim())
        .Where(marker => !formatted.Contains(marker, StringComparison.Ordinal))
        .ToArray();
    Console.WriteLine($"Checking {fixture.Name}: original");
    var before = Observe(originalPath, fixture, decoratorMode);
    Console.WriteLine($"Checking {fixture.Name}: formatted");
    var after = Observe(formattedPath, fixture, decoratorMode);
    bool equivalent = before.Success && after.Success && lostComments.Length == 0 &&
        (!fixture.Execute ||
         (before.InterpretedOutput == after.InterpretedOutput &&
          before.CompiledOutput == after.CompiledOutput));
    observations.Add(new CaseObservation(
        fixture.Name, fixture.Path, fixture.DecoratorMode,
        Hash(originalPath), Hash(formattedPath), LineEndings(original), LineEndings(formatted),
        equivalent, lostComments, before, after));
}

bool success = observations.All(item => item.Equivalent);
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
File.WriteAllText(reportPath, JsonSerializer.Serialize(new
{
    success,
    recordedAtUtc = DateTimeOffset.UtcNow,
    compilerAssembly = typeof(Interpreter).Assembly.GetName().FullName,
    referenceAssembly = typeof(InteropProbe).Assembly.GetName().FullName,
    programOptions = TypeScriptProgramOptions.Default,
    checkerOptions = TypeCheckerOptions.Default with { Jsx = JsxMode.ReactJsx },
    jsxOptions = JsxParseOptions.Default,
    cases = observations,
}, jsonOptions) + "\n");
Console.WriteLine($"Formatter compiler evidence: {observations.Count} cases, " +
    $"{observations.Count(item => item.Equivalent)} passed.");
Console.WriteLine(reportPath);
return success ? 0 : 1;

static SourceObservation Observe(string path, FixtureCase fixture, DecoratorMode decoratorMode)
{
    string compiledPipeline = "not-run";
    int moduleCount = 0;
    string? interpreted = null;
    string? compiled = null;
    try
    {
        var resolver = new ModuleResolver(path, TypeScriptProgramOptions.Default)
        {
            JsxOptions = JsxParseOptions.Default,
        };
        Console.WriteLine("  load program");
        ParsedModule entry = resolver.LoadProgram(path, decoratorMode);
        List<ParsedModule> modules = resolver.GetModulesInOrder(entry);
        moduleCount = modules.Count;
        Diagnostic[] parseErrors = modules.SelectMany(module => module.ParseDiagnostics)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (parseErrors.Length != 0)
        {
            Console.WriteLine("  parse errors: " + string.Join("; ", parseErrors.Select(Describe)));
            return new(false, modules.Count, null, null, parseErrors.Select(Describe).ToArray());
        }

        var checker = new TypeChecker(TypeCheckerOptions.Default with { Jsx = JsxMode.ReactJsx })
            .WithFilePath(path);
        checker.SetDecoratorMode(decoratorMode);
        Console.WriteLine($"  check {modules.Count} modules");
        TypeMap types = checker.CheckModules(modules, resolver);
        var entryLexer = new Lexer(entry.Document!.Text)
        {
            JsxTolerant = path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase),
        };
        entryLexer.ScanTokens();
        var allDiagnostics = checker.GetDiagnostics();
        var entryDiagnostics = allDiagnostics.Where(diagnostic =>
            string.Equals(diagnostic.FilePath, path, StringComparison.OrdinalIgnoreCase));
        var checkedDiagnostics = allDiagnostics.Where(diagnostic =>
                !string.Equals(diagnostic.FilePath, path, StringComparison.OrdinalIgnoreCase))
            .Concat(TypeCheckPolicy.ApplyLineDirectives(entryDiagnostics, entryLexer.Pragmas));
        Diagnostic[] typeErrors = checkedDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (typeErrors.Length != 0)
        {
            Console.WriteLine("  type errors: " + string.Join("; ", typeErrors.Select(Describe)));
            return new(false, modules.Count, null, null, typeErrors.Select(Describe).ToArray());
        }
        if (!fixture.Execute)
            return new(true, modules.Count, null, null, []);

        // Match the CLI: declaration libraries participate in checking, never in execution or
        // emission. Executing the declaration graph can override real runtime globals.
        List<ParsedModule> runtimeModules = resolver.GetRuntimeModulesInOrder(entry);
        List<ParsedModule> emittedModules = modules.Where(module => !module.IsDeclarationFile).ToList();
        Console.WriteLine($"  interpret {runtimeModules.Count} runtime modules");
        using var interpretedOutput = new StringWriter();
        using (var interpreter = new Interpreter(interpretedOutput, interpretedOutput))
        {
            interpreter.SetDecoratorMode(decoratorMode);
            var variableResolver = new VariableResolver(interpreter);
            foreach (ParsedModule module in runtimeModules.Where(module => !module.IsBuiltIn))
                variableResolver.Resolve(module.Statements);
            interpreter.InterpretModules(runtimeModules, resolver, types);
            if (interpreter.LastUncaughtError is { } uncaught)
                throw new InvalidOperationException("Interpreter: " + uncaught.Message);
        }
        interpreted = Normalize(interpretedOutput.ToString());

        Console.WriteLine("  analyze dead code");
        bool compileAsScript = entry.IsScript && runtimeModules.Count == 1;
        compiledPipeline = compileAsScript ? "script" : "modules";
        List<Stmt> statements = compileAsScript ? entry.Statements
            : emittedModules.SelectMany(module => module.Statements).ToList();
        var deadCode = new DeadCodeAnalyzer(types).Analyze(statements);
        var compiler = new ILCompiler("FormatterInterop_" + Guid.NewGuid().ToString("N"));
        compiler.SetDecoratorMode(decoratorMode);
        Console.WriteLine(compileAsScript ? "  compile script" : "  compile modules");
        if (compileAsScript)
            compiler.Compile(statements, types, deadCode);
        else
            compiler.CompileModules(emittedModules, resolver, types, deadCode);
        Console.WriteLine("  save assembly");
        byte[] assemblyBytes = compiler.SaveToBytes();
        using var compiledOutput = new StringWriter();
        Console.WriteLine("  execute assembly");
        ExecuteCompiled(assemblyBytes, compiledOutput);
        compiled = Normalize(compiledOutput.ToString());
        string expected = Normalize(fixture.ExpectedOutput!);
        var errors = new List<string>();
        if (interpreted != expected)
            errors.Add($"Interpreted output differs from expected: {JsonSerializer.Serialize(expected)}.");
        if (compiled != expected)
            errors.Add($"Compiled output differs from expected: {JsonSerializer.Serialize(expected)}.");
        return new(errors.Count == 0, modules.Count, interpreted, compiled, errors.ToArray(), compiledPipeline);
    }
    catch (Exception error)
    {
        while (error is TargetInvocationException { InnerException: { } inner })
            error = inner;
        Console.WriteLine("  failed: " + error.Message);
        return new(false, moduleCount, interpreted, compiled, [error.ToString()], compiledPipeline);
    }
}

static string FixturePath(string root, string relative)
{
    string path = Path.GetFullPath(Path.Combine(root, relative));
    string suffix = Path.GetRelativePath(root, path);
    if (Path.IsPathRooted(suffix) || suffix == ".." ||
        suffix.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        throw new InvalidOperationException("Fixture paths must stay inside their source root.");
    return path;
}

static void ExecuteCompiled(byte[] assemblyBytes, TextWriter output)
{
    // Match the ordinary compiler test path. The surrounding smoke runner supplies a hard
    // process timeout; a cooperative cancellation flag cannot bound arbitrary emitted code.
    Assembly assembly = Assembly.Load(assemblyBytes);
    MethodInfo main = assembly.GetType("$Program")?.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Compiled assembly has no $Program.Main entry point.");
    TextWriter priorOutput = Console.Out;
    TextWriter priorError = Console.Error;
    SynchronizationContext? priorContext = SynchronizationContext.Current;
    Console.SetOut(output);
    Console.SetError(output);
    try
    {
        main.Invoke(null, null);
    }
    finally
    {
        SynchronizationContext.SetSynchronizationContext(priorContext);
        Console.SetOut(priorOutput);
        Console.SetError(priorError);
    }
}

static string Describe(Diagnostic diagnostic) =>
    $"{diagnostic.TsCode ?? diagnostic.Code.ToString()}: {diagnostic.Message}";
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
static string Normalize(string value) => value.ReplaceLineEndings("\n");
static string LineEndings(string value) => value.Contains("\r\n", StringComparison.Ordinal)
    ? (value.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n') ? "mixed" : "CRLF")
    : "LF";

internal sealed record FixtureCase(
    string Name, string Path, string DecoratorMode, bool Execute, string? ExpectedOutput);
internal sealed record SourceObservation(
    bool Success, int ModuleCount, string? InterpretedOutput, string? CompiledOutput, string[] Errors,
    string CompiledPipeline = "not-run");
internal sealed record CaseObservation(
    string Name, string Path, string DecoratorMode, string OriginalSha256, string FormattedSha256,
    string OriginalLineEndings, string FormattedLineEndings, bool Equivalent,
    string[] LostComments, SourceObservation Original, SourceObservation Formatted);
