using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    private void EmitLiteral(Expr.Literal l)
    {
        switch (l.Value)
        {
            case null:
                _il.Emit(OpCodes.Ldnull);
                SetStackType(StackType.Null);
                break;
            case double d:
                _il.Emit(OpCodes.Ldc_R8, d);
                SetStackType(StackType.Double);
                break;
            case bool b:
                _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                SetStackType(StackType.Boolean);
                break;
            case string s:
                _il.Emit(OpCodes.Ldstr, s);
                SetStackType(StackType.String);
                break;
            default:
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    private void EmitVariable(Expr.Variable v)
    {
        string name = v.Name.Lexeme;

        // Try resolver first (hoisted fields and non-hoisted locals)
        var stackType = _resolver!.TryLoadVariable(name);
        if (stackType != null)
        {
            SetStackType(stackType.Value);
            return;
        }

        // Fallback: Check if it's a function
        if (_ctx!.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod))
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Fallback: Check if it's a namespace - load the static field
        if (_ctx.NamespaceFields?.TryGetValue(name, out var nsField) == true)
        {
            _il.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return;
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackUnknown();
    }

    private void EmitAssign(Expr.Assign a)
    {
        string name = a.Name.Lexeme;

        EmitExpression(a.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);

        // Use resolver to store (consumes one copy, leaves one on stack as return value)
        _resolver!.TryStoreVariable(name);

        SetStackUnknown();
    }

    private void EmitArrowFunction(Expr.ArrowFunction af)
    {
        if (_ctx!.ArrowMethods == null || !_ctx.ArrowMethods.TryGetValue(af, out var method))
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
            return;
        }

        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Ldtoken, method);
        _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        SetStackUnknown();
    }
}
