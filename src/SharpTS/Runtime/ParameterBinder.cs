using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.Execution;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime;

/// <summary>
/// Helper for binding function parameters to arguments.
/// Handles rest parameters, default values, optional parameters, and required parameter validation.
/// </summary>
internal static class ParameterBinder
{
    // AST bodies are shared by invocations and receiver-bound views. Analyze
    // their declaration layout once, without retaining discarded scripts.
    private static readonly ConditionalWeakTable<List<Stmt>, BodyBindings> _bodyBindings = new();
    private static readonly BodyBindings _emptyBodyBindings = new([]);

    private sealed class BodyBindings
    {
        internal Dictionary<string, Stmt.Sequence>? Initializers { get; }
        internal List<Stmt> Statements { get; }
        internal List<(Token Name, bool IsFunction)> Declarations { get; }
        internal HashSet<string> FunctionNames { get; }
        internal List<Token> DestructuredNames { get; }

        internal BodyBindings(List<Stmt> body)
        {
            foreach (var statement in body)
                if (statement is Stmt.Sequence { ParameterName: { } parameterName } initializer)
                    (Initializers ??= new(StringComparer.Ordinal)).Add(parameterName.Lexeme, initializer);
            Statements = Initializers is null ? body
                : body.Where(statement => statement is not Stmt.Sequence { ParameterName: not null }).ToList();
            Declarations = BodyVarDeclarations(Statements).ToList();
            Declarations.AddRange(InterpreterVarDeclarations.CollectLoopHeads(Statements)
                .Select(name => (name, false)));
            FunctionNames = Declarations.Where(declaration => declaration.IsFunction)
                .Select(declaration => declaration.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
            DestructuredNames = Initializers is null ? []
                : Initializers.Values.SelectMany(initializer => LexicalDeclarationBindings.Collect(initializer.Statements)).ToList();
        }
    }

    private static BodyBindings GetBodyBindings(List<Stmt>? body)
        => body is null ? _emptyBodyBindings
            : _bodyBindings.GetValue(body, static statements => new BodyBindings(statements));

    /// <summary>
    /// Binds parameters to arguments in a synchronous context.
    /// </summary>
    /// <param name="parameters">The function's parameter declarations.</param>
    /// <param name="arguments">The arguments provided by the caller.</param>
    /// <param name="environment">The runtime environment to define parameters in.</param>
    /// <param name="interpreter">The interpreter for evaluating default values.</param>
    internal static void Bind(
        List<Stmt.Parameter> parameters,
        List<object?> arguments,
        RuntimeEnvironment environment,
        Interpreter interpreter,
        List<Stmt>? body = null)
    {
        var initializers = ParameterInitializers(body);
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                InitializeBinding(param, initializers, environment, interpreter);
                break; // Rest is always last
            }

