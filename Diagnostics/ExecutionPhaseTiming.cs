namespace SharpTS.Diagnostics;

/// <summary>
/// One completed or failed stage in an execution or compilation pipeline.
/// </summary>
/// <param name="Name">Stable, architecture-level phase identifier.</param>
/// <param name="DurationMs">Precise wall-clock duration in milliseconds.</param>
/// <param name="Status"><c>completed</c> or <c>failed</c>.</param>
public sealed record ExecutionPhaseTiming(
    string Name,
    double DurationMs,
    string Status)
{
    public const string CompletedStatus = "completed";
    public const string FailedStatus = "failed";

    // Embedding and compiler phase names are protocol identifiers. Keep these values stable.
    public const string Tokenize = "tokenize";
    public const string Parse = "parse";
    public const string ValidateModules = "validateModules";
    public const string TypeCheck = "typeCheck";
    public const string PrepareInterpreter = "prepareInterpreter";
    public const string Execute = "execute";
    public const string Load = "load";
    public const string AnalyzeDeadCode = "analyzeDeadCode";
    public const string InitializeCompiler = "initializeCompiler";
    public const string PrepareCompilation = "prepareCompilation";
    public const string ExtractNamespaces = "extractNamespaces";
    public const string EmitRuntimeTypes = "emitRuntimeTypes";
    public const string AnalyzeClosures = "analyzeClosures";
    public const string DefineProgramStructure = "defineProgramStructure";
    public const string AnalyzeModuleBindings = "analyzeModuleBindings";
    public const string DefineDeclarations = "defineDeclarations";
    public const string CollectFunctions = "collectFunctions";
    public const string EmitFunctionBodies = "emitFunctionBodies";
    public const string EmitMethodBodies = "emitMethodBodies";
    public const string EmitModuleInitializers = "emitModuleInitializers";
    public const string EmitEntryPoint = "emitEntryPoint";
    public const string FinalizeTypes = "finalizeTypes";
    public const string SerializeAssembly = "serializeAssembly";

    // Compile-command-only phase names.
    public const string ResolveConfiguration = "resolveConfiguration";
    public const string LoadReferences = "loadReferences";
    public const string LoadPackageMetadata = "loadPackageMetadata";
    public const string LoadModules = "loadModules";
    public const string LoadDynamicImports = "loadDynamicImports";
    public const string TypeCheckDynamicImports = "typeCheckDynamicImports";
    public const string EmitDeclarations = "emitDeclarations";
    public const string VerifyAssembly = "verifyAssembly";
    public const string BundleExecutable = "bundleExecutable";
    public const string GenerateRuntimeConfig = "generateRuntimeConfig";
    public const string CopyRuntime = "copyRuntime";
    public const string CopyDependencies = "copyDependencies";
    public const string CreatePackage = "createPackage";
    public const string PushPackage = "pushPackage";

    internal static ExecutionPhaseTiming Completed(string name, double durationMs) =>
        new(name, Math.Max(0, durationMs), CompletedStatus);

    internal static ExecutionPhaseTiming Failed(string name, double durationMs) =>
        new(name, Math.Max(0, durationMs), FailedStatus);
}
