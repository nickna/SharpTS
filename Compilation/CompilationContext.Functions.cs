using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Function Compilation State
    // ============================================

    public Dictionary<string, MethodBuilder> Functions { get; }

    // Rest parameter info: function name -> (restParamIndex, regularParamCount)
    // If a function has a rest param, restParamIndex is its index, regularParamCount is non-rest param count
    public Dictionary<string, (int RestParamIndex, int RegularParamCount)>? FunctionRestParams { get; set; }

    // Function overloads for default parameters: function name -> list of overload methods
    public Dictionary<string, List<MethodBuilder>>? FunctionOverloads { get; set; }

    // Method overloads for default parameters: class name -> method name -> list of overload methods
    public Dictionary<string, Dictionary<string, List<MethodBuilder>>>? MethodOverloads { get; set; }

    // Track generic params per function for instantiation
    public Dictionary<string, GenericTypeParameterBuilder[]>? FunctionGenericParams { get; set; }

    // Track which functions are generic definitions
    public Dictionary<string, bool>? IsGenericFunction { get; set; }

    /// <summary>
    /// Resolves a simple function name to its qualified name for lookup in the Functions dictionary.
    /// </summary>
    public string ResolveFunctionName(string simpleFunctionName)
    {
        if (FunctionToModule != null && FunctionToModule.TryGetValue(simpleFunctionName, out var modulePath))
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(modulePath));
            return $"$M_{sanitizedModule}_{simpleFunctionName}";
        }
        return simpleFunctionName;
    }

    /// <summary>
    /// Gets the qualified function name for the current module context.
    /// </summary>
    public string GetQualifiedFunctionName(string simpleFunctionName)
    {
        if (CurrentModulePath == null)
            return simpleFunctionName;

        string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(CurrentModulePath));
        return $"$M_{sanitizedModule}_{simpleFunctionName}";
    }
}
