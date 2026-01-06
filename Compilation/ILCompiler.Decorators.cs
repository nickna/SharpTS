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
}