            object? value;
            bool hasArg = i < arguments.Count;
            bool argIsUndefined = hasArg && arguments[i] is SharpTSUndefined;
            if (hasArg && !argIsUndefined)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue is { } defaultExpr)
            {
                // Per ECMA-262: default value applies when argument is missing OR explicitly undefined.
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.Evaluate(defaultExpr);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else
            {
                // Missing argument with no default — use undefined (JavaScript semantics).
                value = SharpTSUndefined.Instance;
            }
            environment.Define(param.Name.Lexeme, value);
            InitializeBinding(param, initializers, environment, interpreter);
        }
    }

    /// <summary>
    /// Binds parameters to RuntimeValue arguments without boxing.
    /// Used by the V2 call path for user-defined functions.
    /// </summary>
    internal static void BindRV(
        List<Stmt.Parameter> parameters,
        ReadOnlySpan<RuntimeValue> arguments,
        RuntimeEnvironment environment,
        Interpreter interpreter,
        List<Stmt>? body = null)
    {
        var initializers = ParameterInitializers(body);
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments as object? list
                var restArgs = new List<object?>();
                for (int j = i; j < arguments.Length; j++)
                    restArgs.Add(arguments[j].ToObject());
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                InitializeBinding(param, initializers, environment, interpreter);
                break; // Rest is always last
            }

            RuntimeValue value;
            bool hasArg = i < arguments.Length;
            bool argIsUndefined = hasArg && arguments[i].IsUndefined;
            if (hasArg && !argIsUndefined)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue is { } defaultExpr)
            {
                // Per ECMA-262: default value applies when argument is missing OR explicitly undefined.
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = interpreter.EvaluateRV(defaultExpr);
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else
            {
                // Missing argument with no default — use undefined (JavaScript semantics).
                value = RuntimeValue.Undefined;
            }
            environment.DefineRV(param.Name.Lexeme, value);
            InitializeBinding(param, initializers, environment, interpreter);
        }
    }

    /// <summary>
    /// Binds parameters to arguments in an asynchronous context.
    /// </summary>
    /// <param name="parameters">The function's parameter declarations.</param>
    /// <param name="arguments">The arguments provided by the caller.</param>
    /// <param name="environment">The runtime environment to define parameters in.</param>
    /// <param name="interpreter">The interpreter for evaluating default values.</param>
    internal static async Task BindAsync(
        List<Stmt.Parameter> parameters,
        List<object?> arguments,
        RuntimeEnvironment environment,
        Interpreter interpreter,
        List<Stmt>? body = null)
    {
        var initializers = ParameterInitializers(body);
        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];

            if (param.IsRest)
            {
                // Rest parameter - collect all remaining arguments
                var restArgs = arguments.Skip(i).ToList();
                environment.Define(param.Name.Lexeme, new SharpTSArray(restArgs));
                await InitializeBindingAsync(param, initializers, environment, interpreter);
                break; // Rest is always last
            }

            object? value;
            bool hasArg = i < arguments.Count;
            bool argIsUndefined = hasArg && arguments[i] is SharpTSUndefined;
            if (hasArg && !argIsUndefined)
            {
                value = arguments[i];
            }
            else if (param.DefaultValue is { } defaultExpr)
            {
                // Per ECMA-262: default value applies when argument is missing OR explicitly undefined.
                RuntimeEnvironment previous = interpreter.Environment;
                try
                {
                    interpreter.SetEnvironment(environment);
                    value = (await interpreter.EvaluateAsync(defaultExpr)).ToObject();
                }
                finally
                {
                    interpreter.SetEnvironment(previous);
                }
            }
            else
            {
                // Missing argument with no default — use undefined (JavaScript semantics).
                value = SharpTSUndefined.Instance;
            }
            environment.Define(param.Name.Lexeme, value);
            await InitializeBindingAsync(param, initializers, environment, interpreter);
        }
    }

    internal static bool HasParameterExpressions(IEnumerable<Stmt.Parameter> parameters)
        => parameters.Any(parameter => parameter.DefaultValue is not null || parameter.HasBindingExpressions);

    internal static Dictionary<string, Stmt.Sequence>? ParameterInitializers(List<Stmt>? body)
        => body is null ? null : GetBodyBindings(body).Initializers;

    internal static List<Stmt> BodyStatements(List<Stmt>? body)
        => GetBodyBindings(body).Statements;

    private static void InitializeBinding(Stmt.Parameter parameter,
        Dictionary<string, Stmt.Sequence>? initializers, RuntimeEnvironment environment, Interpreter interpreter)
    {
        if (initializers is null || !initializers.TryGetValue(parameter.Name.Lexeme, out var initializer)) return;
        var result = interpreter.ExecuteBlock([initializer], environment);
        if (result.Type == ExecutionResult.ResultType.Throw)
            throw Exceptions.ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
    }

    private static async Task InitializeBindingAsync(Stmt.Parameter parameter,
        Dictionary<string, Stmt.Sequence>? initializers, RuntimeEnvironment environment, Interpreter interpreter)
    {
        if (initializers is null || !initializers.TryGetValue(parameter.Name.Lexeme, out var initializer)) return;
        var result = await interpreter.ExecuteBlockAsync([initializer], environment);
        if (result.Type == ExecutionResult.ResultType.Throw)
            throw Exceptions.ThrowException.FromResult(result.Value.ToObject(), result.FromGuestThrow);
    }

    // Parser hoisting leaves function-owned vars in this statement-list scope;
    // nested functions and block lexical declarations belong to other scopes.
    internal static IEnumerable<(Token Name, bool IsFunction)> BodyVarDeclarations(IEnumerable<Stmt> body)
    {
        foreach (var statement in body)
        {
            switch (statement)
            {
                case Stmt.Var { IsVar: true } variable:
                    yield return (variable.Name, false);
                    break;
                case Stmt.Function { Body: not null } function:
                    yield return (function.Name, true);
                    break;
                case Stmt.Sequence { ParameterName: null } sequence:
                    foreach (var declaration in BodyVarDeclarations(sequence.Statements))
                        yield return declaration;
                    break;
            }
        }
    }

    internal static IEnumerable<(Token Name, bool IsFunction)> InterpreterBodyVarDeclarations(List<Stmt> body)
        => GetBodyBindings(body).Declarations;

    internal static (List<Stmt> Statements, RuntimeEnvironment Environment) PrepareBody(
        List<Stmt.Parameter> parameters, List<Stmt>? body, RuntimeEnvironment parameterEnvironment)
    {
        var bindings = GetBodyBindings(body);
        var statements = bindings.Statements;
        bool separateBody = HasParameterExpressions(parameters);
        var environment = separateBody
            ? new RuntimeEnvironment(parameterEnvironment)
            : parameterEnvironment;
        if (bindings.Declarations.Count == 0) return (statements, environment);
        var parameterNames = parameters.Select(parameter => parameter.Name.Lexeme).ToHashSet(StringComparer.Ordinal);
        if (parameterEnvironment.IsDefinedLocally("arguments")) parameterNames.Add("arguments");
        foreach (var name in bindings.DestructuredNames) parameterNames.Add(name.Lexeme);

        foreach (var (name, isFunction) in bindings.Declarations)
        {
            // Function initialization runs in ExecuteBlock and overrides a
            // same-name parameter copy. Do not hide it behind a precreated slot.
            if (isFunction || bindings.FunctionNames.Contains(name.Lexeme) || environment.IsDefinedLocally(name.Lexeme)) continue;
            environment.Define(name.Lexeme, parameterNames.Contains(name.Lexeme)
                ? parameterEnvironment.Get(name) : RuntimeValue.Undefined);
        }
        return (statements, environment);
    }
}
