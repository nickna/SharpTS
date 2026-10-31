using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    // Accessor and data definitions must replace each other at their source
    // positions. Ordinary data-only literals retain their dictionary fast path.
    private async ValueTask<object?> EvaluateAccessorLiteralCore(IEvaluationContext context, Expr.ObjectLiteral literal)
    {
        var result = new SharpTSObject([]);
        foreach (var property in literal.Properties)
        {
            if (property.IsSpread)
            {
                var source = (await context.EvaluateExprAsync(property.Value)).ToObject();
                Dictionary<string, object?> strings = [];
                var symbols = ApplySpreadToFields(source, strings, null);
                foreach (var pair in strings)
                    DefineLiteralProperty(result, pair.Key, Expr.ObjectPropertyKind.Value, pair.Value);
                if (symbols is not null)
                    foreach (var pair in symbols)
                        DefineLiteralProperty(result, pair.Key, Expr.ObjectPropertyKind.Value, pair.Value);
                continue;
            }
            if (property.IsPrototypeSetter)
            {
                var prototype = await context.EvaluateExprAsync(property.Value);
                if (prototype.Kind is ValueKind.Null or ValueKind.Object)
                {
                    result.Prototype = prototype.ToObject();
                    result.IsNullPrototype = prototype.Kind == ValueKind.Null;
                }
                continue;
            }
            var key = await GetPropertyKeyNameCore(context, property.Key!);
            object? value = property.Kind switch
            {
                Expr.ObjectPropertyKind.Getter => CreateAccessorFunction(property.Value),
                Expr.ObjectPropertyKind.Setter => CreateSetterFunction(property.Value, property.SetterParam!),
                _ => (await context.EvaluateExprAsync(property.Value)).ToObject(),
            };
            DefineLiteralProperty(result, key, property.Kind, value);
        }
        return result;
    }

    private static void DefineLiteralProperty(SharpTSObject target, object key, Expr.ObjectPropertyKind kind, object? value)
    {
        var descriptor = new SharpTSPropertyDescriptor
        {
            Enumerable = true, HasEnumerable = true,
            Configurable = true, HasConfigurable = true,
        };
        if (kind == Expr.ObjectPropertyKind.Getter)
        {
            descriptor.Get = (ISharpTSCallable)value!;
            descriptor.HasGet = true;
        }
        else if (kind == Expr.ObjectPropertyKind.Setter)
        {
            descriptor.Set = (ISharpTSCallable)value!;
            descriptor.HasSet = true;
        }
        else
        {
            descriptor.Value = value;
            descriptor.HasValue = true;
            descriptor.Writable = true;
            descriptor.HasWritable = true;
        }
        if (key is SharpTSSymbol symbol)
            target.DefineProperty(symbol, descriptor);
        else
            target.DefineProperty((string)key, descriptor);
    }
}
