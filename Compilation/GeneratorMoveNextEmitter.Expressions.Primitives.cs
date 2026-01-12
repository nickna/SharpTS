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

        // Check if hoisted to state machine field
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, field);
            SetStackUnknown();
            return;
        }

        // Check if it's a local variable
        if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Ldloc, local);
            SetStackUnknown();
            return;
        }

        // Check if it's a function
        if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var funcMethod))
        {
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Ldtoken, funcMethod);
            _il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
            _il.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
            SetStackUnknown();
            return;
        }

        // Check if it's a namespace - load the static field
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

        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            var temp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, temp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, temp);
            _il.Emit(OpCodes.Stfld, field);
        }
        else if (_ctx!.Locals.TryGetLocal(name, out var local))
        {
            _il.Emit(OpCodes.Stloc, local);
        }

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
