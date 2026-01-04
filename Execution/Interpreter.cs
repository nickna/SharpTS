using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

/// <summary>
/// Tree-walking interpreter that executes the AST.
/// </summary>
/// <remarks>
/// One of two execution paths after type checking (the other being <see cref="ILCompiler"/>).
/// Traverses the AST recursively, evaluating expressions and executing statements. Uses
/// <see cref="RuntimeEnvironment"/> for variable scopes and control flow exceptions
/// (<see cref="ReturnException"/>, <see cref="BreakException"/>, <see cref="ContinueException"/>)
/// for unwinding. Runtime values include <see cref="SharpTSClass"/>, <see cref="SharpTSInstance"/>,
/// <see cref="SharpTSFunction"/>, <see cref="SharpTSArray"/>, and <see cref="SharpTSObject"/>.
///
/// This class is split across multiple partial class files:
/// <list type="bullet">
///   <item><description>Interpreter.cs - Core infrastructure and statement dispatch</description></item>
///   <item><description>Interpreter.Statements.cs - Statement execution helpers (block, switch, try/catch, loops)</description></item>
///   <item><description>Interpreter.Expressions.cs - Expression dispatch and basic evaluators</description></item>
///   <item><description>Interpreter.Properties.cs - Property/member access (Get, Set, New, This)</description></item>
///   <item><description>Interpreter.Calls.cs - Function calls and binary/logical operators</description></item>
///   <item><description>Interpreter.Operators.cs - Compound assignment, increment, and utility methods</description></item>
/// </list>
/// </remarks>
/// <seealso cref="RuntimeEnvironment"/>
/// <seealso cref="ILCompiler"/>
public partial class Interpreter
{
    private RuntimeEnvironment _environment = new();
    private TypeMap? _typeMap;

    internal RuntimeEnvironment Environment => _environment;
    internal TypeMap? TypeMap => _typeMap;
    internal void SetEnvironment(RuntimeEnvironment env) => _environment = env;

