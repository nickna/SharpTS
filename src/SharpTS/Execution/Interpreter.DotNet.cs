using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// Interpreter-side support for <c>@DotNetType</c> external type bindings.
/// Mirrors the compile-time wiring in <c>ILCompiler.Classes.cs</c>: extracts the
/// decorator argument, resolves the CLR type, and registers a <see cref="DotNetClass"/>
/// in the runtime environment under the TypeScript name.
/// </summary>
public partial class Interpreter
{
    /// <summary>
    /// If the class statement is an <c>@DotNetType declare class</c>, resolves the
    /// mapped CLR type and binds a <see cref="DotNetClass"/> into the environment.
    /// </summary>
    /// <returns>True if handled; false to fall through to the normal class path.</returns>
    private bool TryRegisterDotNetType(Stmt.Class classStmt)
    {
        string? mapping = ExtractDotNetTypeMapping(classStmt);
        if (mapping == null) return false;

        Type? type = DotNetTypeRegistry.ResolveFriendly(mapping);
        if (type == null)
        {
            throw new InterpreterException(
                $"@DotNetType: .NET type '{mapping}' not found in any loaded assembly.");
        }

        var overloadHints = ExtractOverloadHints(classStmt);
        var wrapper = new DotNetClass(classStmt.Name.Lexeme, type, overloadHints);
        _environment.Define(classStmt.Name.Lexeme, wrapper);
        return true;
    }

    /// <summary>
    /// Extracts the <c>@DotNetType("...")</c> argument, or null if absent.
    /// </summary>
    private static string? ExtractDotNetTypeMapping(Stmt.Class classStmt)
    {
        if (classStmt.Decorators == null) return null;

        foreach (var decorator in classStmt.Decorators)
        {
            if (decorator.Expression is Expr.Call call &&
                call.Callee is Expr.Variable v &&
                v.Name.Lexeme == "DotNetType" &&
                call.Arguments.Count == 1 &&
                call.Arguments[0] is Expr.Literal { Value: string typeName })
            {
                return typeName;
            }
        }
        return null;
    }

    /// <summary>
    /// Collects <c>@DotNetOverload("...")</c> hints keyed by method name
    /// (<c>"constructor"</c> for the constructor).
    /// </summary>
    private static IReadOnlyDictionary<string, string> ExtractOverloadHints(Stmt.Class classStmt)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var method in classStmt.Methods)
        {
            if (method.Decorators == null) continue;
            foreach (var decorator in method.Decorators)
            {
                if (decorator.Expression is Expr.Call call &&
                    call.Callee is Expr.Variable v &&
                    v.Name.Lexeme == "DotNetOverload" &&
                    call.Arguments.Count == 1 &&
                    call.Arguments[0] is Expr.Literal { Value: string hint })
                {
                    result[method.Name.Lexeme] = hint;
                    break;
                }
            }
        }

        return result;
    }
}
