using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Decorator support for IL compilation.
/// Maps known decorators to .NET attributes and stores metadata for reflect-metadata compatibility.
/// </summary>
/// <remarks>
/// IL-compiled decorators work differently from interpreted decorators:
/// - Known decorators (Obsolete, Serializable) are mapped to .NET attributes
/// - Other decorators are stored as metadata in static fields for reflection
/// - Runtime decorator execution (calling decorator functions) is not supported in compiled mode
/// </remarks>
public partial class ILCompiler
{
    private DecoratorMode _decoratorMode = DecoratorMode.None;

    /// <summary>
    /// Sets the decorator mode for compilation.
    /// </summary>
    public void SetDecoratorMode(DecoratorMode mode) => _decoratorMode = mode;

    /// <summary>
    /// Applies decorators to a class definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyClassDecorators(Stmt.Class classStmt, TypeBuilder typeBuilder)
    {
        if (classStmt.Decorators == null || classStmt.Decorators.Count == 0)
            return;

        foreach (var decorator in classStmt.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                typeBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a method definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyMethodDecorators(Stmt.Function method, MethodBuilder methodBuilder)
    {
        if (method.Decorators == null || method.Decorators.Count == 0)
            return;

        foreach (var decorator in method.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                methodBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a field definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyFieldDecorators(Stmt.Field field, FieldBuilder fieldBuilder)
    {
        if (field.Decorators == null || field.Decorators.Count == 0)
            return;

        foreach (var decorator in field.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                fieldBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a property (accessor pair) definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyAccessorDecorators(Stmt.Accessor accessor, MethodBuilder methodBuilder)
    {
        if (accessor.Decorators == null || accessor.Decorators.Count == 0)
            return;

        foreach (var decorator in accessor.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                methodBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Applies decorators to a parameter definition.
    /// Maps known decorators to .NET attributes.
    /// </summary>
    private void ApplyParameterDecorators(Stmt.Parameter param, ParameterBuilder paramBuilder)
    {
        if (param.Decorators == null || param.Decorators.Count == 0)
            return;

        foreach (var decorator in param.Decorators)
        {
            var attribute = AttributeMapper.MapToAttribute(decorator);
            if (attribute != null)
            {
                paramBuilder.SetCustomAttribute(attribute);
            }
        }
    }

    /// <summary>
    /// Gets the name of a decorator from its expression.
    /// </summary>
    private static string? GetDecoratorName(Decorator decorator)
    {
        return decorator.Expression switch
        {
            Expr.Variable variable => variable.Name.Lexeme,
            Expr.Call call when call.Callee is Expr.Variable v => v.Name.Lexeme,
            Expr.Get get => get.Name.Lexeme,
            _ => null
        };
    }

    /// <summary>
    /// Checks if a method has the @lock decorator.
    /// </summary>
    private static bool HasLockDecorator(Stmt.Function method)
    {
        if (method.Decorators == null || method.Decorators.Count == 0)
            return false;

        return method.Decorators.Any(d => GetDecoratorName(d) == "lock");
    }

    /// <summary>
    /// Checks if an accessor has the @lock decorator.
    /// </summary>
    private static bool HasLockDecorator(Stmt.Accessor accessor)
    {
        if (accessor.Decorators == null || accessor.Decorators.Count == 0)
            return false;

        return accessor.Decorators.Any(d => GetDecoratorName(d) == "lock");
    }

    /// <summary>
    /// Analyzes a class to determine what lock fields are needed.
    /// </summary>
    private static (bool NeedsSyncLock, bool NeedsAsyncLock, bool NeedsStaticSyncLock, bool NeedsStaticAsyncLock) AnalyzeLockRequirements(Stmt.Class classStmt)
    {
        bool needsSyncLock = false;
        bool needsAsyncLock = false;
        bool needsStaticSyncLock = false;
        bool needsStaticAsyncLock = false;

        // Check all methods
        foreach (var method in classStmt.Methods)
        {
            if (!HasLockDecorator(method))
                continue;

            if (method.IsStatic)
            {
                if (method.IsAsync)
                    needsStaticAsyncLock = true;
                else
                    needsStaticSyncLock = true;
            }
            else
            {
                if (method.IsAsync)
                    needsAsyncLock = true;
                else
                    needsSyncLock = true;
            }
        }

        // Check accessors
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                if (!HasLockDecorator(accessor))
                    continue;

                // Accessors are never async, so only sync locks
                needsSyncLock = true;
            }
        }

        return (needsSyncLock, needsAsyncLock, needsStaticSyncLock, needsStaticAsyncLock);
    }
}