    /// <summary>
    /// Executes a list of statements as the main entry point for interpretation.
    /// </summary>
    /// <param name="statements">The list of parsed statements to execute.</param>
    /// <param name="typeMap">Optional type map from static analysis for type-aware dispatch.</param>
    /// <remarks>
    /// Catches and reports runtime errors to the console. Each statement is executed
    /// sequentially via <see cref="Execute"/>.
    /// </remarks>
    public void Interpret(List<Stmt> statements, TypeMap? typeMap = null)
    {
        _typeMap = typeMap;
        try
        {
            foreach (Stmt statement in statements)
            {
                Execute(statement);
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
        }
    }

    /// <summary>
    /// Dispatches a statement to the appropriate execution handler using pattern matching.
    /// </summary>
    /// <param name="stmt">The statement AST node to execute.</param>
    /// <remarks>
    /// Handles all statement types including control flow (if, while, for, switch),
    /// declarations (var, function, class, enum), and control transfer (return, break, continue, throw).
    /// Control flow uses exceptions (<see cref="ReturnException"/>, <see cref="BreakException"/>,
    /// <see cref="ContinueException"/>, <see cref="ThrowException"/>) for stack unwinding.
    /// </remarks>
    private void Execute(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                ExecuteBlock(block.Statements, new RuntimeEnvironment(_environment));
                break;
            case Stmt.Sequence seq:
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                    Execute(s);
                break;
            case Stmt.Expression exprStmt:
                Evaluate(exprStmt.Expr);
                break;
            case Stmt.If ifStmt:
                if (IsTruthy(Evaluate(ifStmt.Condition)))
                {
                    Execute(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    Execute(ifStmt.ElseBranch);
                }
                break;
            case Stmt.While whileStmt:
                while (IsTruthy(Evaluate(whileStmt.Condition)))
                {
                    try
                    {
                        Execute(whileStmt.Body);
                    }
                    catch (BreakException)
                    {
                        break;
                    }
                    catch (ContinueException)
                    {
                        continue;
                    }
                }
                break;
            case Stmt.DoWhile doWhileStmt:
                do
                {
                    try
                    {
                        Execute(doWhileStmt.Body);
                    }
                    catch (BreakException)
                    {
                        break;
                    }
                    catch (ContinueException)
                    {
                        continue;
                    }
                } while (IsTruthy(Evaluate(doWhileStmt.Condition)));
                break;
            case Stmt.ForOf forOf:
                ExecuteForOf(forOf);
                break;
            case Stmt.ForIn forIn:
                ExecuteForIn(forIn);
                break;
            case Stmt.Break:
                throw new BreakException();
            case Stmt.Continue:
                throw new ContinueException();
            case Stmt.Switch switchStmt:
                ExecuteSwitch(switchStmt);
                break;
            case Stmt.TryCatch tryCatch:
                ExecuteTryCatch(tryCatch);
                break;
            case Stmt.Throw throwStmt:
                throw new ThrowException(Evaluate(throwStmt.Value));
            case Stmt.Var varStmt:
                object? value = null;
                if (varStmt.Initializer != null)
                {
                    value = Evaluate(varStmt.Initializer);
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                break;
            case Stmt.Function functionStmt:
                // Skip overload signatures (no body) - they're type-checking only
                if (functionStmt.Body == null) break;
                SharpTSFunction function = new(functionStmt, _environment);
                _environment.Define(functionStmt.Name.Lexeme, function);
                break;
            case Stmt.Class classStmt:
                object? superclass = null;
                if (classStmt.Superclass != null)
                {
                    superclass = _environment.Get(classStmt.Superclass);
                    if (superclass is not SharpTSClass)
                    {
                        throw new Exception("Superclass must be a class.");
                    }
                }

                _environment.Define(classStmt.Name.Lexeme, null);

                if (classStmt.Superclass != null)
                {
                    _environment = new RuntimeEnvironment(_environment);
                    _environment.Define("super", superclass);
                }

                Dictionary<string, SharpTSFunction> methods = [];
                Dictionary<string, SharpTSFunction> staticMethods = [];
                Dictionary<string, object?> staticProperties = [];

                // Evaluate static property initializers at class definition time
                foreach (Stmt.Field field in classStmt.Fields)
                {
                    if (field.IsStatic)
                    {
                        object? fieldValue = field.Initializer != null
                            ? Evaluate(field.Initializer)
                            : null;
                        staticProperties[field.Name.Lexeme] = fieldValue;
                    }
                }

                // Separate static and instance methods (skip overload signatures with no body)
                foreach (Stmt.Function method in classStmt.Methods.Where(m => m.Body != null))
                {
                    SharpTSFunction func = new(method, _environment);
                    if (method.IsStatic)
                    {
                        staticMethods[method.Name.Lexeme] = func;
                    }
                    else
                    {
                        methods[method.Name.Lexeme] = func;
                    }
                }

                // Create accessor functions
                Dictionary<string, SharpTSFunction> getters = [];
                Dictionary<string, SharpTSFunction> setters = [];

                if (classStmt.Accessors != null)
                {
                    foreach (var accessor in classStmt.Accessors)
                    {
                        // Create a synthetic function for the accessor
                        var funcStmt = new Stmt.Function(
                            accessor.Name,
                            null,  // No type parameters for accessor
                            accessor.SetterParam != null ? [accessor.SetterParam] : [],
                            accessor.Body,
                            accessor.ReturnType);

                        SharpTSFunction func = new(funcStmt, _environment);

                        if (accessor.Kind.Type == TokenType.GET)
                        {
                            getters[accessor.Name.Lexeme] = func;
                        }
                        else
                        {
                            setters[accessor.Name.Lexeme] = func;
                        }
                    }
                }

                SharpTSClass klass = new(
                    classStmt.Name.Lexeme,
                    (SharpTSClass?)superclass,
                    methods,
                    staticMethods,
                    staticProperties,
                    getters,
                    setters,
                    classStmt.IsAbstract);

                if (classStmt.Superclass != null)
                {
                    _environment = _environment.Enclosing!;
                }

                _environment.Assign(classStmt.Name, klass);
                break;
            case Stmt.TypeAlias:
                // Type aliases are compile-time only, no runtime effect
                break;
            case Stmt.Enum enumStmt:
                ExecuteEnumDeclaration(enumStmt);
                break;
            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null) returnValue = Evaluate(returnStmt.Value);
                throw new ReturnException(returnValue);
            case Stmt.Print printStmt:
                Console.WriteLine(Stringify(Evaluate(printStmt.Expr)));
                break;
        }
    }
}
